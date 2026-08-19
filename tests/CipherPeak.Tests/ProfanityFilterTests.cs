using CipherPeak.Core.Filtering;
using Xunit;

namespace CipherPeak.Tests
{
    public class ProfanityFilterTests
    {
        [Fact]
        public void OffLeavesTheMessageAlone()
        {
            var settings = Build.Settings();   // Profanity defaults to Off
            var result = new MessageFilter(settings).Evaluate(Build.Message("what the fuck"));

            Assert.True(result.Accepted);
            Assert.Equal("what the fuck", result.Text);
        }

        [Fact]
        public void MaskReplacesTheSwearAndKeepsTheRest()
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;

            var result = new MessageFilter(settings).Evaluate(Build.Message("what the FUCK is that"));

            Assert.True(result.Accepted);
            Assert.Equal("what the beep is that", result.Text);
        }

        [Fact]
        public void MaskCatchesSuffixedForms()
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;

            var result = new MessageFilter(settings).Evaluate(Build.Message("that bitches and shitting"));

            Assert.True(result.Accepted);
            Assert.Equal("that beep and beep", result.Text);
        }

        [Theory]
        [InlineData("oh damn that hurt", "oh beep that hurt")]
        [InlineData("what a crappy climb", "what a beep climb")]
        public void MildSwearsAreInTheBuiltInList(string text, string expected)
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;

            Assert.Equal(expected, new MessageFilter(settings).Evaluate(Build.Message(text)).Text);
        }

        [Fact]
        public void BlockDropsTheWholeMessage()
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Block;

            var result = new MessageFilter(settings).Evaluate(Build.Message("oh shit"));

            Assert.Equal(FilterVerdict.Profanity, result.Verdict);
        }

        // The whole reason for word boundaries: a filter that eats ordinary words is worse than none.
        [Theory]
        [InlineData("look at the grass")]
        [InlineData("assess the class")]
        [InlineData("Scunthorpe is a place")]
        [InlineData("cockatoo on a shitake")]
        public void DoesNotTouchInnocentWords(string text)
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;

            var result = new MessageFilter(settings).Evaluate(Build.Message(text));

            Assert.True(result.Accepted);
            Assert.Equal(text, result.Text);
        }

        [Fact]
        public void ExtraWordsExtendTheBuiltInList()
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;
            settings.Filter.ProfanityWords.Add("kappa");

            var result = new MessageFilter(settings).Evaluate(Build.Message("kappa moment"));

            Assert.Equal("beep moment", result.Text);
        }

        [Fact]
        public void EmptyMaskRemovesTheWord()
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;
            settings.Filter.ProfanityMask = "";

            var result = new MessageFilter(settings).Evaluate(Build.Message("oh shit really"));

            Assert.Equal("oh really", result.Text);
        }

        [Fact]
        public void AMessageThatIsNothingButProfanityIsDroppedWhenMasked()
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;
            settings.Filter.ProfanityMask = "";

            var result = new MessageFilter(settings).Evaluate(Build.Message("shit"));

            Assert.Equal(FilterVerdict.Empty, result.Verdict);
        }

        [Fact]
        public void ChangingTheExtraWordsRebuildsTheMatcher()
        {
            var settings = Build.Settings();
            settings.Filter.Profanity = ProfanityAction.Mask;
            var filter = new MessageFilter(settings);

            Assert.Equal("kappa moment", filter.Evaluate(Build.Message("kappa moment")).Text);

            settings.Filter.ProfanityWords.Add("kappa");

            Assert.Equal("beep moment", filter.Evaluate(Build.Message("kappa moment", login: "other")).Text);
        }
    }
}
