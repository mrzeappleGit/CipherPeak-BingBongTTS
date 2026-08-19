using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CipherPeak.Core.Filtering
{
    /// <summary>What to do with a message containing profanity.</summary>
    public enum ProfanityAction
    {
        /// <summary>No profanity handling at all.</summary>
        Off,

        /// <summary>Replace each match with the configured mask word and speak the rest.</summary>
        Mask,

        /// <summary>Drop the whole message.</summary>
        Block
    }

    /// <summary>
    /// Word-stem profanity matching.
    ///
    /// Stems match on word boundaries plus a short list of common suffixes, so "fuck" also catches
    /// "fucking" without catching "Scunthorpe" or "grass". That is the whole trade: no leetspeak
    /// normalisation, no character substitution, no fuzzy distance. Chat that wants past a filter
    /// always gets past a filter; this exists so ordinary swearing does not go out over the stream.
    /// </summary>
    public sealed class ProfanityMatcher
    {
        /// <summary>
        /// Starter list. Deliberately short and stem-based - <see cref="Config.FilterSettings.ProfanityWords"/>
        /// extends it, which is where channel-specific additions belong.
        /// </summary>
        public static readonly string[] DefaultWords =
        {
            "fuck", "shit", "bitch", "cunt", "asshole", "arsehole", "bastard",
            "dick", "cock", "pussy", "whore", "slut", "wank", "twat", "prick",
            "damn", "crap",
            "nigger", "nigga", "faggot", "fag", "retard", "tranny", "spic", "kike", "chink"
        };

        // Suffixes that keep a stem a swear rather than turning it into an unrelated word. The
        // optional letter in front absorbs consonant doubling, so "shit" also catches "shitting".
        private const string SuffixGroup = "[a-z]?(?:s|es|ed|ing|er|ers|ry|y|ies|head|hole)?";

        private readonly Regex _pattern;

        public ProfanityMatcher(IEnumerable<string> extraWords, bool includeDefaults = true)
        {
            var stems = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (includeDefaults)
                foreach (var word in DefaultWords) Add(stems, seen, word);

            if (extraWords != null)
                foreach (var word in extraWords) Add(stems, seen, word);

            if (stems.Count == 0)
            {
                _pattern = null;
                return;
            }

            // \b on both ends: a stem only matches as its own word, never inside a longer innocent one.
            _pattern = new Regex(
                @"\b(?:" + string.Join("|", stems.ToArray()) + @")" + SuffixGroup + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        /// <summary>True when nothing was configured and every call is therefore a no-op.</summary>
        public bool IsEmpty => _pattern == null;

        public bool IsMatch(string text) =>
            _pattern != null && !string.IsNullOrEmpty(text) && _pattern.IsMatch(text);

        /// <summary>Replaces every match with <paramref name="replacement"/>. Returns the input unchanged when clean.</summary>
        public string Mask(string text, string replacement)
        {
            if (_pattern == null || string.IsNullOrEmpty(text)) return text;
            return _pattern.Replace(text, replacement ?? "");
        }

        private static void Add(List<string> stems, HashSet<string> seen, string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            word = word.Trim();
            if (!seen.Add(word)) return;
            stems.Add(Regex.Escape(word));
        }
    }
}
