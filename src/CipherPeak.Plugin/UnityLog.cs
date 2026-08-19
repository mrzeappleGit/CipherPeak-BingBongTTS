using BepInEx.Logging;
using CipherPeak.Core.Logging;

namespace CipherPeak.Plugin
{
    internal sealed class UnityLog : ILog
    {
        private readonly ManualLogSource _source;

        public UnityLog(ManualLogSource source) { _source = source; }

        public void Info(string message) { _source.LogInfo(SecretScrubber.Scrub(message)); }
        public void Warn(string message) { _source.LogWarning(SecretScrubber.Scrub(message)); }
        public void Error(string message) { _source.LogError(SecretScrubber.Scrub(message)); }
    }
}
