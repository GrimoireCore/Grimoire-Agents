namespace AgentLearning.Core;

/// <summary>
/// Decides whether content is suitable for long-term memory.
/// The current input still reaches the model; this policy only controls future retention.
/// </summary>
public sealed class AgentMemoryWritePolicy
{
    private static readonly string[] SensitiveMarkers =
    [
        "api_key",
        "password",
        "token",
        "bearer ",
        "sk-"
    ];

    private readonly int _maxMemoryContentChars;

    public AgentMemoryWritePolicy(int maxMemoryContentChars)
    {
        if (maxMemoryContentChars <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMemoryContentChars),
                "Maximum memory content characters must be greater than zero.");
        }

        _maxMemoryContentChars = maxMemoryContentChars;
    }

    public bool ShouldWrite(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (content.Length > _maxMemoryContentChars)
        {
            return false;
        }

        return !SensitiveMarkers.Any(marker =>
            content.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
