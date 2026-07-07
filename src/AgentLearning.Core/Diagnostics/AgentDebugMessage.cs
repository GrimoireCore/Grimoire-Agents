namespace AgentLearning.Core.Diagnostics;

/// <summary>
/// 调试视图里的 Chat Completions message。
/// 这里不用 OpenAI SDK 类型，是为了让核心逻辑更容易测试。
/// </summary>
public sealed record AgentDebugMessage
{
    /// <summary>消息角色，例如 system、user、assistant、tool。</summary>
    public required string Role { get; init; }

    /// <summary>普通文本内容；工具调用消息可以为空。</summary>
    public string? Content { get; init; }

    /// <summary>assistant 要求调用的工具列表。</summary>
    public IReadOnlyList<AgentDebugToolCall> ToolCalls { get; init; } = [];

    /// <summary>tool 消息对应的 tool_call_id。</summary>
    public string? ToolCallId { get; init; }
}
