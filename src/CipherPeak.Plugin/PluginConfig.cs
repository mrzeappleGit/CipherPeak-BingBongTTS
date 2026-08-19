using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using CipherPeak.Core.Config;
using CipherPeak.Core.Filtering;
using CipherPeak.Core.Logging;
using UnityEngine;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Owns the BepInEx .cfg and projects it into a plain <see cref="ModSettings"/> snapshot that
    /// Core consumes. Everything here is data only: no behaviour depends on the file format.
    /// </summary>
    internal sealed class PluginConfig
    {
        private readonly ConfigFile _file;
        private readonly ILog _log;

        private ConfigEntry<bool> _enabled;

        private ConfigEntry<string> _channel, _twitchUser, _twitchToken;
        private ConfigEntry<bool> _allowInsecureTls, _useOwnChat;

        private ConfigEntry<int> _minLength, _maxLength, _duplicateHistory;
        private ConfigEntry<string> _commandPrefixes, _ignoredUsers, _blockedWords;
        private ConfigEntry<bool> _blockLinks, _subsOnly, _modsOnly, _modBypass;
        private ConfigEntry<ProfanityAction> _profanity;
        private ConfigEntry<string> _profanityWords, _profanityMask;

        private ConfigEntry<int> _maxQueued;
        private ConfigEntry<float> _userCooldown, _globalCooldown, _messageGap;

        private ConfigEntry<string> _provider, _defaultVoice, _voiceAliases;
        private ConfigEntry<bool> _allowVoiceCommand, _fallbackProvider, _cacheEnabled;
        private ConfigEntry<string> _elevenKey, _elevenModel, _elevenFormat;
        private ConfigEntry<float> _elevenStability, _elevenSimilarity;
        private ConfigEntry<string> _tiktokEndpoint, _tiktokSession;
        private ConfigEntry<int> _timeout, _retries, _cacheMb, _maxAudioBytes;

        private ConfigEntry<string> _prefabOverride;
        private ConfigEntry<float> _spawnRadius, _spawnHeight, _tickSeconds, _maxDistance, _outOfBoundsDrop;
        private ConfigEntry<bool> _weightless, _dedicatedSlot;
        private ConfigEntry<float> _slotUiOffsetX;

        private ConfigEntry<float> _volume, _minDistance, _audioMaxDistance;
        private ConfigEntry<bool> _animateMouth;

        private ConfigEntry<KeyboardShortcut> _skipKey, _clearKey, _toggleKey, _bingBongSlotKey, _backpackKey;

        private ModSettings _snapshot = new ModSettings();

        public PluginConfig(ConfigFile file, ILog log)
        {
            _file = file;
            _log = log;
            Bind();
            Rebuild();
            _file.SettingChanged += (s, e) => Rebuild();
        }

        public ModSettings Settings => _snapshot;

        public KeyboardShortcut SkipKey => _skipKey.Value;
        public KeyboardShortcut ClearKey => _clearKey.Value;
        public KeyboardShortcut ToggleKey => _toggleKey.Value;
        public KeyboardShortcut BingBongSlotKey => _bingBongSlotKey.Value;
        public KeyboardShortcut BackpackKey => _backpackKey.Value;

        /// <summary>Runtime toggle from a chat command or hotkey. Persisted so it survives a restart.</summary>
        public void SetEnabled(bool value)
        {
            _enabled.Value = value;
            Rebuild();
        }

        /// <summary>Runtime volume change from the options-screen slider. Persisted to the .cfg.</summary>
        public void SetVolume(float value)
        {
            if (_volume == null) return;
            _volume.Value = Mathf.Clamp(value, 0f, TtsPlaybackManager.MaxVolume);
            Rebuild();
        }

        public float Volume => _volume == null ? 1f : _volume.Value;

        private void Bind()
        {
            _enabled = _file.Bind("General", "Enabled", true,
                "Master switch. When false no Twitch connection is made and no Bing Bongs are spawned.");

            _useOwnChat = _file.Bind("Twitch", "UseMyOwnChat", false,
                "Read your own Twitch chat instead of listening to the host's, using your own channel, " +
                "filters, voices and API key. Only you hear it - your chat is never sent to the lobby - " +
                "so it needs no agreement from the host. Ignored when you are the host, since the host " +
                "already reads their own.");
            _channel = _file.Bind("Twitch", "Channel", "",
                "Twitch channel to read, without the leading '#'.");
            _twitchUser = _file.Bind("Twitch", "Username", "",
                "Lowercase login of the account the OAuth token belongs to. Leave empty for anonymous read-only chat.");
            _twitchToken = _file.Bind("Twitch", "OAuthToken", "",
                "OAuth token with the chat:read scope, with or without the 'oauth:' prefix. " +
                "Leave empty to connect anonymously. This file is not tracked by git; never share it.");
            _allowInsecureTls = _file.Bind("Twitch", "AllowInsecureTls", true,
                "Unity's Mono runtime often has no root certificate store, which makes strict TLS validation " +
                "fail for harmless reasons. When true the client connects anyway and logs a warning. " +
                "Set false to refuse unverified certificates instead.");

            _minLength = _file.Bind("Filter", "MinMessageLength", 2, "Messages shorter than this are ignored.");
            _maxLength = _file.Bind("Filter", "MaxMessageLength", 200, "Messages longer than this are ignored.");
            _commandPrefixes = _file.Bind("Filter", "CommandPrefixes", "!,/,.,?",
                "Comma-separated prefixes treated as bot commands and never spoken.");
            _ignoredUsers = _file.Bind("Filter", "IgnoredUsers",
                "nightbot,streamelements,streamlabs,moobot,fossabot,sery_bot",
                "Comma-separated logins that are never spoken.");
            _blockedWords = _file.Bind("Filter", "BlockedWords", "",
                "Comma-separated substrings; a message containing any of them is dropped.");
            _profanity = _file.Bind("Filter", "Profanity", ProfanityAction.Off,
                "Off = no profanity handling. Mask = replace each swear with ProfanityMask and speak the rest. " +
                "Block = drop the whole message.");
            _profanityWords = _file.Bind("Filter", "ProfanityWords", "",
                "Comma-separated extra word stems, added to the built-in list. A stem also matches its " +
                "common suffixes ('fuck' catches 'fucking') but only as a whole word, so 'grass' stays clean.");
            _profanityMask = _file.Bind("Filter", "ProfanityMask", "beep",
                "Spoken in place of each swear when Profanity = Mask. Empty removes the word instead.");
            _blockLinks = _file.Bind("Filter", "BlockLinks", true, "Drop messages containing URLs or bare domains.");
            _duplicateHistory = _file.Bind("Filter", "DuplicateHistorySize", 25,
                "How many recent messages are remembered for duplicate suppression. 0 disables it.");
            _subsOnly = _file.Bind("Filter", "SubscribersOnly", false, "Only subscribers may be spoken.");
            _modsOnly = _file.Bind("Filter", "ModeratorsOnly", false, "Only moderators and the broadcaster may be spoken.");
            _modBypass = _file.Bind("Filter", "ModeratorsBypassLimits", true,
                "Moderators and the broadcaster skip length limits, cooldowns and permission gates.");

            _maxQueued = _file.Bind("Queue", "MaxQueuedMessages", 10, "Queue capacity; further messages are dropped.");
            _userCooldown = _file.Bind("Queue", "PerUserCooldownSeconds", 20f, "Minimum gap between one chatter's messages.");
            _globalCooldown = _file.Bind("Queue", "GlobalCooldownSeconds", 0f, "Minimum gap between any two accepted messages.");
            _messageGap = _file.Bind("Queue", "GapBetweenMessagesSeconds", 0.5f, "Silence inserted between spoken messages.");

            _provider = _file.Bind("TTS", "DefaultProvider", "elevenlabs", new ConfigDescription(
                "Provider used when a voice alias does not name one.",
                new AcceptableValueList<string>("elevenlabs", "tiktok")));
            _defaultVoice = _file.Bind("TTS", "DefaultVoiceAlias", "default",
                "Alias from VoiceAliases used for chatters who have not picked one.");
            _voiceAliases = _file.Bind("TTS", "VoiceAliases",
                "default=elevenlabs:21m00Tcm4TlvDq8ikWAM",
                "Comma-separated 'alias=provider:voiceId'. Chat only ever sees the alias, so provider " +
                "voice ids stay out of the stream. Only aliases listed here can be selected.");
            _allowVoiceCommand = _file.Bind("TTS", "AllowVoiceCommand", false,
                "Let chatters run '!voice <alias>' to pick one of the allowlisted aliases.");
            _elevenKey = _file.Bind("TTS", "ElevenLabsApiKey", "",
                "ElevenLabs API key. Also readable from the CIPHERPEAK_ELEVENLABS_API_KEY environment " +
                "variable, which keeps it out of this file entirely.");
            _elevenModel = _file.Bind("TTS", "ElevenLabsModelId", "eleven_multilingual_v2", "ElevenLabs model id.");
            _elevenFormat = _file.Bind("TTS", "ElevenLabsOutputFormat", "mp3_44100_64",
                "ElevenLabs output_format. Must be an mp3_* value; Unity decodes MP3.");
            _elevenStability = _file.Bind("TTS", "ElevenLabsStability", 0.5f, "ElevenLabs voice_settings.stability.");
            _elevenSimilarity = _file.Bind("TTS", "ElevenLabsSimilarityBoost", 0.75f, "ElevenLabs voice_settings.similarity_boost.");
            _tiktokEndpoint = _file.Bind("TTS", "TikTokEndpoint", "",
                "TikTok publishes no public, permitted TTS API. The provider stays disabled unless you " +
                "point this at an endpoint you are allowed to use. See README, 'TikTok limitation'.");
            _tiktokSession = _file.Bind("TTS", "TikTokSessionId", "",
                "Session cookie for the TikTok endpoint above. Also readable from CIPHERPEAK_TIKTOK_SESSION_ID.");
            _timeout = _file.Bind("TTS", "RequestTimeoutSeconds", 20, "Per-request timeout.");
            _retries = _file.Bind("TTS", "MaxRetries", 2, "Retries per provider before giving up or falling back.");
            _fallbackProvider = _file.Bind("TTS", "FallbackToOtherProvider", true,
                "If the selected provider fails, try the other configured one.");
            _cacheEnabled = _file.Bind("TTS", "CacheEnabled", true, "Cache generated clips on disk.");
            _cacheMb = _file.Bind("TTS", "CacheMaxMegabytes", 64, "Cache size budget; oldest clips are trimmed first.");
            _maxAudioBytes = _file.Bind("TTS", "MaxAudioBytes", 512 * 1024,
                "Reject clips larger than this before they are sent over the network.");

            _prefabOverride = _file.Bind("BingBong", "PrefabNameOverride", "",
                "Resources name of the Bing Bong item prefab. Empty auto-detects it from the item database.");
            _spawnRadius = _file.Bind("BingBong", "SpawnRadiusMeters", 3f, "Horizontal spread around the anchor point.");
            _spawnHeight = _file.Bind("BingBong", "SpawnHeightOffsetMeters", 1.5f, "Height above the anchor to probe from.");
            _tickSeconds = _file.Bind("BingBong", "LifecycleTickSeconds", 3f,
                "How often the 'exactly two Bing Bongs' check runs.");
            _maxDistance = _file.Bind("BingBong", "MaxDistanceFromPlayersMeters", 0f,
                "Past this distance from the nearest living scout a Bing Bong counts as lost and is replaced " +
                "next to the party. 0 disables it, so Bing Bongs stay exactly where they were left.");
            _outOfBoundsDrop = _file.Bind("BingBong", "OutOfBoundsDropMeters", 0f,
                "This far below the nearest scout, a Bing Bong counts as out of bounds and is replaced. " +
                "0 disables it. Climbing triggers this as readily as falling does, hence the default.");
            _weightless = _file.Bind("BingBong", "Weightless", false,
                "Carry a Bing Bong without it counting toward your carry weight, in any slot. " +
                "Applies to your own character only, so every player who wants it needs the mod. " +
                "The weight readout refreshes on the next pickup or drop, not the instant you change this.");

            _volume = _file.Bind("Audio", "Volume", 1f, new ConfigDescription(
                "TTS volume, on top of the game's SFX slider. 1 matches the game's own sounds; above " +
                "that amplifies, and past about 2.5 it starts to clip.",
                new AcceptableValueRange<float>(0f, TtsPlaybackManager.MaxVolume)));
            _minDistance = _file.Bind("Audio", "MinDistance", 10f,
                "Distance the voice stays at full volume before it starts falling off. Raising this is " +
                "the most effective way to make Bing Bongs louder at normal ranges.");
            _audioMaxDistance = _file.Bind("Audio", "MaxDistance", 60f, "Distance at which the voice is inaudible.");
            _animateMouth = _file.Bind("Audio", "AnimateMouth", true, "Animate the Bing Bong's mouth to the speech.");

            _dedicatedSlot = _file.Bind("BingBong", "DedicatedSlot", true,
                "Give the Bing Bong its own inventory slot instead of spending one of the normal three. " +
                "The slot sits outside the game's weight sum, so what is in it never weighs anything. " +
                "Every player who wants the slot needs the mod; to others the Bing Bong is simply not there.");
            _slotUiOffsetX = _file.Bind("BingBong", "SlotUiOffsetX", 0f,
                "Horizontal nudge for the slot's HUD widget, in canvas units. 0 auto-places it beside the " +
                "game's temporary slot.");

            _skipKey = _file.Bind("Hotkeys", "Skip", new KeyboardShortcut(KeyCode.F7), "Skip the message being spoken.");
            _clearKey = _file.Bind("Hotkeys", "Clear", new KeyboardShortcut(KeyCode.F8), "Clear the pending queue.");
            _toggleKey = _file.Bind("Hotkeys", "Toggle", new KeyboardShortcut(KeyCode.F9), "Toggle TTS on or off.");
            _bingBongSlotKey = _file.Bind("Hotkeys", "BingBongSlot", new KeyboardShortcut(KeyCode.Alpha5),
                "Equip or stow the Bing Bong slot. The game's scroll cycle only walks slots 0-3, so the " +
                "dedicated slot needs its own key.");
            _backpackKey = _file.Bind("Hotkeys", "OpenBackpack", new KeyboardShortcut(KeyCode.B),
                "Open the wheel for the backpack on your own back, without dropping it first. " +
                "Set to None to leave the key alone for another mod.");
        }

        private void Rebuild()
        {
            var settings = new ModSettings
            {
                Enabled = _enabled.Value,
                Twitch =
                {
                    Channel = (_channel.Value ?? "").Trim().TrimStart('#'),
                    Username = (_twitchUser.Value ?? "").Trim(),
                    OAuthToken = ResolveSecret(_twitchToken.Value, "CIPHERPEAK_TWITCH_OAUTH"),
                    AllowInsecureTls = _allowInsecureTls.Value,
                    UseMyOwnChat = _useOwnChat.Value
                },
                Filter =
                {
                    MinMessageLength = _minLength.Value,
                    MaxMessageLength = _maxLength.Value,
                    CommandPrefixes = SplitCsv(_commandPrefixes.Value),
                    IgnoredUsers = SplitCsv(_ignoredUsers.Value),
                    BlockedWords = SplitCsv(_blockedWords.Value),
                    Profanity = _profanity.Value,
                    ProfanityWords = SplitCsv(_profanityWords.Value),
                    ProfanityMask = (_profanityMask.Value ?? "").Trim(),
                    BlockLinks = _blockLinks.Value,
                    DuplicateHistorySize = _duplicateHistory.Value,
                    SubscribersOnly = _subsOnly.Value,
                    ModeratorsOnly = _modsOnly.Value,
                    ModeratorsBypassLimits = _modBypass.Value
                },
                Queue =
                {
                    MaxQueuedMessages = _maxQueued.Value,
                    PerUserCooldownSeconds = _userCooldown.Value,
                    GlobalCooldownSeconds = _globalCooldown.Value,
                    GapBetweenMessagesSeconds = _messageGap.Value
                },
                Tts =
                {
                    DefaultProvider = (_provider.Value ?? "elevenlabs").ToLowerInvariant(),
                    DefaultVoiceAlias = (_defaultVoice.Value ?? "").Trim().ToLowerInvariant(),
                    AllowVoiceCommand = _allowVoiceCommand.Value,
                    VoiceAliases = VoiceAlias.ParseCsv(_voiceAliases.Value),
                    ElevenLabsApiKey = ResolveSecret(_elevenKey.Value, "CIPHERPEAK_ELEVENLABS_API_KEY"),
                    ElevenLabsModelId = _elevenModel.Value,
                    ElevenLabsOutputFormat = _elevenFormat.Value,
                    ElevenLabsStability = _elevenStability.Value,
                    ElevenLabsSimilarityBoost = _elevenSimilarity.Value,
                    TikTokEndpoint = (_tiktokEndpoint.Value ?? "").Trim(),
                    TikTokSessionId = ResolveSecret(_tiktokSession.Value, "CIPHERPEAK_TIKTOK_SESSION_ID"),
                    RequestTimeoutSeconds = _timeout.Value,
                    MaxRetries = _retries.Value,
                    FallbackToOtherProvider = _fallbackProvider.Value,
                    CacheEnabled = _cacheEnabled.Value,
                    CacheMaxMegabytes = _cacheMb.Value,
                    MaxAudioBytes = _maxAudioBytes.Value
                },
                BingBong =
                {
                    PrefabNameOverride = (_prefabOverride.Value ?? "").Trim(),
                    SpawnRadiusMeters = _spawnRadius.Value,
                    SpawnHeightOffsetMeters = _spawnHeight.Value,
                    LifecycleTickSeconds = Mathf.Max(0.5f, _tickSeconds.Value),
                    MaxDistanceFromPlayersMeters = _maxDistance.Value,
                    OutOfBoundsDropMeters = _outOfBoundsDrop.Value,
                    Weightless = _weightless.Value,
                    DedicatedSlot = _dedicatedSlot.Value,
                    SlotUiOffsetX = _slotUiOffsetX.Value
                },
                Audio =
                {
                    Volume = _volume.Value,
                    MinDistance = _minDistance.Value,
                    MaxDistance = _audioMaxDistance.Value,
                    AnimateMouth = _animateMouth.Value
                }
            };

            // A default alias must always resolve, otherwise nothing can ever be spoken.
            if (settings.Tts.VoiceAliases.Count == 0)
                _log.Warn("No voice aliases configured; TTS will stay silent until VoiceAliases has an entry.");

            SecretScrubber.Clear();
            SecretScrubber.Register(settings.Tts.ElevenLabsApiKey);
            SecretScrubber.Register(settings.Tts.TikTokSessionId);
            SecretScrubber.Register(settings.Twitch.OAuthToken);

            _snapshot = settings;
        }

        /// <summary>Environment variable wins, so credentials need never be written to disk.</summary>
        private static string ResolveSecret(string configured, string environmentVariable)
        {
            string fromEnv = null;
            try { fromEnv = Environment.GetEnvironmentVariable(environmentVariable); }
            catch { /* restricted environment; fall back to the config value */ }
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv.Trim() : (configured ?? "").Trim();
        }

        private static List<string> SplitCsv(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return result;
            foreach (var part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }
    }
}
