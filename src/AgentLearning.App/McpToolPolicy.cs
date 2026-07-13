using AgentLearning.Core.Skills;

namespace AgentLearning.App;

/// <summary>
/// Local security policy for one remotely discovered MCP tool.
/// </summary>
public sealed record McpToolPolicy(
    AgentSkillRiskLevel RiskLevel,
    bool RequiresConfirmation);
