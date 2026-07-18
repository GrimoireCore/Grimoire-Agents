namespace AgentLearning.Core.Workflow;

/// <summary>
/// Identifies the current state of one agent run.
/// State answers which phase is active and is easier for UIs to consume than raw logs.
/// </summary>
public enum AgentRunStatus
{
    /// <summary>The state machine was created but has not processed input.</summary>
    Initialized,

    /// <summary>User input has been received.</summary>
    ReceivedInput,

    /// <summary>The model context window has been built.</summary>
    BuiltContext,

    /// <summary>Tool routing has selected which tools to expose.</summary>
    RoutedTools,

    /// <summary>A request has been sent to the main model.</summary>
    AskedModel,

    /// <summary>The model requested a tool and the harness is processing it.</summary>
    WaitingForTool,

    /// <summary>The tool requires approval and is waiting for yes or no.</summary>
    WaitingForApproval,

    /// <summary>The tool completed successfully.</summary>
    ToolExecuted,

    /// <summary>The user rejected tool execution.</summary>
    ToolRejected,

    /// <summary>The tool failed, and its error became a model-visible observation.</summary>
    ToolFailed,

    /// <summary>The model answer failed validation and is being repaired.</summary>
    RepairingAnswer,

    /// <summary>The agent run produced its final answer.</summary>
    Finished,

    /// <summary>The run encountered an unrecoverable error.</summary>
    Failed
}
