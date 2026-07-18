namespace AgentLearning.Core.Diagnostics;

/// <summary>
/// Represents a Chat Completions message in the diagnostic view.
/// It avoids SDK types so the core logic remains easy to test.
/// </summary>
public sealed record AgentDebugMessage
{
    /// <summary>The message role, such as system, user, assistant, or tool.</summary>
    public required string Role { get; init; }

    /// <summary>Plain text content; tool-call messages may leave it empty.</summary>
    public string? Content { get; init; }

    /// <summary>Tools requested by the assistant.</summary>
    public IReadOnlyList<AgentDebugToolCall> ToolCalls { get; init; } = [];

    /// <summary>The tool_call_id associated with a tool message.</summary>
    public string? ToolCallId { get; init; }
}
