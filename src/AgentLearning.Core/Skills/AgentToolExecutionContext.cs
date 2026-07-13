using System.Security.Cryptography;
using System.Text;

namespace AgentLearning.Core.Skills;

/// <summary>
/// Trusted metadata that identifies one logical tool call across retries.
/// </summary>
public sealed class AgentToolExecutionContext
{
    public AgentToolExecutionContext(string runId, string toolCallId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);

        RunId = runId.Trim();
        ToolCallId = toolCallId.Trim();
        IdempotencyKey = BuildIdempotencyKey(RunId, ToolCallId);
    }

    public string RunId { get; }

    public string ToolCallId { get; }

    public string IdempotencyKey { get; }

    /// <summary>
    /// Creates a unique context for a local command that is not part of an agent run.
    /// </summary>
    public static AgentToolExecutionContext CreateLocalCommand()
    {
        string id = Guid.NewGuid().ToString("N");
        return new AgentToolExecutionContext($"local_{id}", $"call_{id}");
    }

    private static string BuildIdempotencyKey(string runId, string toolCallId)
    {
        byte[] source = Encoding.UTF8.GetBytes($"{runId}\n{toolCallId}");
        return Convert.ToHexString(SHA256.HashData(source));
    }
}
