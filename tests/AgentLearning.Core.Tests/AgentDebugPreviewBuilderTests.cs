using AgentLearning.Core.Diagnostics;
using AgentLearning.Core.Skills;
using System.Text.Json;

namespace AgentLearning.Core.Tests;

public sealed class AgentDebugPreviewBuilderTests
{
    [Fact]
    public void BuildChatCompletionsRequestPreview_includes_messages_and_tools()
    {
        AgentDebugMessage[] messages =
        [
            new()
            {
                Role = "system",
                Content = "You are Grimoire Router."
            },
            new()
            {
                Role = "user",
                Content = "帮我算一下 (2 + 3) * 4"
            }
        ];

        string preview = AgentDebugPreviewBuilder.BuildChatCompletionsRequestPreview(
            model: "gpt-5.4",
            stream: false,
            messages: messages,
            skills: [new CalculatorSkill()],
            includeTools: true);

        using JsonDocument document = JsonDocument.Parse(preview);
        JsonElement root = document.RootElement;

        Assert.Equal("gpt-5.4", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal("function", root.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.Equal("calculate", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("object", root.GetProperty("tools")[0].GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());
        Assert.Contains("帮我算一下 (2 + 3) * 4", preview);
    }

    [Fact]
    public void BuildChatCompletionsRequestPreview_includes_assistant_tool_call_and_tool_result()
    {
        AgentDebugMessage[] messages =
        [
            new()
            {
                Role = "assistant",
                ToolCalls =
                [
                    new AgentDebugToolCall(
                        Id: "call_123",
                        Name: "calculate",
                        ArgumentsJson: """{"expression":"(2 + 3) * 4"}""")
                ]
            },
            new()
            {
                Role = "tool",
                ToolCallId = "call_123",
                Content = "20"
            }
        ];

        string preview = AgentDebugPreviewBuilder.BuildChatCompletionsRequestPreview(
            model: "gpt-5.4",
            stream: false,
            messages: messages,
            skills: [],
            includeTools: false);

        using JsonDocument document = JsonDocument.Parse(preview);
        JsonElement root = document.RootElement;

        JsonElement assistantMessage = root.GetProperty("messages")[0];
        Assert.Equal("assistant", assistantMessage.GetProperty("role").GetString());
        Assert.Equal("call_123", assistantMessage.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("calculate", assistantMessage.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("""{"expression":"(2 + 3) * 4"}""", assistantMessage.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString());

        JsonElement toolMessage = root.GetProperty("messages")[1];
        Assert.Equal("tool", toolMessage.GetProperty("role").GetString());
        Assert.Equal("call_123", toolMessage.GetProperty("tool_call_id").GetString());
        Assert.Equal("20", toolMessage.GetProperty("content").GetString());
    }

    [Fact]
    public void RedactSensitiveValues_masks_api_keys_and_authorization_tokens()
    {
        string text = """
            {
              "api_key": "secret-api-key",
              "Authorization": "Bearer secret-bearer-token",
              "content": "normal text"
            }
            """;

        string redacted = AgentDebugPreviewBuilder.RedactSensitiveValues(text);

        Assert.DoesNotContain("secret-api-key", redacted);
        Assert.DoesNotContain("secret-bearer-token", redacted);
        Assert.Contains("\"api_key\": \"[redacted]\"", redacted);
        Assert.Contains("\"Authorization\": \"Bearer [redacted]\"", redacted);
        Assert.Contains("normal text", redacted);
    }
}
