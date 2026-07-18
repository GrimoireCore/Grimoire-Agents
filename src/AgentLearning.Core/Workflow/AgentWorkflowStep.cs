namespace AgentLearning.Core.Workflow;

/// <summary>
/// Represents one step in an agent workflow.
/// Number preserves order, and Kind identifies the action category.
/// </summary>
public sealed record AgentWorkflowStep(
    int Number,
    AgentWorkflowStepKind Kind,
    string Title,
    string Detail);
