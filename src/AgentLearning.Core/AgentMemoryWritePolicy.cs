namespace AgentLearning.Core;

/// <summary>
/// 判断一段内容是否适合写入长期记忆。
/// 当前用户输入仍然会参与本轮模型调用；这里只控制“要不要保存到下一轮还能看到的记忆里”。
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
