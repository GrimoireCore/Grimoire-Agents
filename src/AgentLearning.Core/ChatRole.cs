namespace AgentLearning.Core;

/// <summary>
/// Identifies the role of a chat message.
/// The model uses this value to distinguish user input from assistant replies.
/// </summary>
public enum ChatRole
{
    /// <summary>A message written by the user.</summary>
    User,

    /// <summary>A reply written by the agent.</summary>
    Assistant
}
