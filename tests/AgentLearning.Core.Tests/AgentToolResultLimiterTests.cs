using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolResultLimiterTests
{
    [Fact]
    public void Limit_keeps_short_tool_results_unchanged()
    {
        AgentToolResultLimiter limiter = new(maxToolResultChars: 20);

        string result = limiter.Limit("short result");

        Assert.Equal("short result", result);
    }

    [Fact]
    public void Limit_truncates_long_tool_results_with_a_clear_notice()
    {
        AgentToolResultLimiter limiter = new(maxToolResultChars: 8);

        string result = limiter.Limit("abcdefghijklmnop");

        Assert.StartsWith("abcdefgh", result);
        Assert.DoesNotContain("ijklmnop", result);
        Assert.Contains("工具结果过长，已截断", result);
        Assert.Contains("原始长度：16", result);
        Assert.Contains("只保留前 8", result);
    }

    [Fact]
    public void Constructor_rejects_non_positive_limits()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentToolResultLimiter(maxToolResultChars: 0));

        Assert.Equal("maxToolResultChars", error.ParamName);
    }
}
