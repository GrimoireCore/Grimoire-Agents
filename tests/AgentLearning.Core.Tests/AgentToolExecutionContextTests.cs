using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolExecutionContextTests
{
    [Fact]
    public void Constructor_builds_same_key_for_same_logical_tool_call()
    {
        AgentToolExecutionContext first = new("run_123", "call_456");
        AgentToolExecutionContext second = new("run_123", "call_456");

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(64, first.IdempotencyKey.Length);
    }

    [Fact]
    public void Constructor_builds_different_key_for_different_tool_call()
    {
        AgentToolExecutionContext first = new("run_123", "call_456");
        AgentToolExecutionContext second = new("run_123", "call_789");

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }
}
