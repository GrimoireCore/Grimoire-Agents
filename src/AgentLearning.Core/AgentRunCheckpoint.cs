using AgentLearning.Core.Workflow;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Agent 暂停运行时保存的恢复点。
/// 第一版只保存等待工具确认的暂停点，暂时不保存完整模型消息历史。
/// </summary>
public sealed record AgentRunCheckpoint(
    /// <summary>一次 Agent 运行的唯一 ID。</summary>
    [property: JsonPropertyName("run_id")]
    string RunId,

    /// <summary>Checkpoint 类型。</summary>
    [property: JsonPropertyName("kind")]
    AgentCheckpointKind Kind,

    /// <summary>Checkpoint 创建时间。</summary>
    [property: JsonPropertyName("created_at")]
    DateTimeOffset CreatedAt,

    /// <summary>暂停时的运行状态快照。</summary>
    [property: JsonPropertyName("state")]
    AgentRunSnapshot State,

    /// <summary>如果当前在等待工具确认，这里保存待确认工具信息。</summary>
    [property: JsonPropertyName("pending_approval")]
    PendingToolApproval? PendingApproval)
{
    /// <summary>创建等待工具确认的 Checkpoint。</summary>
    public static AgentRunCheckpoint CreatePendingApproval(
        string runId,
        DateTimeOffset createdAt,
        AgentToolConfirmationRequest request,
        AgentRunSnapshot state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status != AgentRunStatus.WaitingForApproval || !state.WaitingForApproval)
        {
            throw new InvalidOperationException("Pending approval checkpoint requires state WaitingForApproval.");
        }

        return new AgentRunCheckpoint(
            runId.Trim(),
            AgentCheckpointKind.PendingToolApproval,
            createdAt,
            state,
            PendingToolApproval.FromConfirmationRequest(request));
    }
}
