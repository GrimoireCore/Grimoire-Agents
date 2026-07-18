namespace AgentLearning.Core.Workflow;

/// <summary>
/// Represents the workflow trace for one user request.
/// It reveals the observable steps the agent actually performed.
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
