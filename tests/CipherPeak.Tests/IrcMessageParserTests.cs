using System;
using CipherPeak.Core.Twitch;
using Xunit;

namespace CipherPeak.Tests
{
    public class IrcMessageParserTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

        [Fact]
        public void ParsesAPlainPrivmsg()
        {
            const string line = ":bob!bob@bob.tmi.twitch.tv PRIVMSG #channel :hello world";

            Assert.True(IrcMessageParser.TryParsePrivmsg(line, Now, out var message));
            Assert.Equal("bob", message.Login);
            Assert.Equal("hello world", message.Text);
        }

        [Fact]
        public void ParsesTagsIncludingRoles()
        {
            const string line =
                "@badge-info=;badges=broadcaster/1,subscriber/12;display-name=CoolBob;mod=0;subscriber=1 " +
                ":bob!bob@bob.tmi.twitch.tv PRIVMSG #channel :hi chat";

            Assert.True(IrcMessageParser.TryParsePrivmsg(line, Now, out var message));
            Assert.Equal("CoolBob", message.DisplayName);
            Assert.True(message.IsBroadcaster);
            Assert.True(message.IsModerator);      // broadcaster implies moderator
            Assert.True(message.IsSubscriber);
        }

        [Fact]
        public void ParsesModeratorTag()
        {
            const string line = "@mod=1;display-name=Mod :m!m@m.tmi.twitch.tv PRIVMSG #c :hello";

            Assert.True(IrcMessageParser.TryParsePrivmsg(line, Now, out var message));
            Assert.True(message.IsModerator);
            Assert.False(message.IsBroadcaster);
        }

        [Fact]
        public void UnescapesTagValues()
        {
            const string line = @"@display-name=Coolers\sBob :bob!bob@bob.tmi.twitch.tv PRIVMSG #c :hi";

            Assert.True(IrcMessageParser.TryParsePrivmsg(line, Now, out var message));
            Assert.Equal("Coolers Bob", message.DisplayName);
        }

        [Fact]
        public void KeepsColonsInsideTheMessageBody()
        {
            const string line = ":bob!bob@bob.tmi.twitch.tv PRIVMSG #channel :time is 12:30 : ok";

            Assert.True(IrcMessageParser.TryParsePrivmsg(line, Now, out var message));
            Assert.Equal("time is 12:30 : ok", message.Text);
        }

        [Fact]
        public void UnwrapsCtcpActionMessages()
        {
            const string line = ":bob!bob@bob.tmi.twitch.tv PRIVMSG #channel :\u0001ACTION waves\u0001";

            Assert.True(IrcMessageParser.TryParsePrivmsg(line, Now, out var message));
            Assert.Equal("waves", message.Text);
        }

        [Theory]
        [InlineData("PING :tmi.twitch.tv")]
        [InlineData(":tmi.twitch.tv 001 justinfan1 :Welcome")]
        [InlineData("@msg-id=x :tmi.twitch.tv NOTICE #c :something")]
        [InlineData("")]
        [InlineData(null)]
        public void IgnoresNonPrivmsgLines(string line)
        {
            Assert.False(IrcMessageParser.TryParsePrivmsg(line, Now, out _));
        }

        [Fact]
        public void RespondsToPingWithMatchingPayload()
        {
            Assert.True(IrcMessageParser.IsPing("PING :tmi.twitch.tv"));
            Assert.Equal("PONG :tmi.twitch.tv", IrcMessageParser.PongFor("PING :tmi.twitch.tv"));
        }

        [Fact]
        public void DetectsAuthenticationFailure()
        {
            Assert.True(IrcMessageParser.IsAuthFailure(
                ":tmi.twitch.tv NOTICE * :Login authentication failed"));
            Assert.False(IrcMessageParser.IsAuthFailure(
                ":tmi.twitch.tv NOTICE * :You are now chatting"));
        }
    }
}
