using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CipherPeak.Core.Config;
using CipherPeak.Core.Net;

namespace CipherPeak.Core.Tts
{
    /// <summary>
    /// POST https://api.elevenlabs.io/v1/text-to-speech/{voice_id}?output_format=...
    /// Header: xi-api-key. Body: {"text","model_id","voice_settings"}. Response: audio bytes.
    /// </summary>
    public sealed class ElevenLabsTtsProvider : ITtsProvider
    {
        public const string ProviderName = "elevenlabs";

        private const string BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech/";

        private readonly IHttpTransport _http;
        private readonly Func<ModSettings> _settings;

        public ElevenLabsTtsProvider(IHttpTransport http, Func<ModSettings> settings)
        {
            _http = http;
            _settings = settings;
        }

        public string Name => ProviderName;

        public bool IsAvailable(out string reason)
        {
            if (string.IsNullOrWhiteSpace(_settings().Tts.ElevenLabsApiKey))
            {
                reason = "no ElevenLabs API key configured";
                return false;
            }
            reason = null;
            return true;
        }

        public async Task<TtsResult> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken)
        {
            string reason;
            if (!IsAvailable(out reason)) return TtsResult.Fail(reason);
            if (string.IsNullOrWhiteSpace(voiceId)) return TtsResult.Fail("no ElevenLabs voice id for that alias");
            if (string.IsNullOrWhiteSpace(text)) return TtsResult.Fail("empty text");

            var tts = _settings().Tts;

            string url = BaseUrl + Uri.EscapeDataString(voiceId)
                         + "?output_format=" + Uri.EscapeDataString(NonEmpty(tts.ElevenLabsOutputFormat, "mp3_44100_64"));

            var headers = new Dictionary<string, string>
            {
                { "xi-api-key", tts.ElevenLabsApiKey },
                { "Accept", "audio/mpeg" }
            };

            byte[] body = Encoding.UTF8.GetBytes(BuildRequestJson(text, tts));

            var response = await _http
                .PostAsync(url, headers, "application/json", body, tts.RequestTimeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (response.TransportError != null)
                return TtsResult.Fail("ElevenLabs transport failure (" + response.TransportError + ")");

            if (response.IsRateLimited)
                return TtsResult.Fail("ElevenLabs rate limited (429)",
                    response.RetryAfterSeconds > 0 ? response.RetryAfterSeconds : 5);

            if (!response.IsSuccess)
                return TtsResult.Fail("ElevenLabs HTTP " + response.StatusCode + " " + Describe(response.StatusCode),
                    response.RetryAfterSeconds);

            if (response.Body == null || response.Body.Length == 0)
                return TtsResult.Fail("ElevenLabs returned an empty body");

            // A JSON error can arrive with a 200 in some proxy setups; audio never starts with '{'.
            if (response.Body[0] == (byte)'{' ||
                (response.ContentType ?? "").IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                return TtsResult.Fail("ElevenLabs returned JSON instead of audio");

            if (response.Body.Length > _settings().Tts.MaxAudioBytes)
                return TtsResult.Fail("ElevenLabs audio exceeded MaxAudioBytes ("
                                      + response.Body.Length + " bytes)");

            return TtsResult.Ok(response.Body);
        }

        private static string BuildRequestJson(string text, TtsSettings tts)
        {
            var sb = new StringBuilder(text.Length + 160);
            sb.Append("{\"text\":").Append(JsonString(text));
            sb.Append(",\"model_id\":").Append(JsonString(NonEmpty(tts.ElevenLabsModelId, "eleven_multilingual_v2")));
            sb.Append(",\"voice_settings\":{\"stability\":")
              .Append(tts.ElevenLabsStability.ToString("0.###", CultureInfo.InvariantCulture))
              .Append(",\"similarity_boost\":")
              .Append(tts.ElevenLabsSimilarityBoost.ToString("0.###", CultureInfo.InvariantCulture))
              .Append("}}");
            return sb.ToString();
        }

        private static string NonEmpty(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string Describe(int status)
        {
            switch (status)
            {
                case 401: return "(bad or missing API key)";
                case 403: return "(key lacks permission for this voice)";
                case 404: return "(voice id not found)";
                case 422: return "(request rejected, check model id and text)";
                default: return status >= 500 ? "(provider outage)" : "";
            }
        }

        /// <summary>Minimal JSON string escaping; Core deliberately has no JSON dependency.</summary>
        internal static string JsonString(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
