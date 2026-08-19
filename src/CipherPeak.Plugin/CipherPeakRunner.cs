using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CipherPeak.Core.BingBong;
using CipherPeak.Core.Commands;
using CipherPeak.Core.Config;
using CipherPeak.Core.Filtering;
using CipherPeak.Core.Ingestion;
using CipherPeak.Core.Logging;
using CipherPeak.Core.Net;
using CipherPeak.Core.Queueing;
using CipherPeak.Core.Speech;
using CipherPeak.Core.Tts;
using CipherPeak.Core.Twitch;
using Photon.Pun;
using UnityEngine;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Ties the modules together and owns all game-thread state. Everything host-only lives behind
    /// <see cref="IsHost"/>; clients only ever receive audio.
    /// </summary>
    internal sealed class CipherPeakRunner : MonoBehaviour
    {
        private PluginConfig _config;
        private ILog _log;

        private TwitchIrcClient _twitch;
        private MessageFilter _filter;
        private SpeechQueue _queue;
        private VoiceRegistry _voices;
        private ChatCommandProcessor _commands;
        private ChatPipeline _pipeline;
        private TtsRouter _router;
        private HttpClientTransport _transport;
        private FileAudioCache _cache;

        private BingBongDirector _director;
        private UnityBingBongWorld _world;

        private TtsPlaybackManager _playback;
        private NetworkAudioBus _bus;

        private CancellationTokenSource _ttsCancellation;
        private Coroutine _pump;

        private float _nextLifecycleTick;
        private bool _wasInRun;
        private bool _wasHost;
        private bool _wasDriving;

        private ModSettings Settings => _config.Settings;

        private static bool IsHost => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;

        /// <summary>
        /// A run we are allowed to act in: a gameplay scene with a live map and a spawned local
        /// character that is not sitting in the airport lobby.
        /// </summary>
        private static bool InPlayableRun
        {
            get
            {
                if (GameHandler.Instance == null) return false;
                if (!GameHandler.IsInGameplayScene) return false;
                if (!MapHandler.Exists) return false;

                var local = Character.localCharacter;
                return local != null && !local.inAirport;
            }
        }

        public void Initialize(PluginConfig config, ILog log, string cacheDirectory, string playbackDirectory)
        {
            _config = config;
            _log = log;

            _playback = gameObject.AddComponent<TtsPlaybackManager>();
            _playback.Initialize(() => Settings, _log, playbackDirectory);

            _bus = gameObject.AddComponent<NetworkAudioBus>();
            _bus.Initialize(_log, _playback);

            _transport = new HttpClientTransport();
            _cache = new FileAudioCache(cacheDirectory,
                () => Math.Max(0L, (long)Settings.Tts.CacheMaxMegabytes * 1024L * 1024L), _log);

            _voices = new VoiceRegistry(() => Settings);
            _filter = new MessageFilter(() => Settings);
            _queue = new SpeechQueue(() => Settings);

            _commands = new ChatCommandProcessor(() => Settings, _voices, _log);
            _commands.SkipRequested += Skip;
            _commands.ClearRequested += ClearQueue;
            _commands.EnabledChangeRequested += SetEnabled;

            _pipeline = new ChatPipeline(() => Settings, _commands, _filter, _queue, _voices, _log);

            var providers = new List<ITtsProvider>
            {
                new ElevenLabsTtsProvider(_transport, () => Settings),
                new TikTokTtsProvider(_transport, () => Settings)
            };
            _router = new TtsRouter(() => Settings, _voices, providers, _cache, _log);

            _world = new UnityBingBongWorld(
                () => Settings,
                () => InPlayableRun,
                viewId => _playback.IsSpeaking(viewId),
                _log);
            _director = new BingBongDirector(_world, _log);

            _twitch = new TwitchIrcClient(() => Settings, _log);
            _ttsCancellation = new CancellationTokenSource();
            _pump = StartCoroutine(SpeakPump());
        }

        private void Update()
        {
            if (_config == null) return;

            HandleHotkeys();

            bool enabled = Settings.Enabled;
            bool inRun = enabled && InPlayableRun;
            bool host = inRun && IsHost;
            bool driving = inRun && (IsHost || Settings.Twitch.UseMyOwnChat);

            if (_wasInRun && !inRun) OnRunEnded();
            if (_wasDriving && !driving) OnStoppedDrivingChat();
            if (_wasHost && !host) OnLostHost();
            if (!_wasHost && host) OnBecameHost();
            if (!_wasDriving && driving) OnStartedDrivingChat();

            _wasInRun = inRun;
            _wasHost = host;
            _wasDriving = driving;

            if (host && Time.unscaledTime >= _nextLifecycleTick)
            {
                _nextLifecycleTick = Time.unscaledTime + (float)Settings.BingBong.LifecycleTickSeconds;
                try { _director.Tick(); }
                catch (Exception ex) { _log.Error("Bing Bong lifecycle tick failed: " + ex.Message); }
            }

            if (driving) DrainChat();
        }

        /// <summary>
        /// True when this machine is reading a Twitch chat of its own. The host always is. A client
        /// only is when it has opted in, and then its audio never leaves the machine.
        /// </summary>
        private bool DrivingChat => Settings.Enabled && InPlayableRun && (IsHost || Settings.Twitch.UseMyOwnChat);

        /// <summary>Whether what this machine speaks should go out to the lobby, or stay local.</summary>
        private bool BroadcastsSpeech => IsHost;

        private void OnStartedDrivingChat()
        {
            _log.Info(IsHost
                ? "Now hosting; connecting to Twitch for the lobby."
                : "Reading your own Twitch chat; only you will hear it.");
            _twitch.Start();
        }

        private void OnStoppedDrivingChat()
        {
            _log.Info("No longer reading Twitch; disconnecting.");
            _twitch.Stop();
            _queue.Reset();
            _filter.Reset();
        }

        private void OnBecameHost()
        {
            _log.Info("Taking over the Bing Bongs.");
            _nextLifecycleTick = 0f;   // reconcile immediately
        }

        private void OnLostHost()
        {
            _log.Info("No longer hosting; Bing Bongs stay with the new host.");
            // Deliberately do NOT despawn: the new master client adopts them via the marker.
            _director.Forget();
        }

        private void OnRunEnded()
        {
            _log.Info("Run ended; removing mod Bing Bongs and clearing the queue.");
            if (IsHost)
            {
                try { _director.ReleaseAll(); }
                catch (Exception ex) { _log.Warn("Cleanup on run end failed: " + ex.Message); }
            }

            _queue.Reset();
            _filter.Reset();
            if (BroadcastsSpeech) _bus.BroadcastStop();
            else _playback.StopAll();
        }

        private void HandleHotkeys()
        {
            if (Hotkey.Pressed(_config.SkipKey)) Skip();
            if (Hotkey.Pressed(_config.ClearKey)) ClearQueue();
            if (Hotkey.Pressed(_config.ToggleKey)) SetEnabled(!Settings.Enabled);
            if (Hotkey.Pressed(_config.BingBongSlotKey)) BingBongSlot.ToggleEquip();
            if (Hotkey.Pressed(_config.BackpackKey)) BackpackHotkey.Open();
        }

        private void DrainChat()
        {
            ChatMessage message;
            var now = DateTimeOffset.UtcNow;
            int budget = 32;   // never let a chat burst stall a frame

            while (budget-- > 0 && _twitch.TryDequeue(out message))
            {
                SpeechRequest request;
                try { _pipeline.Handle(message, now, out request); }
                catch (Exception ex) { _log.Warn("Chat message could not be processed: " + ex.Message); }
            }
        }

        /// <summary>
        /// Host-side sequencer: one message at a time, alternating speakers, and the next message is
        /// only synthesized once the previous one has finished playing here.
        /// </summary>
        private IEnumerator SpeakPump()
        {
            var rotation = new SpeakerRotation();
            var wait = new WaitForSecondsRealtime(0.2f);

            while (true)
            {
                if (_config == null || !DrivingChat || _playback.IsBusy)
                {
                    yield return wait;
                    continue;
                }

                SpeechRequest request;
                if (!_queue.TryDequeue(out request))
                {
                    yield return wait;
                    continue;
                }

                var task = SynthesizeSafely(request);
                while (!task.IsCompleted) yield return null;

                var result = task.Result;
                if (result == null || !result.Success)
                {
                    _log.Warn("Skipping message from " + request.Login + ": " +
                              (result != null ? result.Error : "unknown TTS failure"));
                    continue;
                }

                int speakerViewId = PickSpeaker(rotation);
                if (speakerViewId == 0)
                {
                    _log.Warn("No Bing Bong available to speak; dropping this message while one is restored.");
                    continue;
                }

                _log.Info("Speaking " + request.Login + "'s message through " + _world.Describe(speakerViewId) +
                          " (" + result.Audio.Length + " bytes" + (BroadcastsSpeech ? "" : ", locally") + ").");

                if (BroadcastsSpeech)
                    yield return _bus.Broadcast(request.Id, speakerViewId, result.Audio);
                else
                    _playback.Enqueue(request.Id, speakerViewId, result.Audio);   // your chat, your ears only

                // RaiseEvent to ReceiverGroup.All round-trips through the Photon server, so our own
                // copy lands a moment after sending. Wait for it to arrive before treating
                // "not playing" as "finished", or the next message would start on top of this one.
                float arrivalDeadline = Time.unscaledTime + 5f;
                while (!_playback.IsPlaying(request.Id) && Time.unscaledTime < arrivalDeadline)
                    yield return null;

                float playbackDeadline = Time.unscaledTime + 120f;
                while (_playback.IsPlaying(request.Id) && Time.unscaledTime < playbackDeadline)
                    yield return null;

                float gap = (float)Settings.Queue.GapBetweenMessagesSeconds;
                if (gap > 0f) yield return new WaitForSecondsRealtime(gap);
            }
        }

        private Task<TtsResult> SynthesizeSafely(SpeechRequest request)
        {
            try
            {
                return _router.SynthesizeAsync(request.Text, request.VoiceAlias, _ttsCancellation.Token);
            }
            catch (Exception ex)
            {
                return Task.FromResult(TtsResult.Fail("TTS dispatch failed: " + ex.Message));
            }
        }

        /// <summary>
        /// Alternates between the two managed Bing Bongs, falling back to whichever one is alive
        /// while the other is being replaced.
        /// </summary>
        private int PickSpeaker(SpeakerRotation rotation)
        {
            // A client reading its own chat has no director handles - only the host manages Bing Bongs.
            if (!IsHost) return _world.LocalSpeaker();

            var handles = _director.Handles;
            var available = new bool[handles.Count];
            for (int i = 0; i < handles.Count; i++)
                available[i] = _world.IsAlive(handles[i]) && _world.CanBeHeard(handles[i]);

            int index = rotation.Next(available);
            if (index >= 0) return handles[index];

            // Nothing within earshot. Anything alive still beats silence.
            for (int i = 0; i < handles.Count; i++)
                available[i] = _world.IsAlive(handles[i]);

            index = rotation.Next(available);
            return index < 0 ? 0 : handles[index];
        }

        private void Skip()
        {
            if (!IsHost) return;
            _bus.BroadcastStop();
            _log.Info("Skipped the current message.");
        }

        private void ClearQueue()
        {
            if (!IsHost) return;
            int dropped = _queue.Clear();
            _bus.BroadcastStop();
            _log.Info("Cleared " + dropped + " queued message(s).");
        }

        private void SetEnabled(bool value)
        {
            _config.SetEnabled(value);
            _log.Info("TTS " + (value ? "enabled" : "disabled") + ".");

            if (value) return;

            _twitch.Stop();
            _queue.Reset();
            _bus.BroadcastStop();
            if (IsHost) _director.ReleaseAll();
        }

        public void Shutdown()
        {
            if (_pump != null) { StopCoroutine(_pump); _pump = null; }

            try { _ttsCancellation?.Cancel(); } catch { /* already disposed */ }

            if (_twitch != null) { _twitch.Dispose(); _twitch = null; }

            if (_director != null && IsHost)
            {
                try { _director.ReleaseAll(); } catch { /* leaving anyway */ }
            }

            if (_playback != null) _playback.StopAll();

            // The clip cache is meant to survive restarts; only reclaim it if caching is off.
            if (_cache != null && _config != null && !Settings.Tts.CacheEnabled) _cache.Purge();

            if (_transport != null) { _transport.Dispose(); _transport = null; }

            _ttsCancellation?.Dispose();
            _ttsCancellation = null;
        }

        private void OnDestroy() { Shutdown(); }
    }
}
