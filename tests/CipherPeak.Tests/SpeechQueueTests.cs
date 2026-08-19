using System;
using CipherPeak.Core.Queueing;
using Xunit;

namespace CipherPeak.Tests
{
    public class SpeechQueueTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void PreservesFifoOrder()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 0;

            var queue = new SpeechQueue(settings);
            queue.TryEnqueue("a", "A", "first", "default", T0, out _);
            queue.TryEnqueue("b", "B", "second", "default", T0, out _);
            queue.TryEnqueue("c", "C", "third", "default", T0, out _);

            Assert.True(queue.TryDequeue(out var first));
            Assert.True(queue.TryDequeue(out var second));
            Assert.True(queue.TryDequeue(out var third));

            Assert.Equal("first", first.Text);
            Assert.Equal("second", second.Text);
            Assert.Equal("third", third.Text);
            Assert.False(queue.TryDequeue(out _));
        }

        [Fact]
        public void AssignsIncreasingIds()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 0;

            var queue = new SpeechQueue(settings);
            queue.TryEnqueue("a", "A", "one", "default", T0, out var first);
            queue.TryEnqueue("b", "B", "two", "default", T0, out var second);

            Assert.True(second.Id > first.Id);
        }

        [Fact]
        public void EnforcesPerUserCooldown()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 20;

            var queue = new SpeechQueue(settings);

            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("bob", "Bob", "one", "default", T0, out _));
            Assert.Equal(EnqueueVerdict.UserCooldown,
                queue.TryEnqueue("bob", "Bob", "two", "default", T0.AddSeconds(19), out _));
            Assert.Equal(EnqueueVerdict.Queued,
                queue.TryEnqueue("bob", "Bob", "three", "default", T0.AddSeconds(20), out _));
        }

        [Fact]
        public void PerUserCooldownIsPerUser()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 20;

            var queue = new SpeechQueue(settings);
            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("bob", "Bob", "one", "default", T0, out _));
            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("amy", "Amy", "one", "default", T0, out _));
        }

        [Fact]
        public void EnforcesGlobalCooldown()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 0;
            settings.Queue.GlobalCooldownSeconds = 5;

            var queue = new SpeechQueue(settings);
            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("a", "A", "one", "default", T0, out _));
            Assert.Equal(EnqueueVerdict.GlobalCooldown,
                queue.TryEnqueue("b", "B", "two", "default", T0.AddSeconds(4), out _));
            Assert.Equal(EnqueueVerdict.Queued,
                queue.TryEnqueue("b", "B", "two", "default", T0.AddSeconds(5), out _));
        }

        [Fact]
        public void EnforcesQueueCapacity()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 0;
            settings.Queue.MaxQueuedMessages = 2;

            var queue = new SpeechQueue(settings);
            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("a", "A", "1", "default", T0, out _));
            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("b", "B", "2", "default", T0, out _));
            Assert.Equal(EnqueueVerdict.QueueFull, queue.TryEnqueue("c", "C", "3", "default", T0, out _));

            queue.TryDequeue(out _);
            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("c", "C", "3", "default", T0, out _));
        }

        [Fact]
        public void RejectsEverythingWhileDisabled()
        {
            var settings = Build.Settings();
            settings.Enabled = false;

            var queue = new SpeechQueue(settings);
            Assert.Equal(EnqueueVerdict.Disabled, queue.TryEnqueue("a", "A", "hi", "default", T0, out _));
        }

        [Fact]
        public void ClearDropsPendingButKeepsCooldowns()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 20;

            var queue = new SpeechQueue(settings);
            queue.TryEnqueue("bob", "Bob", "one", "default", T0, out _);

            Assert.Equal(1, queue.Clear());
            Assert.Equal(0, queue.Count);
            Assert.Equal(EnqueueVerdict.UserCooldown,
                queue.TryEnqueue("bob", "Bob", "two", "default", T0.AddSeconds(1), out _));
        }

        [Fact]
        public void ResetClearsCooldownsToo()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 20;

            var queue = new SpeechQueue(settings);
            queue.TryEnqueue("bob", "Bob", "one", "default", T0, out _);
            queue.Reset();

            Assert.Equal(EnqueueVerdict.Queued,
                queue.TryEnqueue("bob", "Bob", "two", "default", T0.AddSeconds(1), out _));
        }

        [Fact]
        public void RejectedMessagesDoNotStartACooldown()
        {
            var settings = Build.Settings();
            settings.Queue.PerUserCooldownSeconds = 0;
            settings.Queue.MaxQueuedMessages = 1;

            var queue = new SpeechQueue(settings);
            queue.TryEnqueue("a", "A", "1", "default", T0, out _);
            Assert.Equal(EnqueueVerdict.QueueFull, queue.TryEnqueue("b", "B", "2", "default", T0, out _));

            queue.TryDequeue(out _);
            settings.Queue.PerUserCooldownSeconds = 20;
            Assert.Equal(EnqueueVerdict.Queued, queue.TryEnqueue("b", "B", "2", "default", T0, out _));
        }
    }
}
