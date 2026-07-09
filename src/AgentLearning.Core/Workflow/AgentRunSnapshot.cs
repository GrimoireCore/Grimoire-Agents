namespace AgentLearning.Core.Workflow;

/// <summary>
/// Agent 运行状态的只读快照。
/// 对外暴露快照而不是可变对象，可以避免 UI 或调用方误改状态机内部数据。
/// </summary>
public sealed record AgentRunSnapshot(
    /// <summary>当前运行状态。</summary>
    AgentRunStatus Status,

    /// <summary>这一轮已经请求主模型多少次。</summary>
    int ModelRequestCount,

    /// <summary>这一轮已经收到多少个工具调用请求。</summary>
    int ToolCallCount,

    /// <summary>最近一次涉及的工具名。</summary>
    string? LastToolName,

    /// <summary>当前是否正在等待用户确认工具调用。</summary>
    bool WaitingForApproval,

    /// <summary>最近一次错误信息。</summary>
    string? LastError);
