using CipherPeak.Core.Speech;
using Xunit;

namespace CipherPeak.Tests
{
    public class SpeakerRotationTests
    {
        [Fact]
        public void AlternatesBetweenTwoAvailableSpeakers()
        {
            var rotation = new SpeakerRotation();
            var both = new[] { true, true };

            Assert.Equal(0, rotation.Next(both));
            Assert.Equal(1, rotation.Next(both));
            Assert.Equal(0, rotation.Next(both));
            Assert.Equal(1, rotation.Next(both));
        }

        [Fact]
        public void UsesTheSurvivorWhileTheOtherIsBeingReplaced()
        {
            var rotation = new SpeakerRotation();

            Assert.Equal(0, rotation.Next(new[] { true, true }));
            Assert.Equal(0, rotation.Next(new[] { true, false }));
            Assert.Equal(0, rotation.Next(new[] { true, false }));
        }

        [Fact]
        public void ResumesAlternatingOnceTheReplacementArrives()
        {
            var rotation = new SpeakerRotation();

            Assert.Equal(0, rotation.Next(new[] { true, false }));
            Assert.Equal(1, rotation.Next(new[] { true, true }));
        }

        [Fact]
        public void ReturnsMinusOneWhenNobodyCanSpeak()
        {
            var rotation = new SpeakerRotation();

            Assert.Equal(-1, rotation.Next(new[] { false, false }));
            Assert.Equal(-1, rotation.Next(new bool[0]));
            Assert.Equal(-1, rotation.Next(null));
        }

        [Fact]
        public void ResetStartsFromTheFirstSpeakerAgain()
        {
            var rotation = new SpeakerRotation();
            rotation.Next(new[] { true, true });

            rotation.Reset();

            Assert.Equal(0, rotation.Next(new[] { true, true }));
        }
    }
}
