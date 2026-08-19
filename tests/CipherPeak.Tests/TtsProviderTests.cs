using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CipherPeak.Core.Config;
using CipherPeak.Core.Net;
using CipherPeak.Core.Tts;
using Xunit;

namespace CipherPeak.Tests
{
    public class ElevenLabsProviderTests
    {
        private static readonly byte[] Mp3 = { 0xFF, 0xFB, 0x90, 0x44, 0x00 };

        [Fact]
        public async Task ReturnsAudioAndSendsTheDocumentedRequestShape()
        {
            var settings = Build.Settings();
            var http = new FakeHttpTransport().EnqueueAudio(Mp3);
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "voice-a", CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(Mp3, result.Audio);
            Assert.StartsWith("https://api.elevenlabs.io/v1/text-to-speech/voice-a?output_format=", http.LastUrl);
            Assert.Equal("test-key", http.LastHeaders["xi-api-key"]);
            Assert.Contains("\"text\":\"hello\"", http.LastBody);
            Assert.Contains("\"model_id\":\"eleven_multilingual_v2\"", http.LastBody);
        }

        [Fact]
        public async Task EscapesJsonSoQuotesCannotBreakTheBody()
        {
            var settings = Build.Settings();
            var http = new FakeHttpTransport().EnqueueAudio(Mp3);
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            await provider.SynthesizeAsync("he said \"hi\"\nbye", "voice-a", CancellationToken.None);

            Assert.Contains("\"text\":\"he said \\\"hi\\\"\\nbye\"", http.LastBody);
        }

        [Fact]
        public async Task ReportsMissingApiKeyWithoutCallingOut()
        {
            var settings = Build.Settings();
            settings.Tts.ElevenLabsApiKey = "";

            var http = new FakeHttpTransport();
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "voice-a", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(0, http.Calls);
        }

        [Fact]
        public async Task SurfacesRateLimitsWithARetryDelay()
        {
            var settings = Build.Settings();
            var http = new FakeHttpTransport().EnqueueStatus(429, retryAfter: 7);
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "voice-a", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("rate limited", result.Error);
            Assert.Equal(7, result.RetryAfterSeconds);
        }

        [Theory]
        [InlineData(401)]
        [InlineData(404)]
        [InlineData(500)]
        public async Task SurfacesHttpErrors(int status)
        {
            var settings = Build.Settings();
            var http = new FakeHttpTransport().EnqueueStatus(status);
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "voice-a", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(status.ToString(), result.Error);
        }

        [Fact]
        public async Task SurfacesTimeouts()
        {
            var settings = Build.Settings();
            var http = new FakeHttpTransport().EnqueueTransportError("timeout after 20s");
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "voice-a", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("timeout", result.Error);
        }

        [Fact]
        public async Task RejectsJsonMasqueradingAsAudio()
        {
            var settings = Build.Settings();
            var http = new FakeHttpTransport().Enqueue(new HttpResponse
            {
                StatusCode = 200,
                Body = Encoding.UTF8.GetBytes("{\"detail\":\"nope\"}"),
                ContentType = "application/json"
            });
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "voice-a", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("JSON", result.Error);
        }

        [Fact]
        public async Task RejectsOversizedAudio()
        {
            var settings = Build.Settings();
            settings.Tts.MaxAudioBytes = 4;

            var http = new FakeHttpTransport().EnqueueAudio(new byte[] { 0xFF, 0xFB, 1, 2, 3, 4, 5 });
            var provider = new ElevenLabsTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "voice-a", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("MaxAudioBytes", result.Error);
        }
    }

    public class TikTokProviderTests
    {
        [Fact]
        public void IsUnavailableUntilAnEndpointIsSuppliedByTheOperator()
        {
            var settings = Build.Settings();
            var provider = new TikTokTtsProvider(new FakeHttpTransport(), () => settings);

            Assert.False(provider.IsAvailable(out var reason));
            Assert.Contains("no public, permitted TTS API", reason);
        }

        [Fact]
        public void StillNeedsASessionIdOnceAnEndpointIsSet()
        {
            var settings = Build.Settings();
            settings.Tts.TikTokEndpoint = "https://example.invalid/tts";

            var provider = new TikTokTtsProvider(new FakeHttpTransport(), () => settings);

            Assert.False(provider.IsAvailable(out var reason));
            Assert.Contains("TikTokSessionId", reason);
        }

