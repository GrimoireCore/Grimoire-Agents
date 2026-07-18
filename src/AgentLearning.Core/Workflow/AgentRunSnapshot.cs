namespace AgentLearning.Core.Workflow;

/// <summary>
/// Provides a read-only snapshot of agent run state.
/// A snapshot prevents UI code or callers from mutating the internal state machine.
/// </summary>
public sealed record AgentRunSnapshot(
    /// <summary>The current run status.</summary>
    AgentRunStatus Status,

    /// <summary>The number of main-model requests in this run.</summary>
    int ModelRequestCount,

    /// <summary>The number of tool-call requests received in this run.</summary>
    int ToolCallCount,

    /// <summary>The most recently involved tool name.</summary>
    string? LastToolName,

    /// <summary>Whether the run is waiting for user approval.</summary>
    bool WaitingForApproval,

    /// <summary>The most recent error message.</summary>
    string? LastError);
