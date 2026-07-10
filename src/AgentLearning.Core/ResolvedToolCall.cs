using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// A checkpoint-safe record of a tool call that has already been resolved.
/// Resume can reuse this observation without executing the same tool again.
/// </summary>
public sealed record ResolvedToolCall(
    /// <summary>The model-generated tool_call_id that the tool observation must match.</summary>
    [property: JsonPropertyName("tool_call_id")]
    string ToolCallId,

    /// <summary>The resolved tool name.</summary>
    [property: JsonPropertyName("tool_name")]
    string ToolName,

    /// <summary>Whether the user approved this tool call.</summary>
    [property: JsonPropertyName("approved")]
    bool Approved,

    /// <summary>Whether the local tool was actually executed.</summary>
    [property: JsonPropertyName("tool_executed")]
    bool ToolExecuted,

    /// <summary>The tool result or rejection observation that should be sent back to the model.</summary>
    [property: JsonPropertyName("observation")]
    string Observation)
{
    /// <summary>Create a resolved tool record from a resume result.</summary>
    public static ResolvedToolCall FromResumeResult(AgentCheckpointResumeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ResolvedToolCall(
            result.ToolCallId,
            result.ToolName,
            result.Approved,
            result.ToolExecuted,
            result.Observation);
    }
}
