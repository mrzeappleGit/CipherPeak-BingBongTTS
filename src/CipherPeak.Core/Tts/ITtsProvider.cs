using System.Threading;
using System.Threading.Tasks;

namespace CipherPeak.Core.Tts
{
    public sealed class TtsResult
    {
        public byte[] Audio;

        /// <summary>"mp3" today; kept explicit so a future provider can return something else.</summary>
        public string Format = "mp3";

        public string Error;

        /// <summary>Server-suggested wait before the next call, honoured by the router.</summary>
        public double RetryAfterSeconds;

        public bool Success => Error == null && Audio != null && Audio.Length > 0;

        public static TtsResult Ok(byte[] audio, string format = "mp3") =>
            new TtsResult { Audio = audio, Format = format };

        public static TtsResult Fail(string error, double retryAfterSeconds = 0) =>
            new TtsResult { Error = error, RetryAfterSeconds = retryAfterSeconds };
    }

    public interface ITtsProvider
    {
        /// <summary>Lowercase provider key used in config and voice aliases.</summary>
        string Name { get; }

        /// <summary>False when the provider is not configured or has no permitted API available.</summary>
        bool IsAvailable(out string reason);

        Task<TtsResult> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken);
    }
}
