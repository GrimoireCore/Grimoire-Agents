using AgentLearning.Core.Skills;

namespace AgentLearning.Core;

/// <summary>
/// Harness 请求用户确认工具调用时展示的信息。
/// 它只描述“模型想做什么”，不负责真正执行工具。
/// </summary>
public sealed record AgentToolConfirmationRequest(
    /// <summary>模型生成的 tool_call_id，恢复时要用它把工具结果回填给正确的调用。</summary>
    string ToolCallId,

    /// <summary>模型想调用的工具名。</summary>
    string ToolName,

    /// <summary>工具说明，帮助用户理解这个工具会做什么。</summary>
    string Description,

    /// <summary>模型传给工具的原始参数 JSON。</summary>
    string ArgumentsJson,

    /// <summary>工具风险等级。</summary>
    AgentSkillRiskLevel RiskLevel);
