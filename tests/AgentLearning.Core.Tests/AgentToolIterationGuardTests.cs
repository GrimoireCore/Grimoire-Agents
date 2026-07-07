using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolIterationGuardTests
{
    [Fact]
    public void RecordToolIteration_allows_iterations_up_to_the_configured_limit()
    {
        AgentToolIterationGuard guard = new(maxToolIterations: 2);

        guard.RecordToolIteration();
        guard.RecordToolIteration();

        Assert.Equal(2, guard.UsedIterations);
    }

    [Fact]
    public void RecordToolIteration_rejects_iterations_after_the_configured_limit()
    {
        AgentToolIterationGuard guard = new(maxToolIterations: 1);

        guard.RecordToolIteration();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            guard.RecordToolIteration);

        Assert.Contains("Tool iteration limit reached", error.Message);
        Assert.Equal(1, guard.UsedIterations);
    }

    [Fact]
    public void Constructor_rejects_non_positive_limits()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentToolIterationGuard(maxToolIterations: 0));

        Assert.Equal("maxToolIterations", error.ParamName);
    }
}
