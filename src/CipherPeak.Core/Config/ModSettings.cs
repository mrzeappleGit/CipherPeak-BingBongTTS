using System;
using System.Collections.Generic;

namespace CipherPeak.Core.Config
{
    /// <summary>
    /// Plain settings snapshot. The BepInEx layer owns the .cfg file and copies values in here,
    /// so every rule below is unit-testable without Unity and changeable without recompiling.
    /// </summary>
    public sealed class ModSettings
    {
        public bool Enabled = true;
        public TwitchSettings Twitch = new TwitchSettings();
        public FilterSettings Filter = new FilterSettings();
        public QueueSettings Queue = new QueueSettings();
        public TtsSettings Tts = new TtsSettings();
        public BingBongSettings BingBong = new BingBongSettings();
        public AudioSettings Audio = new AudioSettings();
    }

    public sealed class TwitchSettings
    {
        public string Channel = "";

        /// <summary>Lowercase login of the account the OAuth token belongs to. Empty = anonymous read-only.</summary>
        public string Username = "";

        /// <summary>OAuth token, with or without the "oauth:" prefix. Empty = anonymous read-only login.</summary>
        public string OAuthToken = "";

        /// <summary>
        /// Read your own chat instead of listening to the host's, using your own channel, filters,
        /// voices and API key. Only you hear it - your chat is never broadcast to the lobby - so this
        /// costs nobody else anything and needs no agreement from the host. Off means the default:
        /// the host owns the chat and everyone hears theirs.
        /// </summary>
        public bool UseMyOwnChat = false;

        public string Host = "irc.chat.twitch.tv";
        public int Port = 6697;

        /// <summary>
        /// Unity's Mono runtime frequently ships without a usable root certificate store, which makes
        /// strict TLS validation fail for reasons unrelated to the connection being safe. When true the
        /// client retries once with validation relaxed and logs a warning. Set false to hard-fail instead.
        /// </summary>
        public bool AllowInsecureTls = true;

        public double ReconnectDelaySeconds = 3;
        public double MaxReconnectDelaySeconds = 120;
    }

    public sealed class FilterSettings
    {
        public int MinMessageLength = 2;
        public int MaxMessageLength = 200;

        /// <summary>Messages starting with any of these are treated as bot/mod commands and dropped.</summary>
        public List<string> CommandPrefixes = new List<string> { "!", "/", ".", "?" };

        public List<string> IgnoredUsers = new List<string>
            { "nightbot", "streamelements", "streamlabs", "moobot", "fossabot", "sery_bot" };

        public List<string> BlockedWords = new List<string>();

        /// <summary>Off, Mask (bleep the word) or Block (drop the message).</summary>
        public Filtering.ProfanityAction Profanity = Filtering.ProfanityAction.Off;

        /// <summary>
        /// Extends the built-in stem list in <see cref="Filtering.ProfanityMatcher.DefaultWords"/>.
        /// ponytail: extend only - there is no way to remove a built-in stem. Add a
        /// ProfanityAllowWords list if a real false positive ever shows up in chat.
        /// </summary>
        public List<string> ProfanityWords = new List<string>();

        /// <summary>Spoken in place of each match when <see cref="Profanity"/> is Mask.</summary>
        public string ProfanityMask = "beep";

        public bool BlockLinks = true;

        /// <summary>How many recent messages are remembered for duplicate suppression. 0 disables it.</summary>
        public int DuplicateHistorySize = 25;

        public bool SubscribersOnly = false;
        public bool ModeratorsOnly = false;

        /// <summary>Broadcaster and moderators bypass length limits, cooldowns and sub-only gating.</summary>
        public bool ModeratorsBypassLimits = true;
    }

    public sealed class QueueSettings
    {
        public int MaxQueuedMessages = 10;
        public double PerUserCooldownSeconds = 20;
        public double GlobalCooldownSeconds = 0;

        /// <summary>Silence inserted between two spoken messages.</summary>
        public double GapBetweenMessagesSeconds = 0.5;
    }

    public sealed class TtsSettings
    {
        /// <summary>"elevenlabs" or "tiktok".</summary>
        public string DefaultProvider = "elevenlabs";

        /// <summary>Alias from <see cref="VoiceAliases"/> used when the chatter picks nothing.</summary>
        public string DefaultVoiceAlias = "default";

        /// <summary>Allow "!voice &lt;alias&gt;" in chat to switch a user's voice.</summary>
        public bool AllowVoiceCommand = false;

        /// <summary>Aliases the !voice command may select. Aliases hide provider voice ids from chat.</summary>
        public List<VoiceAlias> VoiceAliases = new List<VoiceAlias>();

        public string ElevenLabsApiKey = "";
        public string ElevenLabsModelId = "eleven_multilingual_v2";
        public string ElevenLabsOutputFormat = "mp3_44100_64";
        public double ElevenLabsStability = 0.5;
        public double ElevenLabsSimilarityBoost = 0.75;

