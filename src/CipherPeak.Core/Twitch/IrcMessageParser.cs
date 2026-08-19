using System;
using System.Collections.Generic;

namespace CipherPeak.Core.Twitch
{
    /// <summary>
    /// Parses a single IRCv3 line from Twitch. Pure and allocation-light so it can be tested
    /// without a socket. Only PRIVMSG is turned into a <see cref="ChatMessage"/>.
    /// </summary>
    public static class IrcMessageParser
    {
        public static bool IsPing(string line) =>
            line != null && line.StartsWith("PING", StringComparison.Ordinal);

        public static string PongFor(string pingLine)
        {
            int colon = pingLine.IndexOf(':');
            string payload = colon >= 0 ? pingLine.Substring(colon + 1) : "tmi.twitch.tv";
            return "PONG :" + payload;
        }

        /// <summary>Twitch sends NOTICE on auth failure; those two texts mean the token is unusable.</summary>
        public static bool IsAuthFailure(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            return line.IndexOf("Login authentication failed", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Improperly formatted auth", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryParsePrivmsg(string line, DateTimeOffset receivedAt, out ChatMessage message)
        {
            message = null;
            if (string.IsNullOrEmpty(line)) return false;

            string rest = line;
            Dictionary<string, string> tags = null;

            if (rest[0] == '@')
            {
                int space = rest.IndexOf(' ');
                if (space < 0) return false;
                tags = ParseTags(rest.Substring(1, space - 1));
                rest = rest.Substring(space + 1);
            }

            if (rest.Length == 0 || rest[0] != ':') return false;

            int prefixEnd = rest.IndexOf(' ');
            if (prefixEnd < 0) return false;
            string prefix = rest.Substring(1, prefixEnd - 1);
            rest = rest.Substring(prefixEnd + 1);

            if (!rest.StartsWith("PRIVMSG ", StringComparison.Ordinal)) return false;

            int textStart = rest.IndexOf(" :", StringComparison.Ordinal);
            if (textStart < 0) return false;
            string text = rest.Substring(textStart + 2);

            int bang = prefix.IndexOf('!');
            string login = (bang > 0 ? prefix.Substring(0, bang) : prefix).ToLowerInvariant();

            message = new ChatMessage
            {
                Login = login,
                DisplayName = login,
                Text = StripActionWrapper(text).Trim(),
                ReceivedAt = receivedAt
            };

            if (tags != null) ApplyTags(message, tags);
            if (string.IsNullOrEmpty(message.DisplayName)) message.DisplayName = login;
            return true;
        }

        /// <summary>"/me hello" arrives wrapped as CTCP: ACTION hello.</summary>
        private static string StripActionWrapper(string text)
        {
            const string open = "\u0001ACTION ";
            const char close = '\u0001';
            if (text.Length > open.Length
                && text.StartsWith(open, StringComparison.Ordinal)
                && text[text.Length - 1] == close)
                return text.Substring(open.Length, text.Length - open.Length - 1);
            return text;
        }

        private static void ApplyTags(ChatMessage message, Dictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("display-name", out value) && !string.IsNullOrWhiteSpace(value))
                message.DisplayName = value;

            if (tags.TryGetValue("subscriber", out value)) message.IsSubscriber = value == "1";
            if (tags.TryGetValue("mod", out value)) message.IsModerator = value == "1";
            if (tags.TryGetValue("vip", out value)) message.IsVip = value == "1";

            if (tags.TryGetValue("badges", out value) && !string.IsNullOrEmpty(value))
            {
                if (value.IndexOf("broadcaster/", StringComparison.Ordinal) >= 0) message.IsBroadcaster = true;
                if (value.IndexOf("moderator/", StringComparison.Ordinal) >= 0) message.IsModerator = true;
                if (value.IndexOf("vip/", StringComparison.Ordinal) >= 0) message.IsVip = true;
                if (value.IndexOf("subscriber/", StringComparison.Ordinal) >= 0
                    || value.IndexOf("founder/", StringComparison.Ordinal) >= 0) message.IsSubscriber = true;
            }

            if (message.IsBroadcaster) message.IsModerator = true;
        }

        private static Dictionary<string, string> ParseTags(string raw)
        {
            var tags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in raw.Split(';'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq < 0) { tags[pair] = ""; continue; }
                tags[pair.Substring(0, eq)] = Unescape(pair.Substring(eq + 1));
            }
            return tags;
        }

        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0) return value;
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i == value.Length - 1) { sb.Append(value[i]); continue; }
                i++;
                switch (value[i])
                {
                    case ':': sb.Append(';'); break;
                    case 's': sb.Append(' '); break;
                    case 'r': sb.Append('\r'); break;
                    case 'n': sb.Append('\n'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append(value[i]); break;
                }
            }
            return sb.ToString();
        }
    }
}
