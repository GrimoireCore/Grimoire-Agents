namespace AgentLearning.Core;

/// <summary>
/// Tool Router 返回了无法信任的选择结果。
/// 这里选择直接暴露错误，而不是偷偷回退到“发送全部工具”。
/// </summary>
public sealed class AgentToolRoutingException : InvalidOperationException
{
    public AgentToolRoutingException(string message) : base(message)
    {
    }

    public AgentToolRoutingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
