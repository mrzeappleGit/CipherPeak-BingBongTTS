using System;
using System.Collections.Generic;
using CipherPeak.Core.Config;

namespace CipherPeak.Core.Queueing
{
    public enum EnqueueVerdict
    {
        Queued,
        QueueFull,
        UserCooldown,
        GlobalCooldown,
        Disabled
    }

    /// <summary>
    /// FIFO admission queue with per-user and global cooldowns. Time is always passed in so the
    /// behaviour is deterministic in tests.
    /// </summary>
    public sealed class SpeechQueue
    {
        private readonly Func<ModSettings> _settings;
        private readonly Queue<SpeechRequest> _items = new Queue<SpeechRequest>();
        private readonly Dictionary<string, DateTimeOffset> _lastAcceptedPerUser =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        private DateTimeOffset _lastAcceptedAny = DateTimeOffset.MinValue;
        private int _nextId = 1;

        public SpeechQueue(Func<ModSettings> settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public SpeechQueue(ModSettings settings) : this(() => settings) { }

        public int Count => _items.Count;

        public EnqueueVerdict TryEnqueue(
            string login, string displayName, string text, string voiceAlias,
            DateTimeOffset now, out SpeechRequest request)
        {
            request = null;
            var settings = _settings();
            if (!settings.Enabled) return EnqueueVerdict.Disabled;

            var q = settings.Queue;

            if (q.MaxQueuedMessages > 0 && _items.Count >= q.MaxQueuedMessages)
                return EnqueueVerdict.QueueFull;

            if (q.GlobalCooldownSeconds > 0
                && _lastAcceptedAny != DateTimeOffset.MinValue
                && (now - _lastAcceptedAny).TotalSeconds < q.GlobalCooldownSeconds)
                return EnqueueVerdict.GlobalCooldown;

            if (q.PerUserCooldownSeconds > 0 && !string.IsNullOrEmpty(login))
            {
                DateTimeOffset last;
                if (_lastAcceptedPerUser.TryGetValue(login, out last)
                    && (now - last).TotalSeconds < q.PerUserCooldownSeconds)
                    return EnqueueVerdict.UserCooldown;
            }

            request = new SpeechRequest
            {
                Id = _nextId++,
                Login = login ?? "",
                DisplayName = string.IsNullOrEmpty(displayName) ? (login ?? "") : displayName,
                Text = text ?? "",
                VoiceAlias = voiceAlias ?? "",
                QueuedAt = now
            };

            _items.Enqueue(request);
            _lastAcceptedAny = now;
            if (!string.IsNullOrEmpty(login)) _lastAcceptedPerUser[login] = now;
            return EnqueueVerdict.Queued;
        }

        public bool TryDequeue(out SpeechRequest request)
        {
            if (_items.Count == 0) { request = null; return false; }
            request = _items.Dequeue();
            return true;
        }

        /// <summary>Drops everything still waiting. Does not touch cooldowns.</summary>
        public int Clear()
        {
            int dropped = _items.Count;
            _items.Clear();
            return dropped;
        }

        /// <summary>Full reset, used when a run ends or the mod is disabled.</summary>
        public void Reset()
        {
            _items.Clear();
            _lastAcceptedPerUser.Clear();
            _lastAcceptedAny = DateTimeOffset.MinValue;
        }
    }
}
