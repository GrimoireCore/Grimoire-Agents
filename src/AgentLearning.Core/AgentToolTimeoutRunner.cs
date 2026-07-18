namespace AgentLearning.Core;

/// <summary>
/// Runs a tool within a time limit so a stalled tool cannot block the agent forever.
/// </summary>
public sealed class AgentToolTimeoutRunner
{
    private readonly int _toolTimeoutSeconds;
    private readonly TimeSpan _timeout;

    public AgentToolTimeoutRunner(int toolTimeoutSeconds)
    {
        if (toolTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toolTimeoutSeconds),
                "Tool timeout seconds must be greater than zero.");
        }

        _toolTimeoutSeconds = toolTimeoutSeconds;
        _timeout = TimeSpan.FromSeconds(toolTimeoutSeconds);
    }

    public async Task<string> RunAsync(
        string toolName,
        Func<CancellationToken, Task<string>> runToolAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(runToolAsync);

        using CancellationTokenSource timeout = new(_timeout);

        try
        {
            return await runToolAsync(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Tool '{toolName}' timed out after {_toolTimeoutSeconds} second(s).");
        }
    }
}
