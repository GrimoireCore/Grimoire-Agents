namespace AgentLearning.Core.Workflow;

/// <summary>
/// Implements the mutable state machine for one agent run.
/// AgentRunner calls Mark methods at key points to make progress explicit.
/// </summary>
public sealed class AgentRunState
{
    public AgentRunStatus Status { get; private set; } = AgentRunStatus.Initialized;

    public int ModelRequestCount { get; private set; }

    public int ToolCallCount { get; private set; }

    public string? LastToolName { get; private set; }

    public bool WaitingForApproval { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Marks user input as received.</summary>
    public void MarkReceivedInput()
    {
        MoveTo(AgentRunStatus.ReceivedInput);
    }

    /// <summary>Marks the context window as built.</summary>
    public void MarkBuiltContext()
    {
        MoveTo(AgentRunStatus.BuiltContext);
    }

    /// <summary>Marks tool routing as completed or skipped.</summary>
    public void MarkRoutedTools()
    {
        MoveTo(AgentRunStatus.RoutedTools);
    }

    /// <summary>Records a request to the main model.</summary>
    public void MarkAskedModel()
    {
        ModelRequestCount++;
        MoveTo(AgentRunStatus.AskedModel);
    }

    /// <summary>Records a tool call requested by the model.</summary>
    public void MarkToolRequested(string toolName)
    {
        ToolCallCount++;
        LastToolName = NormalizeToolName(toolName);
        WaitingForApproval = false;
        MoveTo(AgentRunStatus.WaitingForTool);
    }

    /// <summary>Marks the run as waiting for user approval.</summary>
    public void MarkWaitingForApproval(string toolName)
    {
        LastToolName = NormalizeToolName(toolName);
        WaitingForApproval = true;
        MoveTo(AgentRunStatus.WaitingForApproval);
    }

    /// <summary>Marks the tool call as rejected by the user.</summary>
    public void MarkToolRejected(string toolName)
    {
        LastToolName = NormalizeToolName(toolName);
        WaitingForApproval = false;
        MoveTo(AgentRunStatus.ToolRejected);
    }

    /// <summary>Marks the tool as completed.</summary>
    public void MarkToolExecuted(string toolName)
    {
        LastToolName = NormalizeToolName(toolName);
        WaitingForApproval = false;
        LastError = null;
        MoveTo(AgentRunStatus.ToolExecuted);
    }

    /// <summary>Marks the tool as failed and records the latest error.</summary>
    public void MarkToolFailed(string toolName, string error)
    {
        LastToolName = NormalizeToolName(toolName);
        WaitingForApproval = false;
        LastError = string.IsNullOrWhiteSpace(error)
            ? "Unknown tool error."
            : error.Trim();
        MoveTo(AgentRunStatus.ToolFailed);
    }

    /// <summary>Marks that the latest model answer was rejected and needs one repair attempt.</summary>
    public void MarkRepairingAnswer()
    {
        MoveTo(AgentRunStatus.RepairingAnswer);
    }

    /// <summary>Marks the run as completed.</summary>
    public void MarkFinished()
    {
        WaitingForApproval = false;
        MoveTo(AgentRunStatus.Finished);
    }

    /// <summary>Marks the run as failed by an unrecoverable error.</summary>
    public void MarkFailed(string error)
    {
        WaitingForApproval = false;
        LastError = string.IsNullOrWhiteSpace(error)
            ? "Unknown agent error."
            : error.Trim();
        MoveTo(AgentRunStatus.Failed);
    }

    /// <summary>Creates a read-only snapshot for callers.</summary>
    public AgentRunSnapshot ToSnapshot()
    {
        return new AgentRunSnapshot(
            Status,
            ModelRequestCount,
            ToolCallCount,
            LastToolName,
            WaitingForApproval,
            LastError);
    }

    private void MoveTo(AgentRunStatus status)
    {
        Status = status;
    }

    private static string NormalizeToolName(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return toolName.Trim();
    }
}
