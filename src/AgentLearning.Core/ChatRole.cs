namespace AgentLearning.Core;

/// <summary>
/// 对话消息的角色。
/// 模型需要靠这个区分“用户说的话”和“助手说的话”。
/// </summary>
public enum ChatRole
{
    /// <summary>用户输入。</summary>
    User,

    /// <summary>Agent 回复。</summary>
    Assistant
}
