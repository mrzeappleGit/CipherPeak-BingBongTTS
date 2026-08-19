using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CipherPeak.Core.Net
{
    /// <summary>Default <see cref="IHttpTransport"/>. One shared HttpClient, per-request timeouts.</summary>
    public sealed class HttpClientTransport : IHttpTransport, IDisposable
    {
        private readonly HttpClient _client;

        public HttpClientTransport()
        {
            _client = new HttpClient
            {
                // Per-request timeouts are enforced with a linked CTS instead.
                Timeout = Timeout.InfiniteTimeSpan
            };
            _client.DefaultRequestHeaders.Add("User-Agent", "CipherPeak-BingBongTTS/1.0");
        }

        public async Task<HttpResponse> PostAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            string contentType,
            byte[] body,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var result = new HttpResponse();

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        request.Content = new ByteArrayContent(body ?? Array.Empty<byte>());
                        if (!string.IsNullOrEmpty(contentType))
                            request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);

                        if (headers != null)
                            foreach (var kv in headers)
                                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

                        using (var response = await _client
                                   .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                                   .ConfigureAwait(false))
                        {
                            result.StatusCode = (int)response.StatusCode;
                            result.Body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            result.ContentType = response.Content.Headers.ContentType?.MediaType ?? "";

                            var retryAfter = response.Headers.RetryAfter;
                            if (retryAfter != null)
                            {
                                if (retryAfter.Delta.HasValue)
                                    result.RetryAfterSeconds = retryAfter.Delta.Value.TotalSeconds;
                                else if (retryAfter.Date.HasValue)
                                    result.RetryAfterSeconds =
                                        Math.Max(0, (retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result.TransportError = "timeout after " + timeoutSeconds + "s";
                }
                catch (OperationCanceledException)
                {
                    result.TransportError = "cancelled";
                }
                catch (Exception ex)
                {
                    // Never let a provider outage escape into the game loop.
                    result.TransportError = ex.GetType().Name + ": " + ex.Message;
                }
            }

            return result;
        }

        public void Dispose() { _client.Dispose(); }
    }
}
