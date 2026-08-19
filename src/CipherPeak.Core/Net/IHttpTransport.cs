using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CipherPeak.Core.Net
{
    public sealed class HttpResponse
    {
        public int StatusCode;
        public byte[] Body;
        public string ContentType = "";

        /// <summary>Seconds from a Retry-After header, 0 when absent.</summary>
        public double RetryAfterSeconds;

        /// <summary>Set when the request never completed (timeout, DNS, TLS, socket).</summary>
        public string TransportError;

        public bool IsSuccess => TransportError == null && StatusCode >= 200 && StatusCode < 300;
        public bool IsRateLimited => StatusCode == 429;

        /// <summary>5xx and transport failures are worth retrying; 4xx (except 429) are not.</summary>
        public bool IsRetryable => TransportError != null || StatusCode >= 500 || StatusCode == 429;
    }

    /// <summary>
    /// The only outbound-HTTP seam in Core. Providers depend on this, never on HttpClient directly,
    /// so rate limits, timeouts, outages and malformed bodies are all reproducible in tests.
    /// </summary>
    public interface IHttpTransport
    {
        Task<HttpResponse> PostAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            string contentType,
            byte[] body,
            int timeoutSeconds,
            CancellationToken cancellationToken);
    }
}
