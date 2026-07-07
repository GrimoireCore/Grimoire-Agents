namespace AgentLearning.Core;

/// <summary>
/// 从完整聊天记忆里选择本次要发给模型的上下文窗口。
/// 完整历史仍然保存在 ChatMemory 里，这里只控制“发多少给模型”。
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

        // 如果窗口从旧的 assistant 回复开始，说明它前面的 user 问题已经被裁掉了。
        // 丢掉这条孤立回复，避免模型看到没有上下文的半截对话。
        if (window.Length > 0 && window[0].Role == ChatRole.Assistant)
        {
            return window.Skip(1).ToArray();
        }

        return window;
    }
}
