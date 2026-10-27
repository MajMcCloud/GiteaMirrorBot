using GiteaMirrorBot.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace GiteaMirrorBot.Tests;

/// <summary>
/// TA-13 – the bot may only react inside the configured group.
/// </summary>
public class ChatGuardTests
{
    private const long Allowed = -1001234567890;

    [Theory]
    // the configured group
    [InlineData(Allowed, Allowed, ChatGuard.Reason.Allowed)]
    // a different group / channel
    [InlineData(-1009999999999, Allowed, ChatGuard.Reason.OtherGroup)]
    [InlineData(-1234567890, Allowed, ChatGuard.Reason.OtherGroup)]
    // classic (non-supergroup) group
    [InlineData(-42, Allowed, ChatGuard.Reason.OtherGroup)]
    // private user chat
    [InlineData(123456789, Allowed, ChatGuard.Reason.PrivateChat)]
    // GroupId not configured -> nothing is processed
    [InlineData(Allowed, 0, ChatGuard.Reason.NotConfigured)]
    // chat id cannot be determined
    [InlineData(0, Allowed, ChatGuard.Reason.Unknown)]
    public void Decide_Classifies_Chats_Correctly(long chatId, long allowedGroupId, ChatGuard.Reason expected)
    {
        Assert.Equal(expected, ChatGuard.Decide(chatId, allowedGroupId));
        Assert.Equal(expected == ChatGuard.Reason.Allowed, ChatGuard.IsAllowed(chatId, allowedGroupId));
    }

    [Fact]
    public void Decide_Reads_ChatId_From_The_Message()
    {
        var message = new Message
        {
            Chat = new Chat { Id = -1009999999999, Type = ChatType.Supergroup, Title = "Foreign group" }
        };

        Assert.Equal(ChatGuard.Reason.OtherGroup, ChatGuard.Decide(message, Allowed));

        var own = new Message
        {
            Chat = new Chat { Id = Allowed, Type = ChatType.Supergroup, Title = "Target group" }
        };

        Assert.Equal(ChatGuard.Reason.Allowed, ChatGuard.Decide(own, Allowed));
    }

    [Fact]
    public void Decide_Falls_Back_To_ChatId_0()
    {
        Assert.Equal(ChatGuard.Reason.Unknown, ChatGuard.Decide((Message?)null, Allowed));
    }

    [Fact]
    public void IsCallbackAllowed_Only_In_Target_Group_And_Right_Chat()
    {
        // The request was asked in the target group -> a callback from the target group is valid.
        Assert.True(ChatGuard.IsCallbackAllowed(Allowed, Allowed, Allowed));

        // Question message forwarded into a foreign group -> callback rejected.
        Assert.False(ChatGuard.IsCallbackAllowed(-1009999999999, Allowed, Allowed));

        // Callback from the target group, but the request belongs to a different chat.
        Assert.False(ChatGuard.IsCallbackAllowed(Allowed, Allowed, -1009999999999));

        // Request no longer pending (e.g. after a restart): only the group decides.
        Assert.True(ChatGuard.IsCallbackAllowed(Allowed, Allowed, null));
        Assert.False(ChatGuard.IsCallbackAllowed(-1009999999999, Allowed, null));

        // GroupId missing -> do not execute anything.
        Assert.False(ChatGuard.IsCallbackAllowed(Allowed, 0, Allowed));
    }

    [Fact]
    public void Describe_Returns_A_Hint_For_Every_Reason()
    {
        Assert.Contains("not configured", ChatGuard.Describe(ChatGuard.Reason.NotConfigured));
        Assert.Contains("a different group", ChatGuard.Describe(ChatGuard.Reason.OtherGroup));
        Assert.Contains("private chat", ChatGuard.Describe(ChatGuard.Reason.PrivateChat));
        Assert.Contains("could not be determined", ChatGuard.Describe(ChatGuard.Reason.Unknown));
        Assert.Equal("the configured group", ChatGuard.Describe(ChatGuard.Reason.Allowed));
    }
}
