using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class AgentMemoryWritePolicyTests
{
    [Fact]
    public void ShouldWrite_allows_normal_content()
    {
        AgentMemoryWritePolicy policy = new(maxMemoryContentChars: 100);

        Assert.True(policy.ShouldWrite("请继续讲 Agent 的记忆机制"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldWrite_rejects_empty_content(string content)
    {
        AgentMemoryWritePolicy policy = new(maxMemoryContentChars: 100);

        Assert.False(policy.ShouldWrite(content));
    }

    [Theory]
    [InlineData("my password is 123456")]
    [InlineData("api_key = secret")]
    [InlineData("Authorization: Bearer abc")]
    [InlineData("token: abc")]
    [InlineData("sk-test-secret")]
    public void ShouldWrite_rejects_obvious_secret_content(string content)
    {
        AgentMemoryWritePolicy policy = new(maxMemoryContentChars: 100);

        Assert.False(policy.ShouldWrite(content));
    }

    [Fact]
    public void ShouldWrite_rejects_content_over_the_configured_limit()
    {
        AgentMemoryWritePolicy policy = new(maxMemoryContentChars: 5);

        Assert.False(policy.ShouldWrite("123456"));
    }

    [Fact]
    public void Constructor_rejects_non_positive_limits()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentMemoryWritePolicy(maxMemoryContentChars: 0));

        Assert.Equal("maxMemoryContentChars", error.ParamName);
    }
}
