namespace AgentLearning.Core;

/// <summary>
/// Represents one persisted turn in the conversation.
/// Role identifies the speaker, and Content stores the message text.
/// </summary>
public sealed record ChatTurn(ChatRole Role, string Content);
