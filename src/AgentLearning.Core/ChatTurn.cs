namespace AgentLearning.Core;

/// <summary>
/// 一条被保存的对话记忆。
/// Role 表示是谁说的，Content 保存具体文本。
/// </summary>
public sealed record ChatTurn(ChatRole Role, string Content);
