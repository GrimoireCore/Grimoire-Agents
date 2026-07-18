namespace AgentLearning.Core.Workflow;

/// <summary>
/// Formats a workflow step as a readable console line.
/// </summary>
public static class AgentWorkflowStepFormatter
{
    public static string Format(AgentWorkflowStep step)
    {
        return $"[Workflow {step.Number}] {step.Kind} - {step.Title}: {step.Detail}";
    }
}
