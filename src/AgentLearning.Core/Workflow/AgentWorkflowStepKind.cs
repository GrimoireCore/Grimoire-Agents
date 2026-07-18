namespace AgentLearning.Core.Workflow;

/// <summary>
/// Identifies observable step types in an agent workflow.
/// These steps record external behavior, never hidden model reasoning.
/// </summary>
public enum AgentWorkflowStepKind
{
    ReceiveInput,
    BuildContext,
    RouteTools,
    AskModel,
    ToolRequested,
    ToolApprovalRequested,
    ToolRejected,
    ToolFailed,
    ToolExecuted,
    AnswerRejected,
    Finish
}
