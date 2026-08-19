using System.Linq;
using CipherPeak.Core.Net;
using Xunit;

namespace CipherPeak.Tests
{
    public class AudioChunkProtocolTests
    {
        private static byte[] Payload(int length)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = (byte)(i % 251);
            return data;
        }

        [Fact]
        public void SplitAndReassembleRoundTrips()
        {
            var original = Payload(30000);
            var chunks = AudioChunker.Split(original, 4096);
            var reassembler = new AudioReassembler();

            byte[] result = null;
            for (int i = 0; i < chunks.Count; i++)
            {
                if (reassembler.Accept(7, 42, i, chunks.Count, chunks[i], out var audio, out var speaker))
                {
                    result = audio;
                    Assert.Equal(42, speaker);
                }
            }

            Assert.NotNull(result);
            Assert.Equal(original, result);
        }

        [Fact]
        public void CompletesOnlyOnce()
        {
            var chunks = AudioChunker.Split(Payload(10), 4);
            var reassembler = new AudioReassembler();

            int completions = 0;
            for (int i = 0; i < chunks.Count; i++)
                if (reassembler.Accept(1, 1, i, chunks.Count, chunks[i], out _, out _)) completions++;

            Assert.Equal(1, completions);
        }

        [Fact]
        public void ToleratesOutOfOrderChunks()
        {
            var original = Payload(9000);
            var chunks = AudioChunker.Split(original, 1000);
            var reassembler = new AudioReassembler();

            byte[] result = null;
            foreach (int i in Enumerable.Range(0, chunks.Count).Reverse())
                if (reassembler.Accept(3, 5, i, chunks.Count, chunks[i], out var audio, out _)) result = audio;

            Assert.Equal(original, result);
        }

        [Fact]
        public void IgnoresDuplicateChunks()
        {
            var chunks = AudioChunker.Split(Payload(8), 4);
            var reassembler = new AudioReassembler();

            Assert.False(reassembler.Accept(1, 1, 0, 2, chunks[0], out _, out _));
            Assert.False(reassembler.Accept(1, 1, 0, 2, chunks[0], out _, out _));
            Assert.True(reassembler.Accept(1, 1, 1, 2, chunks[1], out _, out _));
        }

        [Theory]
        [InlineData(-1, 2)]
        [InlineData(2, 2)]
        [InlineData(0, 0)]
        public void RejectsNonsensicalIndices(int index, int count)
        {
            var reassembler = new AudioReassembler();
            Assert.False(reassembler.Accept(1, 1, index, count, new byte[] { 1 }, out _, out _));
        }

        [Fact]
        public void RejectsNullData()
        {
            var reassembler = new AudioReassembler();
            Assert.False(reassembler.Accept(1, 1, 0, 1, null, out _, out _));
        }

        [Fact]
        public void InterleavedMessagesReassembleIndependently()
        {
            var first = Payload(2000);
            var second = Payload(3000);
            var firstChunks = AudioChunker.Split(first, 1000);
            var secondChunks = AudioChunker.Split(second, 1000);

            var reassembler = new AudioReassembler();

            reassembler.Accept(1, 10, 0, firstChunks.Count, firstChunks[0], out _, out _);
            reassembler.Accept(2, 20, 0, secondChunks.Count, secondChunks[0], out _, out _);
            reassembler.Accept(2, 20, 1, secondChunks.Count, secondChunks[1], out _, out _);

            Assert.True(reassembler.Accept(1, 10, 1, firstChunks.Count, firstChunks[1],
                out var firstAudio, out var firstSpeaker));
            Assert.Equal(first, firstAudio);
            Assert.Equal(10, firstSpeaker);

            Assert.True(reassembler.Accept(2, 20, 2, secondChunks.Count, secondChunks[2],
                out var secondAudio, out var secondSpeaker));
            Assert.Equal(second, secondAudio);
            Assert.Equal(20, secondSpeaker);
        }

        [Fact]
        public void AbandonedPartialMessagesAreEvictedInsteadOfLeaking()
        {
            var reassembler = new AudioReassembler(maxConcurrent: 2);

            for (int messageId = 1; messageId <= 10; messageId++)
                reassembler.Accept(messageId, 1, 0, 5, new byte[] { 1 }, out _, out _);

            Assert.True(reassembler.PendingCount <= 2);
        }

        [Fact]
        public void EmptyAudioProducesNoChunks()
        {
            Assert.Empty(AudioChunker.Split(null));
            Assert.Empty(AudioChunker.Split(new byte[0]));
        }
    }
}
