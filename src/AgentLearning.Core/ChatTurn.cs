namespace AgentLearning.Core;

/// <summary>
/// 一条对话记忆。
/// Role 说明是谁说的，Content 是具体文本。
/// </summary>
public sealed record ChatTurn(ChatRole Role, string Content);
