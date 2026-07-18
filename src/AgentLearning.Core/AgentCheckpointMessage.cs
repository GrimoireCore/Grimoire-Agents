using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Represents a chat-message snapshot stored in a checkpoint.
/// Resume logic converts it back into an SDK ChatMessage.
/// </summary>
public sealed record AgentCheckpointMessage(
    /// <summary>The message role, such as system, user, assistant, or tool.</summary>
    [property: JsonPropertyName("role")]
    string Role,

    /// <summary>Plain text content; assistant tool-call messages may leave it empty.</summary>
    [property: JsonPropertyName("content")]
    string? Content,

    /// <summary>The tool_call_id for a tool message; normally empty for other roles.</summary>
    [property: JsonPropertyName("tool_call_id")]
    string? ToolCallId,

    /// <summary>Tool calls carried by an assistant message.</summary>
    [property: JsonPropertyName("tool_calls")]
    IReadOnlyList<AgentCheckpointToolCall> ToolCalls)
{
    /// <summary>Creates a plain text message.</summary>
    public static AgentCheckpointMessage Text(string role, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(content);

        return new AgentCheckpointMessage(role.Trim(), content, ToolCallId: null, ToolCalls: []);
    }

    /// <summary>Creates an assistant tool-call message.</summary>
    public static AgentCheckpointMessage AssistantToolCalls(
        IReadOnlyList<AgentCheckpointToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);
        if (toolCalls.Count == 0)
        {
            throw new ArgumentException("Assistant tool call message requires at least one tool call.", nameof(toolCalls));
        }

        return new AgentCheckpointMessage("assistant", Content: null, ToolCallId: null, ToolCalls: toolCalls);
    }

    /// <summary>Creates a tool-observation message.</summary>
    public static AgentCheckpointMessage Tool(string toolCallId, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentNullException.ThrowIfNull(content);

        return new AgentCheckpointMessage("tool", content, toolCallId.Trim(), ToolCalls: []);
    }
}
