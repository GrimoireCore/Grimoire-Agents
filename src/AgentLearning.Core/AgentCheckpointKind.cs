namespace AgentLearning.Core;

/// <summary>
/// Checkpoint 的类型。
/// 第一版只支持“等待工具确认”，后续可以扩展成等待外部任务、等待文件上传等。
/// </summary>
public enum AgentCheckpointKind
{
    /// <summary>Agent 已暂停，正在等待用户确认一个工具调用。</summary>
    PendingToolApproval
}
