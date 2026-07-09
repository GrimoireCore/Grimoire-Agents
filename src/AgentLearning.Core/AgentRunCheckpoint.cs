using AgentLearning.Core.Workflow;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Agent 暂停运行时保存的恢复点。
/// 它保存足够多的运行现场，让下一次启动可以继续同一轮模型工具循环。
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

    /// <summary>暂停前已经发送给模型的消息快照。</summary>
    [property: JsonPropertyName("messages")]
    IReadOnlyList<AgentCheckpointMessage> Messages,

    /// <summary>暂停前主 Agent 这一轮拿到的工具名。</summary>
    [property: JsonPropertyName("selected_tool_names")]
    IReadOnlyList<string> SelectedToolNames,

    /// <summary>如果当前在等待工具确认，这里保存待确认工具信息。</summary>
    [property: JsonPropertyName("pending_approval")]
    PendingToolApproval? PendingApproval)
{
    /// <summary>创建等待工具确认的 Checkpoint。</summary>
    public static AgentRunCheckpoint CreatePendingApproval(
        string runId,
        DateTimeOffset createdAt,
        AgentToolConfirmationRequest request,
        AgentRunSnapshot state,
        IReadOnlyList<AgentCheckpointMessage> messages,
        IReadOnlyList<string> selectedToolNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(selectedToolNames);

        if (state.Status != AgentRunStatus.WaitingForApproval || !state.WaitingForApproval)
        {
            throw new InvalidOperationException("Pending approval checkpoint requires state WaitingForApproval.");
        }

        return new AgentRunCheckpoint(
            runId.Trim(),
            AgentCheckpointKind.PendingToolApproval,
            createdAt,
            state,
            messages.ToArray(),
            selectedToolNames.Select(toolName => toolName.Trim()).ToArray(),
            PendingToolApproval.FromConfirmationRequest(request));
    }
}
