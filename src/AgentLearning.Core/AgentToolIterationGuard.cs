namespace AgentLearning.Core;

/// <summary>
/// Counts tool-call rounds in one agent run to prevent uncontrolled loops.
/// </summary>
public sealed class AgentToolIterationGuard
{
    private readonly int _maxToolIterations;

    public AgentToolIterationGuard(int maxToolIterations)
    {
        if (maxToolIterations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxToolIterations),
                "Maximum tool iterations must be greater than zero.");
        }

        _maxToolIterations = maxToolIterations;
    }

    public int UsedIterations { get; private set; }

    public void RecordToolIteration()
    {
        if (UsedIterations >= _maxToolIterations)
        {
            throw new InvalidOperationException(
                $"Tool iteration limit reached after {_maxToolIterations} iteration(s).");
        }

        UsedIterations++;
    }
}
