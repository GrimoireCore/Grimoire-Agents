namespace AgentLearning.Core;

/// <summary>
/// Represents the result of resuming an approved or rejected checkpoint.
/// This type describes tool execution; the runner continues the model conversation separately.
/// </summary>
public sealed record AgentCheckpointResumeResult(
    /// <summary>The resumed agent run ID.</summary>
    string RunId,

    /// <summary>The resumed tool_call_id.</summary>
    string ToolCallId,

    /// <summary>The resumed tool name.</summary>
    string ToolName,

    /// <summary>Whether the user approved the tool.</summary>
    bool Approved,

    /// <summary>Whether the tool actually executed.</summary>
    bool ToolExecuted,

    /// <summary>The tool result, or the rejection observation returned to the model.</summary>
    string Observation);
