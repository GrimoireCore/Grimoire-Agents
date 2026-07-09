namespace AgentLearning.Core.Workflow;

/// <summary>
/// 一次 Agent 运行当前所处的状态。
/// 状态回答的是“现在 Agent 正在第几阶段”，比日志更适合给 UI 或排障使用。
/// </summary>
public enum AgentRunStatus
{
    /// <summary>刚创建状态机，还没有处理用户输入。</summary>
    Initialized,

    /// <summary>已经收到用户输入。</summary>
    ReceivedInput,

    /// <summary>已经构建好要发送给模型的上下文窗口。</summary>
    BuiltContext,

    /// <summary>已经完成工具路由，知道本轮要暴露哪些工具。</summary>
    RoutedTools,

    /// <summary>已经向主模型发起请求。</summary>
    AskedModel,

    /// <summary>模型请求调用工具，Harness 正准备处理。</summary>
    WaitingForTool,

    /// <summary>工具需要人工确认，正在等待用户 yes/no。</summary>
    WaitingForApproval,

    /// <summary>工具已经执行完成。</summary>
    ToolExecuted,

    /// <summary>用户拒绝了工具执行。</summary>
    ToolRejected,

    /// <summary>工具执行失败，但失败信息已经被转换成模型可观察结果。</summary>
    ToolFailed,

    /// <summary>一次 Agent 运行已经产出最终回答。</summary>
    Finished,

    /// <summary>运行出现无法恢复的错误。</summary>
    Failed
}
