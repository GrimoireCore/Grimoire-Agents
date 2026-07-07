namespace AgentLearning.Core.Workflow;

/// <summary>
/// 一次用户请求对应的一条 Agent 工作流轨迹。
/// 它帮助我们学习 Agent 实际经历了哪些可观察步骤。
/// </summary>
public sealed class AgentWorkflowTrace
{
    private readonly List<AgentWorkflowStep> _steps = [];

    public IReadOnlyList<AgentWorkflowStep> Steps => _steps;

    public AgentWorkflowStep Add(AgentWorkflowStepKind kind, string title, string detail)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Workflow step title cannot be empty.", nameof(title));
        }

        AgentWorkflowStep step = new(
            Number: _steps.Count + 1,
            Kind: kind,
            Title: title.Trim(),
            Detail: detail.Trim());

        _steps.Add(step);
        return step;
    }
}
