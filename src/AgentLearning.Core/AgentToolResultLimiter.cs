namespace AgentLearning.Core;

/// <summary>
/// 限制工具结果正文长度，避免一次工具调用把太多内容塞回模型上下文。
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
