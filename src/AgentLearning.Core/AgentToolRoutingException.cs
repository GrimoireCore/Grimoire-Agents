namespace AgentLearning.Core;

/// <summary>
/// Indicates that Tool Router returned an untrusted selection.
/// The host exposes this error instead of silently falling back to every tool.
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
