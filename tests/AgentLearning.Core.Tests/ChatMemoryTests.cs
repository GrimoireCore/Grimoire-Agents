using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class ChatMemoryTests
{
    [Fact]
    public void AddUserMessage_and_AddAssistantMessage_preserve_conversation_order()
    {
        ChatMemory memory = new();

        memory.AddUserMessage("What is an Agent?");
        memory.AddAssistantMessage("An Agent combines instructions, memory, tools, and a model.");

        IReadOnlyList<ChatTurn> turns = memory.Turns;

        Assert.Collection(
            turns,
            first =>
            {
                Assert.Equal(ChatRole.User, first.Role);
                Assert.Equal("What is an Agent?", first.Content);
            },
            second =>
            {
                Assert.Equal(ChatRole.Assistant, second.Role);
                Assert.Equal("An Agent combines instructions, memory, tools, and a model.", second.Content);
            });
    }
}
