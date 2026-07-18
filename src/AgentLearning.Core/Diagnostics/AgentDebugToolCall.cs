namespace AgentLearning.Core.Diagnostics;

/// <summary>
/// Represents a tool call in the diagnostic view.
/// It exists for human-readable output and does not execute the tool.
/// </summary>
public sealed record AgentDebugToolCall(
    string Id,
    string Name,
    string ArgumentsJson);
