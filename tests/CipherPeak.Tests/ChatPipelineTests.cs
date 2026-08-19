using System;
using CipherPeak.Core.Commands;
using CipherPeak.Core.Config;
using CipherPeak.Core.Filtering;
using CipherPeak.Core.Ingestion;
using CipherPeak.Core.Queueing;
using CipherPeak.Core.Tts;
using Xunit;

namespace CipherPeak.Tests
{
    public class ChatPipelineTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private sealed class Harness
        {
            public ModSettings Settings;
            public ChatPipeline Pipeline;
            public SpeechQueue Queue;
            public ChatCommandProcessor Commands;
            public VoiceRegistry Voices;

            public Harness(Action<ModSettings> tweak = null)
            {
                Settings = Build.Settings();
                Settings.Queue.PerUserCooldownSeconds = 0;
                tweak?.Invoke(Settings);

                Voices = new VoiceRegistry(() => Settings);
                Queue = new SpeechQueue(() => Settings);
                Commands = new ChatCommandProcessor(() => Settings, Voices);
                Pipeline = new ChatPipeline(
                    () => Settings, Commands, new MessageFilter(() => Settings), Queue, Voices);
            }
        }

        [Fact]
        public void QueuesAnEligibleMessageWithTheResolvedAlias()
        {
            var h = new Harness();

            var outcome = h.Pipeline.Handle(Build.Message("hello chat"), T0, out var request);

            Assert.Equal(IngestOutcome.Queued, outcome);
            Assert.Equal("hello chat", request.Text);
            Assert.Equal("default", request.VoiceAlias);
        }

        [Fact]
        public void FilteredMessagesNeverReachTheQueue()
        {
            var h = new Harness();

            Assert.Equal(IngestOutcome.Filtered,
                h.Pipeline.Handle(Build.Message("check https://example.com"), T0, out _));
            Assert.Equal(0, h.Queue.Count);
        }

        [Fact]
        public void CommandsAreConsumedRatherThanSpoken()
        {
            var h = new Harness(s => s.Tts.AllowVoiceCommand = true);
            h.Settings.Tts.VoiceAliases.Add(new VoiceAlias("robot", "elevenlabs", "voice-b"));

            Assert.Equal(IngestOutcome.Command, h.Pipeline.Handle(Build.Message("!voice robot"), T0, out _));
            Assert.Equal(0, h.Queue.Count);

            h.Pipeline.Handle(Build.Message("now with the robot voice"), T0, out var request);
            Assert.Equal("robot", request.VoiceAlias);
        }

        [Fact]
        public void ModeratorsCanSkipAndClear()
        {
            var h = new Harness();
            int skips = 0, clears = 0;
            h.Commands.SkipRequested += () => skips++;
            h.Commands.ClearRequested += () => clears++;

            h.Pipeline.Handle(Build.Message("!tts skip", login: "mod", mod: true), T0, out _);
            h.Pipeline.Handle(Build.Message("!tts clear", login: "mod", mod: true), T0, out _);

            Assert.Equal(1, skips);
            Assert.Equal(1, clears);
        }

        [Fact]
        public void ViewersCannotSkipOrClear()
        {
            var h = new Harness();
            int skips = 0;
            h.Commands.SkipRequested += () => skips++;

            h.Pipeline.Handle(Build.Message("!tts skip"), T0, out _);

            Assert.Equal(0, skips);
        }

        [Fact]
        public void ModeratorsCanToggleTts()
        {
            var h = new Harness();
            bool? requested = null;
            h.Commands.EnabledChangeRequested += value => requested = value;

            h.Pipeline.Handle(Build.Message("!tts off", login: "streamer", broadcaster: true), T0, out _);

            Assert.Equal(false, requested);
        }

        [Fact]
        public void NothingIsIngestedWhileDisabled()
        {
            var h = new Harness(s => s.Enabled = false);

            Assert.Equal(IngestOutcome.Disabled, h.Pipeline.Handle(Build.Message("hello"), T0, out _));
            Assert.Equal(0, h.Queue.Count);
        }

        [Fact]
        public void CooldownRejectionsAreReportedNotQueued()
        {
            var h = new Harness(s => s.Queue.PerUserCooldownSeconds = 30);

            Assert.Equal(IngestOutcome.Queued, h.Pipeline.Handle(Build.Message("first"), T0, out _));
            Assert.Equal(IngestOutcome.Rejected,
                h.Pipeline.Handle(Build.Message("second"), T0.AddSeconds(1), out _));
            Assert.Equal(1, h.Queue.Count);
        }

        [Fact]
        public void VoiceCommandIsIgnoredWhenTheFeatureIsOff()
        {
            var h = new Harness(s => s.Tts.AllowVoiceCommand = false);
            h.Settings.Tts.VoiceAliases.Add(new VoiceAlias("robot", "elevenlabs", "voice-b"));

            h.Pipeline.Handle(Build.Message("!voice robot"), T0, out _);
            h.Pipeline.Handle(Build.Message("still the default voice"), T0, out var request);

            Assert.Equal("default", request.VoiceAlias);
        }

        [Fact]
        public void FullQueueThenDrainRestoresIngestion()
        {
            var h = new Harness(s => s.Queue.MaxQueuedMessages = 1);

            Assert.Equal(IngestOutcome.Queued, h.Pipeline.Handle(Build.Message("one", login: "a"), T0, out _));
            Assert.Equal(IngestOutcome.Rejected, h.Pipeline.Handle(Build.Message("two", login: "b"), T0, out _));

            h.Queue.TryDequeue(out _);
            Assert.Equal(IngestOutcome.Queued, h.Pipeline.Handle(Build.Message("three", login: "c"), T0, out _));
        }
    }
}
