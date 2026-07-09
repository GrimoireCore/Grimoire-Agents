using AgentLearning.Core.Diagnostics;

namespace AgentLearning.Core.Tests;

public sealed class AgentCheckpointMessageBuilderTests
{
    [Fact]
    public void FromDebugMessages_captures_text_tool_calls_and_tool_observations()
    {
        AgentDebugMessage[] debugMessages =
        [
            new()
            {
                Role = "system",
                Content = "You are a teacher."
            },
            new()
            {
                Role = "user",
                Content = "save this note"
            },
            new()
            {
                Role = "assistant",
                ToolCalls =
                [
                    new AgentDebugToolCall(
                        Id: "call_123",
                        Name: "write_note",
                        ArgumentsJson: """{"note":"hello"}""")
                ]
            },
            new()
            {
                Role = "tool",
                ToolCallId = "call_123",
                Content = "Note saved."
            }
        ];

        IReadOnlyList<AgentCheckpointMessage> messages = AgentCheckpointMessageBuilder.FromDebugMessages(debugMessages);

        Assert.Equal(["system", "user", "assistant", "tool"], messages.Select(message => message.Role));
        Assert.Equal("You are a teacher.", messages[0].Content);
        Assert.Equal("save this note", messages[1].Content);
        Assert.Null(messages[2].Content);
        Assert.Equal("call_123", messages[2].ToolCalls[0].Id);
        Assert.Equal("write_note", messages[2].ToolCalls[0].Name);
        Assert.Equal("""{"note":"hello"}""", messages[2].ToolCalls[0].ArgumentsJson);
        Assert.Equal("call_123", messages[3].ToolCallId);
        Assert.Equal("Note saved.", messages[3].Content);
    }
}
