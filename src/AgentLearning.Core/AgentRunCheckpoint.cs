using AgentLearning.Core.Workflow;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Durable snapshot used to resume an interrupted agent run.
/// </summary>
public sealed record AgentRunCheckpoint(
    /// <summary>The unique ID for one agent run.</summary>
    [property: JsonPropertyName("run_id")]
    string RunId,

    /// <summary>The checkpoint kind.</summary>
    [property: JsonPropertyName("kind")]
    AgentCheckpointKind Kind,

    /// <summary>When this checkpoint was created.</summary>
    [property: JsonPropertyName("created_at")]
    DateTimeOffset CreatedAt,

    /// <summary>The run state snapshot at the checkpoint moment.</summary>
    [property: JsonPropertyName("state")]
    AgentRunSnapshot State,

    /// <summary>The model message snapshot captured before pausing.</summary>
    [property: JsonPropertyName("messages")]
    IReadOnlyList<AgentCheckpointMessage> Messages,

    /// <summary>The tool names exposed to the main agent for this run.</summary>
    [property: JsonPropertyName("selected_tool_names")]
    IReadOnlyList<string> SelectedToolNames,

    /// <summary>The pending tool approval data, when the run is waiting for approval.</summary>
    [property: JsonPropertyName("pending_approval")]
    PendingToolApproval? PendingApproval,

    /// <summary>The resolved tool result, when the run already has an observation to send back.</summary>
    [property: JsonPropertyName("resolved_tool")]
    ResolvedToolCall? ResolvedTool)
{
    /// <summary>Create a checkpoint while waiting for tool approval.</summary>
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
            PendingToolApproval.FromConfirmationRequest(request),
            ResolvedTool: null);
    }

    /// <summary>Create a checkpoint after a tool has been resolved but before the model finishes.</summary>
    public static AgentRunCheckpoint CreateToolResolved(
        string runId,
        DateTimeOffset createdAt,
        AgentRunSnapshot state,
        IReadOnlyList<AgentCheckpointMessage> messages,
        IReadOnlyList<string> selectedToolNames,
        ResolvedToolCall resolvedTool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(selectedToolNames);
        ArgumentNullException.ThrowIfNull(resolvedTool);

        if (state.Status is not AgentRunStatus.ToolExecuted and not AgentRunStatus.ToolRejected)
        {
            throw new InvalidOperationException("Tool resolved checkpoint requires state ToolExecuted or ToolRejected.");
        }

        return new AgentRunCheckpoint(
            runId.Trim(),
            AgentCheckpointKind.ToolResolved,
            createdAt,
            state,
            messages.ToArray(),
            selectedToolNames.Select(toolName => toolName.Trim()).ToArray(),
            PendingApproval: null,
            resolvedTool);
    }
}
