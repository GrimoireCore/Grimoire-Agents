using AgentLearning.Core.Skills;

namespace AgentLearning.Core;

/// <summary>
/// 工具权限策略。
/// 模型可以建议调用工具，但是否能直接执行由 Harness 按这里的规则判断。
/// </summary>
public static class AgentToolPermissionPolicy
{
    /// <summary>
    /// 判断一个技能在执行前是否需要用户确认。
    /// </summary>
    public static bool RequiresConfirmation(IAgentSkill skill)
    {
        return skill.RequiresConfirmation || skill.RiskLevel >= AgentSkillRiskLevel.Medium;
    }
}
