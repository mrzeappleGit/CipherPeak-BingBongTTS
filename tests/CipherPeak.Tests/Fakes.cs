using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CipherPeak.Core.BingBong;
using CipherPeak.Core.Config;
using CipherPeak.Core.Net;
using CipherPeak.Core.Tts;
using CipherPeak.Core.Twitch;

namespace CipherPeak.Tests
{
    internal static class Build
    {
        public static ModSettings Settings()
        {
            var settings = new ModSettings();
            settings.Tts.VoiceAliases.Add(new VoiceAlias("default", "elevenlabs", "voice-a"));
            settings.Tts.ElevenLabsApiKey = "test-key";
            return settings;
        }

        public static ChatMessage Message(
            string text,
            string login = "viewer",
            bool sub = false,
            bool mod = false,
            bool broadcaster = false)
        {
            return new ChatMessage
            {
                Login = login,
                DisplayName = login,
                Text = text,
                IsSubscriber = sub,
                IsModerator = mod || broadcaster,
                IsBroadcaster = broadcaster,
                ReceivedAt = DateTimeOffset.UnixEpoch
            };
        }
    }

    /// <summary>Scriptable transport: one queued response per call, then it repeats the last one.</summary>
    internal sealed class FakeHttpTransport : IHttpTransport
    {
        private readonly Queue<HttpResponse> _responses = new Queue<HttpResponse>();
        private HttpResponse _last;

        public int Calls { get; private set; }
        public string LastUrl { get; private set; }
        public string LastBody { get; private set; }
        public IReadOnlyDictionary<string, string> LastHeaders { get; private set; }

        public FakeHttpTransport Enqueue(HttpResponse response)
        {
            _responses.Enqueue(response);
            return this;
        }

        public FakeHttpTransport EnqueueAudio(byte[] audio)
            => Enqueue(new HttpResponse { StatusCode = 200, Body = audio, ContentType = "audio/mpeg" });

        public FakeHttpTransport EnqueueStatus(int status, double retryAfter = 0)
            => Enqueue(new HttpResponse { StatusCode = status, Body = Array.Empty<byte>(), RetryAfterSeconds = retryAfter });

        public FakeHttpTransport EnqueueTransportError(string error)
            => Enqueue(new HttpResponse { TransportError = error });

        public Task<HttpResponse> PostAsync(
            string url, IReadOnlyDictionary<string, string> headers, string contentType,
            byte[] body, int timeoutSeconds, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = url;
            LastHeaders = headers;
            LastBody = body == null ? "" : System.Text.Encoding.UTF8.GetString(body);

            if (_responses.Count > 0) _last = _responses.Dequeue();
            return Task.FromResult(_last ?? new HttpResponse { TransportError = "no response scripted" });
        }
    }

    internal sealed class ThrowingTtsProvider : ITtsProvider
    {
        public string Name => "boom";
        public bool IsAvailable(out string reason) { reason = null; return true; }

        public Task<TtsResult> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("provider exploded");
    }

    /// <summary>In-memory Bing Bong world. Handles are 1-based ids; "kill" simulates loss.</summary>
    internal sealed class FakeBingBongWorld : IBingBongWorld
    {
        private readonly HashSet<int> _alive = new HashSet<int>();

        /// <summary>Exists in the world but fails the liveness rule: out of range, or fallen away.</summary>
        private readonly HashSet<int> _presentButDead = new HashSet<int>();

        private int _nextHandle = 1;

        public bool CanManage { get; set; } = true;
        public bool SpawnFails { get; set; }
        public int SpawnCalls { get; private set; }
        public int DespawnCalls { get; private set; }

        public IReadOnlyList<int> FindManaged()
        {
            var all = new List<int>(_alive);
            all.AddRange(_presentButDead);
            all.Sort();
            return all;
        }

        public int Spawn()
        {
            SpawnCalls++;
            if (SpawnFails) return 0;
            int handle = _nextHandle++;
            _alive.Add(handle);
            return handle;
        }

        public void Despawn(int handle)
        {
            DespawnCalls++;
            _alive.Remove(handle);
        }

        public bool IsAlive(int handle) => _alive.Contains(handle);

        /// <summary>Simulates the entity being destroyed, falling out of bounds, or drifting too far.</summary>
        public void Lose(int handle) => _alive.Remove(handle);

        /// <summary>Simulates an entity that already exists when the director starts looking.</summary>
        public int PlantExisting()
        {
            int handle = _nextHandle++;
            _alive.Add(handle);
            return handle;
        }

        /// <summary>Simulates one lying in the world that the liveness rule rejects, e.g. left behind.</summary>
        public int PlantOutOfReach()
        {
            int handle = _nextHandle++;
            _presentButDead.Add(handle);
            return handle;
        }

        public int AliveCount => _alive.Count;
    }
}
