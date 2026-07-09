namespace AgentLearning.Core;

/// <summary>
/// 从 Checkpoint 恢复一次工具确认后的结果。
/// 第一版只恢复工具执行层，不负责继续请求模型生成最终回答。
/// </summary>
public sealed record AgentCheckpointResumeResult(
    /// <summary>被恢复的 Agent 运行 ID。</summary>
    string RunId,

    /// <summary>恢复的 tool_call_id。</summary>
    string ToolCallId,

    /// <summary>恢复的工具名。</summary>
    string ToolName,

    /// <summary>用户是否批准执行工具。</summary>
    bool Approved,

    /// <summary>工具是否真的被执行。</summary>
    bool ToolExecuted,

    /// <summary>工具执行结果，或者用户拒绝时发回模型的 observation。</summary>
    string Observation);
