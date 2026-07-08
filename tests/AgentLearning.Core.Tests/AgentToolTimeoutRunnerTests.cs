using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolTimeoutRunnerTests
{
    [Fact]
    public async Task RunAsync_returns_tool_result_before_timeout()
    {
        AgentToolTimeoutRunner runner = new(toolTimeoutSeconds: 1);

        string result = await runner.RunAsync(
            "fast_tool",
            _ => Task.FromResult("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task RunAsync_rejects_tool_that_exceeds_timeout()
    {
        AgentToolTimeoutRunner runner = new(toolTimeoutSeconds: 1);

        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync(
                "slow_tool",
                async cancellationToken =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    return "late";
                }));

        Assert.Contains("slow_tool", error.Message);
        Assert.Contains("1 second", error.Message);
    }

    [Fact]
    public void Constructor_rejects_non_positive_timeout()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentToolTimeoutRunner(toolTimeoutSeconds: 0));

        Assert.Equal("toolTimeoutSeconds", error.ParamName);
    }
}
