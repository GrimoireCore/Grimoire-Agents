namespace AgentLearning.Core;

/// <summary>
/// Checkpoint kind.
/// </summary>
public enum AgentCheckpointKind
{
    /// <summary>The agent is paused while waiting for the user to approve a tool call.</summary>
    PendingToolApproval,

    /// <summary>The tool has already been resolved, but the final model answer is not completed yet.</summary>
    ToolResolved
}
