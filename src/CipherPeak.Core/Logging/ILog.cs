using System;

namespace CipherPeak.Core.Logging
{
    /// <summary>Minimal logging seam so Core never references BepInEx or UnityEngine.</summary>
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    public sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new NullLog();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
    }

    public sealed class DelegateLog : ILog
    {
        private readonly Action<string> _info, _warn, _error;

        public DelegateLog(Action<string> info, Action<string> warn, Action<string> error)
        {
            _info = info;
            _warn = warn;
            _error = error;
        }

        public void Info(string message) { _info?.Invoke(message); }
        public void Warn(string message) { _warn?.Invoke(message); }
        public void Error(string message) { _error?.Invoke(message); }
    }

    /// <summary>
    /// Redacts known secrets from any string before it reaches a log sink.
    /// Registered values are never printed, only their length.
    /// </summary>
    public static class SecretScrubber
    {
        private static readonly System.Collections.Generic.List<string> Secrets =
            new System.Collections.Generic.List<string>();

        public static void Register(string secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 6) return;
            lock (Secrets)
            {
                if (!Secrets.Contains(secret)) Secrets.Add(secret);
            }
        }

        public static void Clear()
        {
            lock (Secrets) Secrets.Clear();
        }

        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            lock (Secrets)
            {
                for (int i = 0; i < Secrets.Count; i++)
                    text = text.Replace(Secrets[i], "<redacted>");
            }
            return text;
        }
    }

    public sealed class ScrubbingLog : ILog
    {
        private readonly ILog _inner;
        public ScrubbingLog(ILog inner) { _inner = inner ?? NullLog.Instance; }
        public void Info(string message) { _inner.Info(SecretScrubber.Scrub(message)); }
        public void Warn(string message) { _inner.Warn(SecretScrubber.Scrub(message)); }
        public void Error(string message) { _inner.Error(SecretScrubber.Scrub(message)); }
    }
}
