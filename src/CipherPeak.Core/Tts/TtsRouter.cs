using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CipherPeak.Core.Config;
using CipherPeak.Core.Logging;

namespace CipherPeak.Core.Tts
{
    /// <summary>
    /// Turns "speak this text in this alias" into audio bytes: alias resolution, provider selection,
    /// bounded retries with backoff, cross-provider fallback and caching. Never throws.
    /// </summary>
    public sealed class TtsRouter
    {
        private readonly Func<ModSettings> _settings;
        private readonly VoiceRegistry _voices;
        private readonly IReadOnlyList<ITtsProvider> _providers;
        private readonly IAudioCache _cache;
        private readonly ILog _log;

        /// <summary>Set by a 429; further calls to that provider are skipped until it passes.</summary>
        private readonly Dictionary<string, DateTimeOffset> _cooldownUntil =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        public TtsRouter(
            Func<ModSettings> settings,
            VoiceRegistry voices,
            IReadOnlyList<ITtsProvider> providers,
            IAudioCache cache = null,
            ILog log = null)
        {
            _settings = settings;
            _voices = voices;
            _providers = providers;
            _cache = cache ?? NullAudioCache.Instance;
            _log = log ?? NullLog.Instance;
        }

        public async Task<TtsResult> SynthesizeAsync(string text, string alias, CancellationToken cancellationToken)
        {
            var tts = _settings().Tts;

            VoiceAlias voice;
            if (!_voices.TryResolve(alias, out voice))
            {
                if (!_voices.TryResolve(tts.DefaultVoiceAlias, out voice))
                    return TtsResult.Fail("no usable voice alias configured");
            }

            string cacheKey = AudioCacheKey.For(voice.Provider, voice.VoiceId, text);
            byte[] cached;
            if (tts.CacheEnabled && _cache.TryGet(cacheKey, out cached))
                return TtsResult.Ok(cached);

            var order = ProviderOrder(voice);
            TtsResult last = TtsResult.Fail("no TTS provider available");

            foreach (var attempt in order)
            {
                last = await CallWithRetries(attempt.Provider, text, attempt.VoiceId, cancellationToken)
                    .ConfigureAwait(false);

                if (last.Success)
                {
                    if (tts.CacheEnabled) _cache.Put(cacheKey, last.Audio);
                    return last;
                }

                _log.Warn("TTS provider '" + attempt.Provider.Name + "' failed: " + last.Error);
                if (!tts.FallbackToOtherProvider) break;
            }

            return last;
        }

        private struct Attempt
        {
            public ITtsProvider Provider;
            public string VoiceId;
        }

        /// <summary>Requested provider first, then any other available provider that has a voice for this alias.</summary>
        private List<Attempt> ProviderOrder(VoiceAlias voice)
        {
            var order = new List<Attempt>();
            var now = DateTimeOffset.UtcNow;

            foreach (var provider in _providers)
            {
                if (provider == null) continue;

                bool isPrimary = string.Equals(provider.Name, voice.Provider, StringComparison.OrdinalIgnoreCase);

                string reason;
                if (!provider.IsAvailable(out reason))
                {
                    if (isPrimary) _log.Warn("TTS provider '" + provider.Name + "' unavailable: " + reason);
                    continue;
                }

                DateTimeOffset until;
                if (_cooldownUntil.TryGetValue(provider.Name, out until) && now < until)
                {
                    _log.Info("Skipping '" + provider.Name + "', rate limited for another "
                              + (int)(until - now).TotalSeconds + "s");
                    continue;
                }

                // Only the primary provider knows this alias' voice id. A fallback provider gets
                // the alias' own default voice for that provider, if one exists.
                string voiceId = isPrimary ? voice.VoiceId : FallbackVoiceIdFor(provider.Name);
                if (string.IsNullOrEmpty(voiceId)) continue;

                var attempt = new Attempt { Provider = provider, VoiceId = voiceId };
                if (isPrimary) order.Insert(0, attempt);
                else order.Add(attempt);
            }

            return order;
        }

        private string FallbackVoiceIdFor(string providerName)
        {
            var aliases = _settings().Tts.VoiceAliases;
            if (aliases == null) return null;
            for (int i = 0; i < aliases.Count; i++)
                if (aliases[i] != null &&
                    string.Equals(aliases[i].Provider, providerName, StringComparison.OrdinalIgnoreCase))
                    return aliases[i].VoiceId;
            return null;
        }

        private async Task<TtsResult> CallWithRetries(
            ITtsProvider provider, string text, string voiceId, CancellationToken cancellationToken)
        {
            int maxRetries = Math.Max(0, _settings().Tts.MaxRetries);
            TtsResult result = TtsResult.Fail("not attempted");

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) return TtsResult.Fail("cancelled");

                try
                {
                    result = await provider.SynthesizeAsync(text, voiceId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // A provider must never take the game down.
                    result = TtsResult.Fail(provider.Name + " threw " + ex.GetType().Name + ": " + ex.Message);
                }

                if (result.Success) return result;

                if (result.RetryAfterSeconds > 0)
                    _cooldownUntil[provider.Name] = DateTimeOffset.UtcNow.AddSeconds(result.RetryAfterSeconds);

                if (attempt == maxRetries) break;

                double delay = Math.Max(result.RetryAfterSeconds, Math.Pow(2, attempt) * 0.5);
                if (delay > 0)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 10)), cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return TtsResult.Fail("cancelled"); }
                }
            }

            return result;
        }
    }
}
