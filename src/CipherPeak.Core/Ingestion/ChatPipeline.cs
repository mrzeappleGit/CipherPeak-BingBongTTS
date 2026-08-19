using System;
using CipherPeak.Core.Commands;
using CipherPeak.Core.Config;
using CipherPeak.Core.Filtering;
using CipherPeak.Core.Logging;
using CipherPeak.Core.Queueing;
using CipherPeak.Core.Tts;
using CipherPeak.Core.Twitch;

namespace CipherPeak.Core.Ingestion
{
    public enum IngestOutcome
    {
        Queued,
        Command,
        Filtered,
        Rejected,
        Disabled
    }

    /// <summary>
    /// One place where a raw chat message becomes (or does not become) a queued utterance:
    /// commands first, then filtering, then admission with cooldowns.
    /// </summary>
    public sealed class ChatPipeline
    {
        private readonly Func<ModSettings> _settings;
        private readonly ChatCommandProcessor _commands;
        private readonly MessageFilter _filter;
        private readonly SpeechQueue _queue;
        private readonly VoiceRegistry _voices;
        private readonly ILog _log;

        public ChatPipeline(
            Func<ModSettings> settings,
            ChatCommandProcessor commands,
            MessageFilter filter,
            SpeechQueue queue,
            VoiceRegistry voices,
            ILog log = null)
        {
            _settings = settings;
            _commands = commands;
            _filter = filter;
            _queue = queue;
            _voices = voices;
            _log = log ?? NullLog.Instance;
        }

        public IngestOutcome Handle(ChatMessage message, DateTimeOffset now, out SpeechRequest request)
        {
            request = null;
            if (message == null) return IngestOutcome.Rejected;
            if (!_settings().Enabled) return IngestOutcome.Disabled;

            var commandOutcome = _commands.Process(message);
            if (commandOutcome != CommandOutcome.NotACommand) return IngestOutcome.Command;

            var filtered = _filter.Evaluate(message);
            if (!filtered.Accepted)
            {
                _log.Info("Dropped message from " + message.Login + ": " + filtered.Verdict);
                return IngestOutcome.Filtered;
            }

            string alias = _voices.AliasFor(message.Login);
            var verdict = _queue.TryEnqueue(
                message.Login, message.DisplayName, filtered.Text, alias, now, out request);

            if (verdict == EnqueueVerdict.Queued)
            {
                _log.Info("Queued a message from " + message.Login + ".");
                return IngestOutcome.Queued;
            }

            _log.Info("Not queued (" + verdict + ") for " + message.Login + ".");
            return verdict == EnqueueVerdict.Disabled ? IngestOutcome.Disabled : IngestOutcome.Rejected;
        }
    }
}
