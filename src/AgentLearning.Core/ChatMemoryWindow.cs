namespace AgentLearning.Core;

/// <summary>
/// Selects the context window to send from the complete chat memory.
/// ChatMemory retains the full history; this class only limits each request.
/// </summary>
public static class ChatMemoryWindow
{
    public static IReadOnlyList<ChatTurn> GetRecentTurns(ChatMemory memory, int maxTurns)
    {
        if (maxTurns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTurns), "Max turns must be greater than zero.");
        }

        IReadOnlyList<ChatTurn> turns = memory.Turns;
        if (turns.Count <= maxTurns)
        {
            return turns;
        }

        ChatTurn[] window = turns
            .Skip(turns.Count - maxTurns)
            .ToArray();

        // If the window starts with an old assistant reply, its user prompt was trimmed.
        // Remove the orphaned reply so the model does not receive half a conversation.
        if (window.Length > 0 && window[0].Role == ChatRole.Assistant)
        {
            return window.Skip(1).ToArray();
        }

        return window;
    }
}
