using System;
using System.Collections;
using CipherPeak.Core.Logging;
using CipherPeak.Core.Net;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace CipherPeak.Plugin
{
    /// <summary>
    /// Multiplayer synchronisation for spoken audio.
    ///
    /// The host synthesizes once and broadcasts the clip to <see cref="ReceiverGroup.All"/>, which
    /// includes itself, so every machine - host included - runs the exact same receive-and-play
    /// path and therefore hears each message exactly once. Clips are chunked and paced so a long
    /// message cannot blow the room's per-message limits.
    ///
    /// Event code 177 is well clear of PEAK's own custom commands (which use codes below 6) and of
    /// Photon's reserved range (200+).
    /// </summary>
    internal sealed class NetworkAudioBus : MonoBehaviour, IOnEventCallback
    {
        internal const byte EventCode = 177;

        private const byte OpChunk = 1;
        private const byte OpStop = 2;

        private const int ChunksPerFrame = 4;

        private readonly AudioReassembler _reassembler = new AudioReassembler();

        private ILog _log;
        private TtsPlaybackManager _playback;
        private bool _registered;

        public void Initialize(ILog log, TtsPlaybackManager playback)
        {
            _log = log;
            _playback = playback;
            if (_registered) return;
            PhotonNetwork.AddCallbackTarget(this);
            _registered = true;
        }

        private void OnDestroy()
        {
            if (!_registered) return;
            PhotonNetwork.RemoveCallbackTarget(this);
            _registered = false;
        }

        /// <summary>Host side: fan a synthesized clip out to the room, including back to itself.</summary>
        public IEnumerator Broadcast(int messageId, int speakerViewId, byte[] audio)
        {
            if (audio == null || audio.Length == 0) yield break;

            if (!PhotonNetwork.InRoom)
            {
                // Offline or between rooms: still play locally so single-player works.
                _playback.Enqueue(messageId, speakerViewId, audio);
                yield break;
            }

            var chunks = AudioChunker.Split(audio);
            var options = new RaiseEventOptions { Receivers = ReceiverGroup.All };

            for (int i = 0; i < chunks.Count; i++)
            {
                bool sent;
                try
                {
                    sent = PhotonNetwork.RaiseEvent(
                        EventCode,
                        new object[]
                        {
                            OpChunk, messageId, speakerViewId,
                            (short)i, (short)chunks.Count, chunks[i]
                        },
                        options,
                        SendOptions.SendReliable);
                }
                catch (Exception ex)
                {
                    _log.Warn("Failed to broadcast audio chunk " + i + ": " + ex.Message);
                    yield break;
                }

                if (!sent)
                {
                    _log.Warn("Photon refused audio chunk " + i + "; dropping the rest of this message.");
                    yield break;
                }

                if ((i + 1) % ChunksPerFrame == 0) yield return null;
            }
        }

        /// <summary>
        /// Stop the current message. Always stops locally; only the host tells the rest of the room,
        /// so a client leaving a run or toggling its own TTS cannot silence everybody else.
        /// </summary>
        public void BroadcastStop()
        {
            _playback.StopAll();
            _reassembler.Clear();

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

            try
            {
                PhotonNetwork.RaiseEvent(
                    EventCode,
                    new object[] { OpStop },
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                    SendOptions.SendReliable);
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to broadcast stop: " + ex.Message);
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != EventCode) return;

            var payload = photonEvent.CustomData as object[];
            if (payload == null || payload.Length == 0) return;

            try
            {
                byte op = Convert.ToByte(payload[0]);
                switch (op)
                {
                    case OpChunk: HandleChunk(payload); break;
                    case OpStop: _playback.StopAll(); _reassembler.Clear(); break;
                }
            }
            catch (Exception ex)
            {
                // A malformed or hostile payload must never take the game down.
                _log.Warn("Ignoring malformed CipherPeak network event: " + ex.Message);
            }
        }

        private void HandleChunk(object[] payload)
        {
            if (payload.Length < 6) return;

            int messageId = Convert.ToInt32(payload[1]);
            int speakerViewId = Convert.ToInt32(payload[2]);
            int chunkIndex = Convert.ToInt32(payload[3]);
            int chunkCount = Convert.ToInt32(payload[4]);
            var data = payload[5] as byte[];
            if (data == null) return;

            byte[] audio;
            int completedSpeaker;
            if (!_reassembler.Accept(messageId, speakerViewId, chunkIndex, chunkCount, data,
                    out audio, out completedSpeaker))
                return;

            _playback.Enqueue(messageId, completedSpeaker, audio);
        }
    }
}
