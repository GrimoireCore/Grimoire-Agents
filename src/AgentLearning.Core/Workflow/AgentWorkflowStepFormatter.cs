namespace AgentLearning.Core.Workflow;

/// <summary>
/// 把工作流步骤格式化成控制台里容易读的一行文字。
/// </summary>
public static class AgentWorkflowStepFormatter
{
    public static string Format(AgentWorkflowStep step)
    {
        return $"[Workflow {step.Number}] {step.Kind} - {step.Title}: {step.Detail}";
    }
}
