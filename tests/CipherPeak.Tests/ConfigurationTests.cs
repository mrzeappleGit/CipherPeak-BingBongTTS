using CipherPeak.Core.Config;
using CipherPeak.Core.Tts;
using Xunit;

namespace CipherPeak.Tests
{
    public class VoiceAliasTests
    {
        [Fact]
        public void ParsesAliasProviderAndVoiceId()
        {
            var alias = VoiceAlias.Parse("Narrator=ElevenLabs:21m00Tcm4TlvDq8ikWAM");

            Assert.NotNull(alias);
            Assert.Equal("narrator", alias.Alias);
            Assert.Equal("elevenlabs", alias.Provider);
            Assert.Equal("21m00Tcm4TlvDq8ikWAM", alias.VoiceId);   // voice id case is preserved
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("# a comment")]
        [InlineData("noequals")]
        [InlineData("alias=")]
        [InlineData("alias=nocolon")]
        [InlineData("=elevenlabs:voice")]
        [InlineData("alias=elevenlabs:")]
        public void RejectsMalformedLines(string line)
        {
            Assert.Null(VoiceAlias.Parse(line));
        }

        [Fact]
        public void LaterDefinitionsWinSoAConfigEditIsNotAmbiguous()
        {
            var aliases = VoiceAlias.ParseCsv("a=elevenlabs:one, a=elevenlabs:two");

            Assert.Single(aliases);
            Assert.Equal("two", aliases[0].VoiceId);
        }

        [Fact]
        public void ParsesACommaSeparatedList()
        {
            var aliases = VoiceAlias.ParseCsv("narrator=elevenlabs:v1, robot=tiktok:en_us_001, ,broken");

            Assert.Equal(2, aliases.Count);
            Assert.Equal("narrator", aliases[0].Alias);
            Assert.Equal("robot", aliases[1].Alias);
        }
    }

    public class VoiceRegistryTests
    {
        [Fact]
        public void ResolvesAllowlistedAliasesOnly()
        {
            var settings = Build.Settings();
            var registry = new VoiceRegistry(() => settings);

            Assert.True(registry.TryResolve("default", out var voice));
            Assert.Equal("voice-a", voice.VoiceId);
            Assert.False(registry.TryResolve("secret-voice", out _));
        }

        [Fact]
        public void UserVoiceIsIgnoredWhileTheVoiceCommandIsDisabled()
        {
            var settings = Build.Settings();
            settings.Tts.AllowVoiceCommand = false;
            settings.Tts.VoiceAliases.Add(new VoiceAlias("robot", "elevenlabs", "voice-b"));

            var registry = new VoiceRegistry(() => settings);
            Assert.True(registry.TrySetUserVoice("bob", "robot"));

            Assert.Equal("default", registry.AliasFor("bob"));
        }

        [Fact]
        public void UserVoiceAppliesOnceTheVoiceCommandIsEnabled()
        {
            var settings = Build.Settings();
            settings.Tts.AllowVoiceCommand = true;
            settings.Tts.VoiceAliases.Add(new VoiceAlias("robot", "elevenlabs", "voice-b"));

            var registry = new VoiceRegistry(() => settings);
            Assert.True(registry.TrySetUserVoice("bob", "robot"));

            Assert.Equal("robot", registry.AliasFor("bob"));
            Assert.Equal("default", registry.AliasFor("amy"));
        }

        [Fact]
        public void UnknownAliasesAreRefused()
        {
            var settings = Build.Settings();
            settings.Tts.AllowVoiceCommand = true;

            var registry = new VoiceRegistry(() => settings);

            Assert.False(registry.TrySetUserVoice("bob", "21m00Tcm4TlvDq8ikWAM"));
            Assert.Equal("default", registry.AliasFor("bob"));
        }

        [Fact]
        public void FallsBackToTheFirstAliasWhenTheDefaultIsMisconfigured()
        {
            var settings = Build.Settings();
            settings.Tts.DefaultVoiceAlias = "typo";

            var registry = new VoiceRegistry(() => settings);
            Assert.Equal("default", registry.AliasFor("bob"));
        }

        [Fact]
        public void MisconfiguredDefaultAliasFallsBackWithinTheChosenProvider()
        {
            var settings = Build.Settings();
            settings.Tts.DefaultVoiceAlias = "typo";
            settings.Tts.DefaultProvider = "tiktok";
            settings.Tts.VoiceAliases.Add(new VoiceAlias("robot", "tiktok", "en_us_001"));

            var registry = new VoiceRegistry(() => settings);
            Assert.Equal("robot", registry.AliasFor("bob"));
        }

        [Fact]
        public void AllowedAliasesAreListedForOperators()
        {
            var settings = Build.Settings();
            settings.Tts.VoiceAliases.Add(new VoiceAlias("robot", "elevenlabs", "voice-b"));

            var registry = new VoiceRegistry(() => settings);
            Assert.Equal(new[] { "default", "robot" }, registry.AllowedAliases());
        }
    }

    public class DefaultSettingsTests
    {
        [Fact]
        public void DefaultsAreSafeForAFreshInstall()
        {
            var settings = new ModSettings();

            Assert.True(settings.Filter.BlockLinks);
            Assert.Equal("", settings.Twitch.Channel);
            Assert.Equal("", settings.Tts.ElevenLabsApiKey);
            Assert.Equal("", settings.Tts.TikTokEndpoint);
            Assert.False(settings.Tts.AllowVoiceCommand);
            Assert.True(settings.Queue.PerUserCooldownSeconds > 0);
            Assert.Equal(2, CipherPeak.Core.BingBong.BingBongDirector.TargetCount);
        }
    }
}
