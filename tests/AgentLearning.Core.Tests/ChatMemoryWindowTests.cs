using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class ChatMemoryWindowTests
{
    [Fact]
    public void GetRecentTurns_returns_all_turns_when_memory_is_within_limit()
    {
        ChatMemory memory = new();
        memory.AddUserMessage("u1");
        memory.AddAssistantMessage("a1");

        IReadOnlyList<ChatTurn> turns = ChatMemoryWindow.GetRecentTurns(memory, maxTurns: 4);

        Assert.Collection(
            turns,
            first => Assert.Equal("u1", first.Content),
            second => Assert.Equal("a1", second.Content));
    }

    [Fact]
    public void GetRecentTurns_returns_recent_turns_and_preserves_order()
    {
        ChatMemory memory = new();
        memory.AddUserMessage("u1");
        memory.AddAssistantMessage("a1");
        memory.AddUserMessage("u2");
        memory.AddAssistantMessage("a2");
        memory.AddUserMessage("u3");
        memory.AddAssistantMessage("a3");

        IReadOnlyList<ChatTurn> turns = ChatMemoryWindow.GetRecentTurns(memory, maxTurns: 4);

        Assert.Collection(
            turns,
            first => Assert.Equal("u2", first.Content),
            second => Assert.Equal("a2", second.Content),
            third => Assert.Equal("u3", third.Content),
            fourth => Assert.Equal("a3", fourth.Content));
    }

    [Fact]
    public void GetRecentTurns_drops_orphaned_assistant_at_start()
    {
        ChatMemory memory = new();
        memory.AddUserMessage("u1");
        memory.AddAssistantMessage("a1");
        memory.AddUserMessage("u2");
        memory.AddAssistantMessage("a2");
        memory.AddUserMessage("u3");
        memory.AddAssistantMessage("a3");
        memory.AddUserMessage("u4");

        IReadOnlyList<ChatTurn> turns = ChatMemoryWindow.GetRecentTurns(memory, maxTurns: 6);

        Assert.Collection(
            turns,
            first =>
            {
                Assert.Equal(ChatRole.User, first.Role);
                Assert.Equal("u2", first.Content);
            },
            second => Assert.Equal("a2", second.Content),
            third => Assert.Equal("u3", third.Content),
            fourth => Assert.Equal("a3", fourth.Content),
            fifth => Assert.Equal("u4", fifth.Content));
    }

    [Fact]
    public void GetRecentTurns_rejects_non_positive_limit()
    {
        ChatMemory memory = new();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ChatMemoryWindow.GetRecentTurns(memory, maxTurns: 0));

        Assert.Equal("maxTurns", exception.ParamName);
    }
}
