using AgentLearning.Core.Skills;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Checkpoint 里保存的待确认工具调用。
/// 这里保存的是恢复执行所需的最小信息，不依赖 OpenAI SDK 类型。
/// </summary>
public sealed record PendingToolApproval(
    /// <summary>模型生成的 tool_call_id，恢复时要和 tool result 对上。</summary>
    [property: JsonPropertyName("tool_call_id")]
    string ToolCallId,

    /// <summary>待执行工具名。</summary>
    [property: JsonPropertyName("tool_name")]
    string ToolName,

    /// <summary>工具说明，方便恢复界面展示给用户。</summary>
    [property: JsonPropertyName("description")]
    string Description,

    /// <summary>模型传给工具的原始参数 JSON。</summary>
    [property: JsonPropertyName("arguments_json")]
    string ArgumentsJson,

    /// <summary>工具风险等级。</summary>
    [property: JsonPropertyName("risk_level")]
    AgentSkillRiskLevel RiskLevel)
{
    /// <summary>从确认请求转换成 Checkpoint 里的待确认工具信息。</summary>
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