        [Fact]
        public async Task FailsGracefullyWithoutTouchingTheNetworkWhenUnconfigured()
        {
            var settings = Build.Settings();
            var http = new FakeHttpTransport();
            var provider = new TikTokTtsProvider(http, () => settings);

            var result = await provider.SynthesizeAsync("hello", "en_us_001", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(0, http.Calls);
        }

        [Fact]
        public async Task DecodesBase64AudioFromAConfiguredEndpoint()
        {
            var settings = Build.Settings();
            settings.Tts.TikTokEndpoint = "https://example.invalid/tts";
            settings.Tts.TikTokSessionId = "session";

            byte[] audio = { 1, 2, 3, 4 };
            var body = "{\"data\":{\"v_str\":\"" + Convert.ToBase64String(audio) + "\"}}";
            var http = new FakeHttpTransport().Enqueue(new HttpResponse
            {
                StatusCode = 200,
                Body = Encoding.UTF8.GetBytes(body),
                ContentType = "application/json"
            });

            var provider = new TikTokTtsProvider(http, () => settings);
            var result = await provider.SynthesizeAsync("hello", "en_us_001", CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(audio, result.Audio);
        }

        [Fact]
        public async Task ReportsAMalformedResponseInsteadOfThrowing()
        {
            var settings = Build.Settings();
            settings.Tts.TikTokEndpoint = "https://example.invalid/tts";
            settings.Tts.TikTokSessionId = "session";

            var http = new FakeHttpTransport().Enqueue(new HttpResponse
            {
                StatusCode = 200,
                Body = Encoding.UTF8.GetBytes("<html>blocked</html>"),
                ContentType = "text/html"
            });

            var provider = new TikTokTtsProvider(http, () => settings);
            var result = await provider.SynthesizeAsync("hello", "en_us_001", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("malformed", result.Error);
        }
    }

    public class TtsRouterTests
    {
        private static readonly byte[] Mp3 = { 0xFF, 0xFB, 0x90 };

        private static TtsRouter Router(ModSettings settings, IHttpTransport http, params ITtsProvider[] extra)
        {
            var providers = new List<ITtsProvider>
            {
                new ElevenLabsTtsProvider(http, () => settings),
                new TikTokTtsProvider(http, () => settings)
            };
            providers.AddRange(extra);
            return new TtsRouter(() => settings, new VoiceRegistry(() => settings), providers);
        }

        [Fact]
        public async Task ReturnsAudioForAKnownAlias()
        {
            var settings = Build.Settings();
            settings.Tts.MaxRetries = 0;

            var result = await Router(settings, new FakeHttpTransport().EnqueueAudio(Mp3))
                .SynthesizeAsync("hello", "default", CancellationToken.None);

            Assert.True(result.Success);
        }

        [Fact]
        public async Task FallsBackToTheDefaultAliasWhenTheRequestedOneIsUnknown()
        {
            var settings = Build.Settings();
            settings.Tts.MaxRetries = 0;

            var result = await Router(settings, new FakeHttpTransport().EnqueueAudio(Mp3))
                .SynthesizeAsync("hello", "not-an-alias", CancellationToken.None);

            Assert.True(result.Success);
        }

        [Fact]
        public async Task RetriesTransientFailuresThenSucceeds()
        {
            var settings = Build.Settings();
            settings.Tts.MaxRetries = 2;
            settings.Tts.FallbackToOtherProvider = false;

            var http = new FakeHttpTransport()
                .EnqueueStatus(500)
                .EnqueueAudio(Mp3);

            var result = await Router(settings, http).SynthesizeAsync("hello", "default", CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(2, http.Calls);
        }

        [Fact]
        public async Task GivesUpAfterMaxRetriesWithoutThrowing()
        {
            var settings = Build.Settings();
            settings.Tts.MaxRetries = 1;
            settings.Tts.FallbackToOtherProvider = false;

            var http = new FakeHttpTransport().EnqueueStatus(500);
            var result = await Router(settings, http).SynthesizeAsync("hello", "default", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(2, http.Calls);   // initial attempt + one retry
        }

        [Fact]
        public async Task AProviderThatThrowsIsContainedAndReported()
        {
            var settings = Build.Settings();
            settings.Tts.MaxRetries = 0;
            settings.Tts.FallbackToOtherProvider = false;
            settings.Tts.VoiceAliases.Clear();
            settings.Tts.VoiceAliases.Add(new VoiceAlias("default", "boom", "any"));

            var router = new TtsRouter(
                () => settings,
                new VoiceRegistry(() => settings),
                new List<ITtsProvider> { new ThrowingTtsProvider() });

            var result = await router.SynthesizeAsync("hello", "default", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("provider exploded", result.Error);
        }

        [Fact]
        public async Task UnavailableProviderIsSkippedAndTheFallbackIsUsed()
        {
            // Alias points at TikTok, which is unavailable by default; ElevenLabs picks it up.
            var settings = Build.Settings();
            settings.Tts.MaxRetries = 0;
            settings.Tts.VoiceAliases.Clear();
            settings.Tts.VoiceAliases.Add(new VoiceAlias("default", "tiktok", "en_us_001"));
            settings.Tts.VoiceAliases.Add(new VoiceAlias("backup", "elevenlabs", "voice-a"));

            var result = await Router(settings, new FakeHttpTransport().EnqueueAudio(Mp3))
                .SynthesizeAsync("hello", "default", CancellationToken.None);

            Assert.True(result.Success);
        }

        [Fact]
        public async Task NoUsableProviderReturnsAFailureRatherThanThrowing()
        {
            var settings = Build.Settings();
            settings.Tts.ElevenLabsApiKey = "";
            settings.Tts.MaxRetries = 0;

            var result = await Router(settings, new FakeHttpTransport())
                .SynthesizeAsync("hello", "default", CancellationToken.None);

            Assert.False(result.Success);
        }

        [Fact]
        public async Task UsesTheCacheOnARepeatedMessage()
        {
            var settings = Build.Settings();
            settings.Tts.MaxRetries = 0;

            var cache = new InMemoryCache();
            var http = new FakeHttpTransport().EnqueueAudio(Mp3);
            var router = new TtsRouter(
                () => settings,
                new VoiceRegistry(() => settings),
                new List<ITtsProvider> { new ElevenLabsTtsProvider(http, () => settings) },
                cache);

            await router.SynthesizeAsync("hello", "default", CancellationToken.None);
            var second = await router.SynthesizeAsync("hello", "default", CancellationToken.None);

            Assert.True(second.Success);
            Assert.Equal(1, http.Calls);
        }

        private sealed class InMemoryCache : IAudioCache
        {
            private readonly Dictionary<string, byte[]> _entries = new Dictionary<string, byte[]>();
            public bool TryGet(string key, out byte[] audio) => _entries.TryGetValue(key, out audio);
            public void Put(string key, byte[] audio) { _entries[key] = audio; }
            public void Purge() { _entries.Clear(); }
        }
    }
}
