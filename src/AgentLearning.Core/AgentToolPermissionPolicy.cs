using AgentLearning.Core.Skills;

namespace AgentLearning.Core;

/// <summary>
/// Defines the permission policy for tool execution.
/// The model may suggest a tool, while the harness decides whether it can run directly.
/// </summary>
public static class AgentToolPermissionPolicy
{
    /// <summary>
    /// Determines whether a skill needs user approval before execution.
    /// </summary>
    public static bool RequiresConfirmation(IAgentSkill skill)
    {
        return skill.RequiresConfirmation || skill.RiskLevel >= AgentSkillRiskLevel.Medium;
    }
}
