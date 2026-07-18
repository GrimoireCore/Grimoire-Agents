using AgentLearning.Core.Skills;

namespace AgentLearning.Core;

/// <summary>
/// Describes a tool call that the harness presents for user approval.
/// It explains the requested action but does not execute it.
/// </summary>
public sealed record AgentToolConfirmationRequest(
    /// <summary>The model-generated tool_call_id used to attach the resumed result correctly.</summary>
    string ToolCallId,

    /// <summary>The tool name requested by the model.</summary>
    string ToolName,

    /// <summary>A description that helps the user understand the tool's action.</summary>
    string Description,

    /// <summary>The original JSON arguments supplied by the model.</summary>
    string ArgumentsJson,

    /// <summary>The tool risk level.</summary>
    AgentSkillRiskLevel RiskLevel);
