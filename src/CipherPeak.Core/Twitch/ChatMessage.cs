using System;

namespace CipherPeak.Core.Twitch
{
    public sealed class ChatMessage
    {
        /// <summary>Lowercase login name.</summary>
        public string Login = "";

        /// <summary>Display name if the tags carried one, otherwise the login.</summary>
        public string DisplayName = "";

        public string Text = "";
        public bool IsSubscriber;
        public bool IsModerator;
        public bool IsBroadcaster;
        public bool IsVip;
        public DateTimeOffset ReceivedAt;

        public bool HasElevatedRole => IsModerator || IsBroadcaster;

        public override string ToString() => Login + ": " + Text;
    }
}
