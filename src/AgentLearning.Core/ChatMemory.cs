namespace AgentLearning.Core;

/// <summary>
/// Stores the chat memory for the current conversation.
/// This class maintains in-memory order; ChatMemoryStore handles persistence.
/// </summary>
public sealed class ChatMemory
{
    // A list is appropriate because turns are appended in sequence.
    private readonly List<ChatTurn> _turns = [];

    // Expose a read-only view so callers cannot corrupt the message order.
    public IReadOnlyList<ChatTurn> Turns => _turns;

    /// <summary>
    /// Stores a message written by the user.
    /// </summary>
    public void AddUserMessage(string content)
    {
        Add(ChatRole.User, content);
    }

    /// <summary>
    /// Stores a reply written by the agent.
    /// </summary>
    public void AddAssistantMessage(string content)
    {
        Add(ChatRole.Assistant, content);
    }

    // Keep validation and whitespace normalization in one entry point.
    private void Add(ChatRole role, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message content cannot be empty.", nameof(content));
        }

        _turns.Add(new ChatTurn(role, content.Trim()));
    }
}