        /// <summary>
        /// TikTok has no public, documented, permitted TTS API. Leave empty and the TikTok provider
        /// reports itself unavailable instead of scraping. See README "TikTok limitation".
        /// </summary>
        public string TikTokEndpoint = "";
        public string TikTokSessionId = "";

        public int RequestTimeoutSeconds = 20;
        public int MaxRetries = 2;

        /// <summary>Fall back to the other configured provider when the selected one fails.</summary>
        public bool FallbackToOtherProvider = true;

        public bool CacheEnabled = true;
        public int CacheMaxMegabytes = 64;

        /// <summary>Hard ceiling on a single synthesized clip. Larger responses are rejected.</summary>
        public int MaxAudioBytes = 512 * 1024;
    }

    public sealed class BingBongSettings
    {
        /// <summary>Resources path of the Bing Bong item prefab. Empty = auto-detect from the item database.</summary>
        public string PrefabNameOverride = "";

        public double SpawnRadiusMeters = 3.0;
        public double SpawnHeightOffsetMeters = 1.5;

        /// <summary>How often the "exactly two" reconciliation runs.</summary>
        public double LifecycleTickSeconds = 3.0;

        /// <summary>
        /// Beyond this distance from the nearest living scout a Bing Bong counts as lost and is
        /// replaced. 0 disables it, which is the default: one you walked away from is where you left
        /// it, and replacing it means a new one appearing beside the party every time it moves on.
        /// </summary>
        public double MaxDistanceFromPlayersMeters = 0;

        /// <summary>
        /// This many metres below the nearest scout a Bing Bong counts as out of bounds and is
        /// replaced. 0 disables it. In a climbing game this fires on ordinary progress, not just on
        /// a fall, which is why it is off by default.
        /// </summary>
        public double OutOfBoundsDropMeters = 0;

        /// <summary>
        /// Carry a Bing Bong without it counting toward your carry weight, in any slot.
        /// Off by default: it changes vanilla balance, so it should be a decision, not a surprise.
        /// </summary>
        public bool Weightless = false;

        /// <summary>
        /// Give the Bing Bong its own inventory slot instead of spending one of the normal three.
        /// The slot sits outside the game's weight sum, so anything in it is weightless regardless
        /// of <see cref="Weightless"/>.
        /// </summary>
        public bool DedicatedSlot = true;

        /// <summary>
        /// Horizontal nudge for the slot's HUD widget, in canvas units. 0 auto-places it beside the
        /// game's temporary slot. Set it if the HUD layout puts the widget somewhere unhelpful.
        /// </summary>
        public double SlotUiOffsetX = 0;
    }

    public sealed class AudioSettings
    {
        /// <summary>1 matches the game's own sounds; above that amplifies, up to a ceiling of 3.</summary>
        public double Volume = 1.0;

        /// <summary>How far the voice carries at full volume before it starts falling off.</summary>
        public double MinDistance = 10.0;

        /// <summary>Distance at which the voice is inaudible.</summary>
        public double MaxDistance = 60.0;

        /// <summary>Animate the Bing Bong's mouth to the spoken audio.</summary>
        public bool AnimateMouth = true;
    }

    public sealed class VoiceAlias
    {
        public string Alias;
        public string Provider;
        public string VoiceId;

        public VoiceAlias() { }

        public VoiceAlias(string alias, string provider, string voiceId)
        {
            Alias = alias;
            Provider = provider;
            VoiceId = voiceId;
        }

        /// <summary>Parses "alias=provider:voiceId". Returns null when the line is blank or malformed.</summary>
        public static VoiceAlias Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            if (line.StartsWith("#")) return null;

            int eq = line.IndexOf('=');
            if (eq <= 0 || eq == line.Length - 1) return null;

            string alias = line.Substring(0, eq).Trim();
            string rest = line.Substring(eq + 1).Trim();

            int colon = rest.IndexOf(':');
            if (colon <= 0 || colon == rest.Length - 1) return null;

            string provider = rest.Substring(0, colon).Trim().ToLowerInvariant();
            string voiceId = rest.Substring(colon + 1).Trim();
            if (alias.Length == 0 || provider.Length == 0 || voiceId.Length == 0) return null;

            return new VoiceAlias(alias.ToLowerInvariant(), provider, voiceId);
        }

        public static List<VoiceAlias> ParseAll(IEnumerable<string> lines)
        {
            var result = new List<VoiceAlias>();
            if (lines == null) return result;
            foreach (var line in lines)
            {
                var parsed = Parse(line);
                if (parsed == null) continue;
                result.RemoveAll(v => string.Equals(v.Alias, parsed.Alias, StringComparison.OrdinalIgnoreCase));
                result.Add(parsed);
            }
            return result;
        }

        /// <summary>Splits a comma-separated config string into aliases.</summary>
        public static List<VoiceAlias> ParseCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new List<VoiceAlias>();
            return ParseAll(csv.Split(','));
        }
    }
}
