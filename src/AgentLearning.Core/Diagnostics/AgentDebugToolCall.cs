namespace AgentLearning.Core.Diagnostics;

/// <summary>
/// 调试视图里的工具调用。
/// 它只用于打印给人看，不负责真正调用工具。
/// </summary>
public sealed record AgentDebugToolCall(
    string Id,
    string Name,
    string ArgumentsJson);
