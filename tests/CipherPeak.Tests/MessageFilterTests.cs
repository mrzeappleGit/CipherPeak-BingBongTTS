using CipherPeak.Core.Filtering;
using Xunit;

namespace CipherPeak.Tests
{
    public class MessageFilterTests
    {
        [Fact]
        public void AcceptsAnOrdinaryMessage()
        {
            var filter = new MessageFilter(Build.Settings());
            var result = filter.Evaluate(Build.Message("hello from chat"));

            Assert.True(result.Accepted);
            Assert.Equal("hello from chat", result.Text);
        }

        [Fact]
        public void NormalisesWhitespace()
        {
            var filter = new MessageFilter(Build.Settings());
            var result = filter.Evaluate(Build.Message("  hello    there \t world  "));

            Assert.True(result.Accepted);
            Assert.Equal("hello there world", result.Text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void RejectsEmpty(string text)
        {
            var filter = new MessageFilter(Build.Settings());
            Assert.Equal(FilterVerdict.Empty, filter.Evaluate(Build.Message(text)).Verdict);
        }

        [Theory]
        [InlineData("!drop")]
        [InlineData("/me waves")]
        [InlineData(".commands")]
        [InlineData("?help")]
        public void RejectsCommands(string text)
        {
            var filter = new MessageFilter(Build.Settings());
            Assert.Equal(FilterVerdict.Command, filter.Evaluate(Build.Message(text)).Verdict);
        }

        [Fact]
        public void RejectsIgnoredUsers()
        {
            var filter = new MessageFilter(Build.Settings());
            Assert.Equal(FilterVerdict.IgnoredUser,
                filter.Evaluate(Build.Message("timer at 5", login: "Nightbot")).Verdict);
        }

        [Theory]
        [InlineData("look at https://example.com/x")]
        [InlineData("www.twitch.tv/someone")]
        [InlineData("go to shady.xyz now")]
        public void RejectsLinks(string text)
        {
            var filter = new MessageFilter(Build.Settings());
            Assert.Equal(FilterVerdict.ContainsLink, filter.Evaluate(Build.Message(text)).Verdict);
        }

        [Fact]
        public void AllowsLinksWhenBlockLinksIsOff()
        {
            var settings = Build.Settings();
            settings.Filter.BlockLinks = false;

            var filter = new MessageFilter(settings);
            Assert.True(filter.Evaluate(Build.Message("see https://example.com")).Accepted);
        }

        [Fact]
        public void RejectsBlockedWordsCaseInsensitively()
        {
            var settings = Build.Settings();
            settings.Filter.BlockedWords.Add("bannedword");

            var filter = new MessageFilter(settings);
            Assert.Equal(FilterVerdict.BlockedWord,
                filter.Evaluate(Build.Message("this has BannedWord in it")).Verdict);
        }

        [Fact]
        public void RejectsTooShortAndTooLong()
        {
            var settings = Build.Settings();
            settings.Filter.MinMessageLength = 3;
            settings.Filter.MaxMessageLength = 10;

            var filter = new MessageFilter(settings);
            Assert.Equal(FilterVerdict.TooShort, filter.Evaluate(Build.Message("hi")).Verdict);
            Assert.Equal(FilterVerdict.TooLong, filter.Evaluate(Build.Message(new string('a', 11))).Verdict);
        }

        [Fact]
        public void RejectsDuplicatesFromTheSameUser()
        {
            var filter = new MessageFilter(Build.Settings());

            Assert.True(filter.Evaluate(Build.Message("same text")).Accepted);
            Assert.Equal(FilterVerdict.Duplicate, filter.Evaluate(Build.Message("Same Text")).Verdict);
        }

        [Fact]
        public void DifferentUsersMaySayTheSameThing()
        {
            var filter = new MessageFilter(Build.Settings());

            Assert.True(filter.Evaluate(Build.Message("gg", login: "alice")).Accepted);
            Assert.True(filter.Evaluate(Build.Message("gg", login: "bob")).Accepted);
        }

        [Fact]
        public void DuplicateHistoryIsBoundedSoOldTextBecomesSayableAgain()
        {
            var settings = Build.Settings();
            settings.Filter.DuplicateHistorySize = 2;

            var filter = new MessageFilter(settings);
            Assert.True(filter.Evaluate(Build.Message("first")).Accepted);
            Assert.True(filter.Evaluate(Build.Message("second")).Accepted);
            Assert.True(filter.Evaluate(Build.Message("third")).Accepted);

            Assert.True(filter.Evaluate(Build.Message("first")).Accepted);
        }

        [Fact]
        public void SubscribersOnlyBlocksNonSubscribers()
        {
            var settings = Build.Settings();
            settings.Filter.SubscribersOnly = true;

            var filter = new MessageFilter(settings);
            Assert.Equal(FilterVerdict.NotSubscriber, filter.Evaluate(Build.Message("hello")).Verdict);
            Assert.True(filter.Evaluate(Build.Message("hello sub", sub: true)).Accepted);
        }

        [Fact]
        public void ModeratorsOnlyBlocksEveryoneElse()
        {
            var settings = Build.Settings();
            settings.Filter.ModeratorsOnly = true;
            settings.Filter.ModeratorsBypassLimits = false;

            var filter = new MessageFilter(settings);
            Assert.Equal(FilterVerdict.NotModerator,
                filter.Evaluate(Build.Message("hello", sub: true)).Verdict);
            Assert.True(filter.Evaluate(Build.Message("hello mod", mod: true)).Accepted);
        }

        [Fact]
        public void ModeratorsBypassLengthLimits()
        {
            var settings = Build.Settings();
            settings.Filter.MaxMessageLength = 5;
            settings.Filter.SubscribersOnly = true;

            var filter = new MessageFilter(settings);
            Assert.True(filter.Evaluate(Build.Message(new string('a', 50), mod: true)).Accepted);
        }

        [Fact]
        public void BroadcasterCountsAsModerator()
        {
            var settings = Build.Settings();
            settings.Filter.ModeratorsOnly = true;

            var filter = new MessageFilter(settings);
            Assert.True(filter.Evaluate(Build.Message("streamer here", broadcaster: true)).Accepted);
        }

        [Fact]
        public void ResetForgetsDuplicateHistory()
        {
            var filter = new MessageFilter(Build.Settings());
            Assert.True(filter.Evaluate(Build.Message("repeat")).Accepted);

            filter.Reset();
            Assert.True(filter.Evaluate(Build.Message("repeat")).Accepted);
        }
    }
}
