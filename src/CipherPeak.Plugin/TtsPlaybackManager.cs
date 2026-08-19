using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CipherPeak.Core.Config;
using CipherPeak.Core.Logging;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Networking;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Plays received clips on every client, one at a time, out of an AudioSource parented to the
    /// speaking Bing Bong. The single-item "currently playing" slot is what guarantees two Bing
    /// Bongs never talk over each other, on every machine, regardless of network timing.
    /// </summary>
    internal sealed class TtsPlaybackManager : MonoBehaviour
    {
        private const string SourceObjectName = "CipherPeakTtsSource";

        /// <summary>Upper bound for Audio/Volume. Past roughly this, amplification turns into clipping.</summary>
        internal const float MaxVolume = 3f;

        private sealed class Job
        {
            public int MessageId;
            public int SpeakerViewId;
            public byte[] Audio;
        }

        private readonly Queue<Job> _pending = new Queue<Job>();
        private Job _current;
        private Coroutine _loop;
        private string _scratchDirectory;

        private Func<ModSettings> _settings;
        private ILog _log;

        public void Initialize(Func<ModSettings> settings, ILog log, string scratchDirectory)
        {
            _settings = settings;
            _log = log;
            _scratchDirectory = scratchDirectory;
            TryEnsureScratch();
            if (_loop == null) _loop = StartCoroutine(PlayLoop());
        }

        /// <summary>True while anything is queued or playing locally.</summary>
        public bool IsBusy => _current != null || _pending.Count > 0;

        public bool IsSpeaking(int speakerViewId) =>
            _current != null && _current.SpeakerViewId == speakerViewId;

        public bool IsPlaying(int messageId) =>
            (_current != null && _current.MessageId == messageId)
            || ContainsPending(messageId);

        public void Enqueue(int messageId, int speakerViewId, byte[] audio)
        {
            if (audio == null || audio.Length == 0) return;

            // Disabling the mod locally mutes TTS for this player, host or not.
            if (!_settings().Enabled) return;

            _pending.Enqueue(new Job { MessageId = messageId, SpeakerViewId = speakerViewId, Audio = audio });
        }

        /// <summary>Stops the current clip and drops everything queued locally.</summary>
        public void StopAll()
        {
            _pending.Clear();
            var current = _current;
            _current = null;
            if (current != null) StopSource(current.SpeakerViewId);
        }

        private void OnDestroy()
        {
            StopAll();
            CleanScratch();
        }

        private bool ContainsPending(int messageId)
        {
            foreach (var job in _pending)
                if (job.MessageId == messageId) return true;
            return false;
        }

        private IEnumerator PlayLoop()
        {
            while (true)
            {
                if (_pending.Count == 0)
                {
                    yield return null;
                    continue;
                }

                var job = _pending.Dequeue();
                _current = job;

                yield return PlayOne(job);

                // StopAll may have cleared it already.
                if (_current == job) _current = null;
            }
        }

        private IEnumerator PlayOne(Job job)
        {
            string path = null;
            AudioClip clip = null;

            try
            {
                TryEnsureScratch();
                path = Path.Combine(_scratchDirectory, "msg_" + job.MessageId + ".mp3");
                File.WriteAllBytes(path, job.Audio);
            }
            catch (Exception ex)
            {
                _log.Warn("Could not stage TTS audio for playback: " + ex.Message);
                yield break;
            }

            string url;
            try { url = new Uri(path).AbsoluteUri; }
            catch (Exception ex) { _log.Warn("Bad audio path: " + ex.Message); TryDelete(path); yield break; }

            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                var handler = request.downloadHandler as DownloadHandlerAudioClip;
                if (handler != null) handler.streamAudio = false;   // GetData needs the whole clip resident

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    _log.Warn("Could not decode TTS audio: " + request.error);
                    TryDelete(path);
                    yield break;
                }

                try { clip = DownloadHandlerAudioClip.GetContent(request); }
                catch (Exception ex) { _log.Warn("Could not build AudioClip: " + ex.Message); }
            }

            if (clip == null) { TryDelete(path); yield break; }

            var source = GetOrCreateSource(job.SpeakerViewId);
            if (source == null)
            {
                _log.Warn("Bing Bong " + job.SpeakerViewId + " vanished before it could speak; dropping the message.");
                Cleanup(clip, path);
                yield break;
            }

            ApplyAudioSettings(source);
            source.clip = clip;
            source.Play();

            if (_settings().Audio.AnimateMouth) TryAnimateMouth(source, clip);

            float deadline = Time.unscaledTime + clip.length + 1f;
            while (source != null && source.isPlaying && Time.unscaledTime < deadline && _current == job)
                yield return null;

            if (source != null && _current != job) source.Stop();
            Cleanup(clip, path);
        }

        private void Cleanup(AudioClip clip, string path)
        {
            if (clip != null)
            {
                clip.UnloadAudioData();
                Destroy(clip);
            }
            TryDelete(path);
        }

        private void ApplyAudioSettings(AudioSource source)
        {
            var audio = _settings().Audio;

            // Above 1 Unity amplifies rather than clamps, which is the only way to get a TTS clip
            // louder than the game's own sounds. Ceiling of 3 because past that it just clips.
            source.volume = Mathf.Clamp((float)audio.Volume, 0f, MaxVolume);
            source.minDistance = Mathf.Max(0.1f, (float)audio.MinDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 1f, (float)audio.MaxDistance);
        }

        /// <summary>The AudioSource lives on the Bing Bong itself, so the voice is always positional.</summary>
        private AudioSource GetOrCreateSource(int speakerViewId)
        {
            var view = PhotonView.Find(speakerViewId);
            if (view == null || view.gameObject == null) return null;

            var anchor = AnchorFor(view);
            if (anchor == null) return null;

            var existing = anchor.Find(SourceObjectName);
            if (existing != null)
            {
                var found = existing.GetComponent<AudioSource>();
                if (found != null) return found;
            }

            var holder = new GameObject(SourceObjectName);
            holder.transform.SetParent(anchor, false);
            holder.transform.localPosition = Vector3.zero;

            var source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;                       // fully 3D; never a global or player-attached voice
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;

            var sfxPlayer = SFX_Player.instance;
            if (sfxPlayer != null && sfxPlayer.defaultMixerGroup != null)
                source.outputAudioMixerGroup = sfxPlayer.defaultMixerGroup;   // respect the game's SFX slider

            ApplyAudioSettings(source);
            return source;
        }

        /// <summary>
        /// Where the voice physically comes from.
        ///
        /// For an item that is its own transform. For a scout it must be a body part: a Character's
        /// root transform does not follow the ragdoll, so a voice parented to it plays from wherever
        /// that root happens to sit - measured at 147 m from the player it belonged to.
        /// </summary>
        private static Transform AnchorFor(PhotonView view)
        {
            var character = view.GetComponent<Character>();
            if (character == null || character.refs == null) return view.transform;

            if (character.refs.head != null) return character.refs.head.transform;
            if (character.refs.hip != null) return character.refs.hip.transform;
            return view.transform;
        }

        private void StopSource(int speakerViewId)
        {
            var view = PhotonView.Find(speakerViewId);
            if (view == null || view.gameObject == null) return;

            var anchor = AnchorFor(view);
            if (anchor == null) return;

            var holder = anchor.Find(SourceObjectName);
            if (holder == null) return;

            var source = holder.GetComponent<AudioSource>();
            if (source != null) source.Stop();
        }

        private void TryAnimateMouth(AudioSource source, AudioClip clip)
        {
            try
            {
                var mouth = source.GetComponentInParent<BingBongMouth>();
                if (mouth == null) return;
                mouth.SampleAudioClip(clip);
            }
            catch (Exception ex)
            {
                // Purely cosmetic; a rig change must never stop the voice.
                _log.Info("Mouth animation skipped: " + ex.Message);
            }
        }

        private void TryEnsureScratch()
        {
            try { Directory.CreateDirectory(_scratchDirectory); }
            catch (Exception ex) { _log.Warn("Could not create playback scratch directory: " + ex.Message); }
        }

        private void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }

        private void CleanScratch()
        {
            try
            {
                if (Directory.Exists(_scratchDirectory)) Directory.Delete(_scratchDirectory, true);
            }
            catch { /* best effort */ }
        }
    }
}
