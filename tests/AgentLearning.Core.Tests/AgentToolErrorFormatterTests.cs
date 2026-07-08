using AgentLearning.Core;
using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolErrorFormatterTests
{
    [Fact]
    public void IsRecoverable_returns_true_for_tool_runtime_errors()
    {
        Assert.True(AgentToolErrorFormatter.IsRecoverable(
            new InvalidOperationException("Division by zero is not allowed.")));
        Assert.True(AgentToolErrorFormatter.IsRecoverable(
            new TimeoutException("Tool timed out.")));
    }

    [Fact]
    public void IsRecoverable_returns_false_for_unknown_tools()
    {
        Assert.False(AgentToolErrorFormatter.IsRecoverable(
            new AgentUnknownSkillException("missing_skill")));
    }

    [Fact]
    public void IsRecoverable_returns_false_for_unexpected_system_errors()
    {
        Assert.False(AgentToolErrorFormatter.IsRecoverable(
            new NullReferenceException("Unexpected bug.")));
    }

    [Fact]
    public void FormatRecoverableError_returns_model_visible_tool_observation()
    {
        string result = AgentToolErrorFormatter.FormatRecoverableError(
            "calculate",
            new InvalidOperationException("Division by zero is not allowed."));

        Assert.Contains("工具执行失败", result);
        Assert.Contains("工具名称：calculate", result);
        Assert.Contains("错误类型：InvalidOperationException", result);
        Assert.Contains("Division by zero is not allowed.", result);
        Assert.Contains("可以根据这个错误", result);
    }
}
