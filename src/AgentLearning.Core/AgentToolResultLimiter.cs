namespace AgentLearning.Core;

/// <summary>
/// Limits tool-result text so one call cannot flood the model context.
/// </summary>
public sealed class AgentToolResultLimiter
{
    private readonly int _maxToolResultChars;

    public AgentToolResultLimiter(int maxToolResultChars)
    {
        if (maxToolResultChars <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxToolResultChars),
                "Maximum tool result characters must be greater than zero.");
        }

        _maxToolResultChars = maxToolResultChars;
    }

    public string Limit(string result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Length <= _maxToolResultChars)
        {
            return result;
        }

        string keptContent = result[.._maxToolResultChars];
        return $"""
            {keptContent}

            [工具结果过长，已截断。原始长度：{result.Length} 字符，只保留前 {_maxToolResultChars} 字符。]
            """;
    }
}
