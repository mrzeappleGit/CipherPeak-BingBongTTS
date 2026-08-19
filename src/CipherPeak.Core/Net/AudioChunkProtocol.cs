using System;
using System.Collections.Generic;

namespace CipherPeak.Core.Net
{
    /// <summary>
    /// Splits a synthesized clip into fixed-size chunks for transport and reassembles them on the
    /// far side. Photon fragments large reliable payloads itself, but sending one clip as a single
    /// multi-hundred-kilobyte event risks the room's per-message limits, so the mod chunks
    /// explicitly and paces the sends.
    /// </summary>
    public static class AudioChunker
    {
        public const int DefaultChunkSize = 12000;

        public static List<byte[]> Split(byte[] audio, int chunkSize = DefaultChunkSize)
        {
            var chunks = new List<byte[]>();
            if (audio == null || audio.Length == 0) return chunks;
            if (chunkSize <= 0) chunkSize = DefaultChunkSize;

            for (int offset = 0; offset < audio.Length; offset += chunkSize)
            {
                int length = Math.Min(chunkSize, audio.Length - offset);
                var chunk = new byte[length];
                Buffer.BlockCopy(audio, offset, chunk, 0, length);
                chunks.Add(chunk);
            }
            return chunks;
        }
    }

    /// <summary>
    /// Reassembles chunked clips. Out-of-order and duplicate chunks are tolerated; partial
    /// messages are evicted once too many pile up so a dropped tail cannot leak memory.
    /// </summary>
    public sealed class AudioReassembler
    {
        private sealed class Pending
        {
            public byte[][] Chunks;
            public int Received;
            public int SpeakerId;
            public long Sequence;
        }

        private readonly int _maxConcurrent;
        private readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        private long _sequence;

        public AudioReassembler(int maxConcurrent = 4)
        {
            _maxConcurrent = Math.Max(1, maxConcurrent);
        }

        public int PendingCount => _pending.Count;

        /// <summary>
        /// Feeds one chunk. Returns true and fills <paramref name="audio"/> exactly once,
        /// on the chunk that completes the message.
        /// </summary>
        public bool Accept(int messageId, int speakerId, int chunkIndex, int chunkCount, byte[] data,
            out byte[] audio, out int completedSpeakerId)
        {
            audio = null;
            completedSpeakerId = 0;

            if (chunkCount <= 0 || chunkIndex < 0 || chunkIndex >= chunkCount || data == null) return false;

            Pending pending;
            if (!_pending.TryGetValue(messageId, out pending))
            {
                EvictOldestIfFull();
                pending = new Pending
                {
                    Chunks = new byte[chunkCount][],
                    SpeakerId = speakerId,
                    Sequence = ++_sequence
                };
                _pending[messageId] = pending;
            }

            if (pending.Chunks.Length != chunkCount) return false;      // sender changed its mind; ignore
            if (pending.Chunks[chunkIndex] != null) return false;       // duplicate

            pending.Chunks[chunkIndex] = data;
            pending.Received++;
            if (pending.Received < chunkCount) return false;

            int total = 0;
            for (int i = 0; i < chunkCount; i++) total += pending.Chunks[i].Length;

            audio = new byte[total];
            int offset = 0;
            for (int i = 0; i < chunkCount; i++)
            {
                Buffer.BlockCopy(pending.Chunks[i], 0, audio, offset, pending.Chunks[i].Length);
                offset += pending.Chunks[i].Length;
            }

            completedSpeakerId = pending.SpeakerId;
            _pending.Remove(messageId);
            return true;
        }

        public void Clear() { _pending.Clear(); }

        private void EvictOldestIfFull()
        {
            while (_pending.Count >= _maxConcurrent)
            {
                int oldestKey = 0;
                long oldestSeq = long.MaxValue;
                foreach (var kv in _pending)
                {
                    if (kv.Value.Sequence >= oldestSeq) continue;
                    oldestSeq = kv.Value.Sequence;
                    oldestKey = kv.Key;
                }
                _pending.Remove(oldestKey);
            }
        }
    }
}
