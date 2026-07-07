namespace AgentLearning.Core.Workflow;

/// <summary>
/// Agent 工作流中的一步。
/// Number 用来显示顺序，Kind 用来表达这一步属于哪类动作。
/// </summary>
public sealed record AgentWorkflowStep(
    int Number,
    AgentWorkflowStepKind Kind,
    string Title,
    string Detail);
