namespace AgentLearning.Core;

/// <summary>
/// 在限定时间内执行工具，避免某个工具卡住后让 Agent 一直等待。
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
