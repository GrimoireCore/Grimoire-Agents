namespace AgentLearning.Core;

/// <summary>
/// 对话消息的角色。
/// Agent 至少要区分“用户说的”和“助手回答的”，模型才知道上下文是谁说的。
/// </summary>
public enum ChatRole
{
    /// <summary>用户输入。</summary>
    User,

    /// <summary>Agent 回复。</summary>
    Assistant
}
