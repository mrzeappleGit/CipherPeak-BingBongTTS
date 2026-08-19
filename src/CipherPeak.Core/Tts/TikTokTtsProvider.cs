using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CipherPeak.Core.Config;
using CipherPeak.Core.Net;

namespace CipherPeak.Core.Tts
{
    /// <summary>
    /// TikTok publishes no documented, permitted text-to-speech API. Every "TikTok TTS" library in
    /// the wild calls a private mobile endpoint with a borrowed session cookie: undocumented, rate
    /// limited without notice, and against TikTok's terms.
    ///
    /// This mod therefore ships the provider WITHOUT any endpoint baked in. It reports itself
    /// unavailable unless the operator supplies both TikTokEndpoint and TikTokSessionId themselves,
    /// and it never scrapes or discovers a host on its own. With nothing configured, selecting
    /// "tiktok" degrades to a clear log line plus the ElevenLabs fallback instead of a silent
    /// failure or a broken scraper. See README, "TikTok limitation".
    ///
    /// The request shape below is the one every community endpoint expects
    /// (form-encoded text_speaker + req_text, JSON reply with data.v_str base64 audio), so a
    /// self-hosted proxy works, but nothing is assumed about availability.
    /// </summary>
    public sealed class TikTokTtsProvider : ITtsProvider
    {
        public const string ProviderName = "tiktok";

        private readonly IHttpTransport _http;
        private readonly Func<ModSettings> _settings;

        public TikTokTtsProvider(IHttpTransport http, Func<ModSettings> settings)
        {
            _http = http;
            _settings = settings;
        }

        public string Name => ProviderName;

        public bool IsAvailable(out string reason)
        {
            var tts = _settings().Tts;
            if (string.IsNullOrWhiteSpace(tts.TikTokEndpoint))
            {
                reason = "TikTok has no public, permitted TTS API; set Tts.TikTokEndpoint to your own " +
                         "proxy to enable it (see README, TikTok limitation)";
                return false;
            }
            if (string.IsNullOrWhiteSpace(tts.TikTokSessionId))
            {
                reason = "TikTok endpoint configured but TikTokSessionId is empty";
                return false;
            }
            reason = null;
            return true;
        }

        public async Task<TtsResult> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken)
        {
            string reason;
            if (!IsAvailable(out reason)) return TtsResult.Fail(reason);
            if (string.IsNullOrWhiteSpace(voiceId)) return TtsResult.Fail("no TikTok voice id for that alias");
            if (string.IsNullOrWhiteSpace(text)) return TtsResult.Fail("empty text");

            var tts = _settings().Tts;

            string url = tts.TikTokEndpoint
                         + (tts.TikTokEndpoint.IndexOf('?') >= 0 ? "&" : "?")
                         + "text_speaker=" + Uri.EscapeDataString(voiceId)
                         + "&req_text=" + Uri.EscapeDataString(text);

            var headers = new Dictionary<string, string>
            {
                { "Cookie", "sessionid=" + tts.TikTokSessionId }
            };

            var response = await _http
                .PostAsync(url, headers, "application/x-www-form-urlencoded", Array.Empty<byte>(),
                    tts.RequestTimeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (response.TransportError != null)
                return TtsResult.Fail("TikTok transport failure (" + response.TransportError + ")");

            if (response.IsRateLimited)
                return TtsResult.Fail("TikTok rate limited (429)",
                    response.RetryAfterSeconds > 0 ? response.RetryAfterSeconds : 10);

            if (!response.IsSuccess)
                return TtsResult.Fail("TikTok HTTP " + response.StatusCode);

            string payload;
            try
            {
                payload = Encoding.UTF8.GetString(response.Body ?? Array.Empty<byte>());
            }
            catch (Exception ex)
            {
                return TtsResult.Fail("TikTok response was not text (" + ex.Message + ")");
            }

            string base64 = ExtractVStr(payload);
            if (base64 == null)
                return TtsResult.Fail("TikTok returned a malformed response (no data.v_str); " +
                                      "the private endpoint has most likely changed or revoked access");

            byte[] audio;
            try
            {
                audio = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return TtsResult.Fail("TikTok returned malformed base64 audio");
            }

            if (audio.Length == 0) return TtsResult.Fail("TikTok returned empty audio");
            if (audio.Length > tts.MaxAudioBytes)
                return TtsResult.Fail("TikTok audio exceeded MaxAudioBytes (" + audio.Length + " bytes)");

            return TtsResult.Ok(audio);
        }

        /// <summary>Pulls "v_str":"..." out of the reply without adding a JSON dependency.</summary>
        internal static string ExtractVStr(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            const string key = "\"v_str\"";
            int k = json.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return null;

            int colon = json.IndexOf(':', k + key.Length);
            if (colon < 0) return null;

            int open = json.IndexOf('"', colon + 1);
            if (open < 0) return null;

            int close = json.IndexOf('"', open + 1);
            if (close < 0) return null;

            string value = json.Substring(open + 1, close - open - 1);
            return value.Length == 0 ? null : value;
        }
    }
}
