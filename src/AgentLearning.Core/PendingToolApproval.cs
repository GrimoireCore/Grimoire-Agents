using AgentLearning.Core.Skills;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Represents a pending tool approval stored in a checkpoint.
/// It contains only data needed to resume and does not depend on SDK types.
/// </summary>
public sealed record PendingToolApproval(
    /// <summary>The model-generated tool_call_id that must match the resumed result.</summary>
    [property: JsonPropertyName("tool_call_id")]
    string ToolCallId,

    /// <summary>The pending tool name.</summary>
    [property: JsonPropertyName("tool_name")]
    string ToolName,

    /// <summary>A description shown to the user by a resume interface.</summary>
    [property: JsonPropertyName("description")]
    string Description,

    /// <summary>The original JSON arguments supplied by the model.</summary>
    [property: JsonPropertyName("arguments_json")]
    string ArgumentsJson,

    /// <summary>The tool risk level.</summary>
    [property: JsonPropertyName("risk_level")]
    AgentSkillRiskLevel RiskLevel)
{
    /// <summary>Converts an approval request into checkpoint data.</summary>
    public static PendingToolApproval FromConfirmationRequest(AgentToolConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PendingToolApproval(
            request.ToolCallId,
            request.ToolName,
            request.Description,
            request.ArgumentsJson,
            request.RiskLevel);
    }
}
