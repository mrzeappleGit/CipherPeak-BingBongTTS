using System;
using System.Collections.Generic;
using CipherPeak.Core.Config;

namespace CipherPeak.Core.Tts
{
    /// <summary>
    /// Maps chat-facing aliases to provider voice ids. Chat only ever sees aliases, so provider
    /// voice ids never leak into the stream, and only allowlisted aliases can be selected.
    /// </summary>
    public sealed class VoiceRegistry
    {
        private readonly Func<ModSettings> _settings;
        private readonly Dictionary<string, string> _userChoice =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public VoiceRegistry(Func<ModSettings> settings) { _settings = settings; }

        public bool TryResolve(string alias, out VoiceAlias voice)
        {
            voice = null;
            if (string.IsNullOrWhiteSpace(alias)) return false;

            var aliases = _settings().Tts.VoiceAliases;
            if (aliases == null) return false;

            for (int i = 0; i < aliases.Count; i++)
            {
                if (aliases[i] != null &&
                    string.Equals(aliases[i].Alias, alias.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    voice = aliases[i];
                    return true;
                }
            }
            return false;
        }

        public IReadOnlyList<string> AllowedAliases()
        {
            var result = new List<string>();
            var aliases = _settings().Tts.VoiceAliases;
            if (aliases == null) return result;
            for (int i = 0; i < aliases.Count; i++)
                if (aliases[i] != null) result.Add(aliases[i].Alias);
            return result;
        }

        /// <summary>Returns false when the alias is not allowlisted; the caller tells the user which are.</summary>
        public bool TrySetUserVoice(string login, string alias)
        {
            VoiceAlias voice;
            if (!TryResolve(alias, out voice)) return false;
            _userChoice[login ?? ""] = voice.Alias;
            return true;
        }

        public void ClearUserVoices() { _userChoice.Clear(); }

        /// <summary>
        /// Alias for this user: their own choice if the voice command is enabled, otherwise
        /// <see cref="TtsSettings.DefaultVoiceAlias"/>. If that is misconfigured, the first
        /// allowlisted alias belonging to <see cref="TtsSettings.DefaultProvider"/> is used, so the
        /// broadcaster's provider choice still decides who speaks.
        /// </summary>
        public string AliasFor(string login)
        {
            var tts = _settings().Tts;

            string chosen;
            if (tts.AllowVoiceCommand && login != null && _userChoice.TryGetValue(login, out chosen))
                return chosen;

            VoiceAlias voice;
            if (TryResolve(tts.DefaultVoiceAlias, out voice)) return voice.Alias;

            var aliases = tts.VoiceAliases;
            if (aliases == null || aliases.Count == 0) return "";

            for (int i = 0; i < aliases.Count; i++)
                if (aliases[i] != null &&
                    string.Equals(aliases[i].Provider, tts.DefaultProvider, StringComparison.OrdinalIgnoreCase))
                    return aliases[i].Alias;

            return aliases[0].Alias;
        }
    }
}
