using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Checkpoint 里保存的聊天消息快照。
/// 真实恢复时会把它重新转换成 SDK 的 ChatMessage。
/// </summary>
public sealed record AgentCheckpointMessage(
    /// <summary>消息角色，例如 system、user、assistant、tool。</summary>
    [property: JsonPropertyName("role")]
    string Role,

    /// <summary>普通文本内容；assistant 工具调用消息可以为空。</summary>
    [property: JsonPropertyName("content")]
    string? Content,

    /// <summary>tool 消息对应的 tool_call_id，非 tool 消息通常为空。</summary>
    [property: JsonPropertyName("tool_call_id")]
    string? ToolCallId,

    /// <summary>assistant 消息里携带的工具调用列表。</summary>
    [property: JsonPropertyName("tool_calls")]
    IReadOnlyList<AgentCheckpointToolCall> ToolCalls)
{
    /// <summary>创建普通文本消息。</summary>
    public static AgentCheckpointMessage Text(string role, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(content);

        return new AgentCheckpointMessage(role.Trim(), content, ToolCallId: null, ToolCalls: []);
    }

    /// <summary>创建 assistant 工具调用消息。</summary>
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

    /// <summary>创建工具观察消息。</summary>
    public static AgentCheckpointMessage Tool(string toolCallId, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentNullException.ThrowIfNull(content);

        return new AgentCheckpointMessage("tool", content, toolCallId.Trim(), ToolCalls: []);
    }
}
