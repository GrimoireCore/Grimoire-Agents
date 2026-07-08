namespace AgentLearning.Core;

/// <summary>
/// AI Tool Router 的选择结果。
/// 主 Agent 只会收到这里选中的工具完整 Schema。
/// </summary>
public sealed record AgentToolRoutingDecision(
    /// <summary>Router 是否认为这一轮需要工具。</summary>
    bool NeedTools,

    /// <summary>Router 选择的工具名列表，必须能在本地技能注册表中找到。</summary>
    IReadOnlyList<string> SelectedToolNames,

    /// <summary>Router 给出的简短原因，主要用于调试和学习。</summary>
    string Reason);
