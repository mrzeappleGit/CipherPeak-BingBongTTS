using System;

namespace CipherPeak.Core.Queueing
{
    public sealed class SpeechRequest
    {
        /// <summary>Monotonic id, unique per session. Used to correlate network audio chunks.</summary>
        public int Id;

        public string Login = "";
        public string DisplayName = "";
        public string Text = "";

        /// <summary>Resolved voice alias, never a raw provider voice id.</summary>
        public string VoiceAlias = "";

        public DateTimeOffset QueuedAt;

        public override string ToString() => "#" + Id + " " + Login + ": " + Text;
    }
}
