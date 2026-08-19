using System;
using CipherPeak.Core.Config;
using CipherPeak.Core.Logging;
using CipherPeak.Core.Tts;
using CipherPeak.Core.Twitch;

namespace CipherPeak.Core.Commands
{
    public enum CommandOutcome
    {
        NotACommand,
        Handled,
        Denied,
        UnknownVoice
    }

    /// <summary>
    /// Handles the small set of chat commands the mod understands. The IRC connection is
    /// read-only, so nothing is ever written back to chat: results go to the BepInEx log only.
    /// </summary>
    public sealed class ChatCommandProcessor
    {
        private readonly Func<ModSettings> _settings;
        private readonly VoiceRegistry _voices;
        private readonly ILog _log;

        public ChatCommandProcessor(Func<ModSettings> settings, VoiceRegistry voices, ILog log = null)
        {
            _settings = settings;
            _voices = voices;
            _log = log ?? NullLog.Instance;
        }

        /// <summary>Raised for "!tts skip".</summary>
        public event Action SkipRequested;

        /// <summary>Raised for "!tts clear".</summary>
        public event Action ClearRequested;

        /// <summary>Raised for "!tts on" / "!tts off" with the new enabled state.</summary>
        public event Action<bool> EnabledChangeRequested;

        public CommandOutcome Process(ChatMessage message)
        {
            if (message == null) return CommandOutcome.NotACommand;

            string text = (message.Text ?? "").Trim();
            if (text.Length < 2 || text[0] != '!') return CommandOutcome.NotACommand;

            string[] parts = text.Substring(1).Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return CommandOutcome.NotACommand;

            string verb = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1] : "";

            switch (verb)
            {
                case "voice":
                    return HandleVoice(message, arg);

                case "tts":
                    return HandleTts(message, arg.ToLowerInvariant());

                default:
                    return CommandOutcome.NotACommand;
            }
        }

        private CommandOutcome HandleVoice(ChatMessage message, string alias)
        {
            if (!_settings().Tts.AllowVoiceCommand) return CommandOutcome.Denied;
            if (alias.Length == 0) return CommandOutcome.UnknownVoice;

            if (!_voices.TrySetUserVoice(message.Login, alias))
            {
                _log.Info("'" + message.Login + "' asked for unknown voice alias '" + alias +
                          "'. Allowed: " + string.Join(", ", _voices.AllowedAliases()));
                return CommandOutcome.UnknownVoice;
            }

            _log.Info("'" + message.Login + "' switched to voice alias '" + alias + "'.");
            return CommandOutcome.Handled;
        }

        private CommandOutcome HandleTts(ChatMessage message, string action)
        {
            if (!message.HasElevatedRole) return CommandOutcome.Denied;

            switch (action)
            {
                case "skip":
                    SkipRequested?.Invoke();
                    _log.Info("TTS skip requested by " + message.Login + ".");
                    return CommandOutcome.Handled;

                case "clear":
                    ClearRequested?.Invoke();
                    _log.Info("TTS queue cleared by " + message.Login + ".");
                    return CommandOutcome.Handled;

                case "on":
                    EnabledChangeRequested?.Invoke(true);
                    _log.Info("TTS enabled by " + message.Login + ".");
                    return CommandOutcome.Handled;

                case "off":
                    EnabledChangeRequested?.Invoke(false);
                    _log.Info("TTS disabled by " + message.Login + ".");
                    return CommandOutcome.Handled;

                default:
                    return CommandOutcome.NotACommand;
            }
        }
    }
}
