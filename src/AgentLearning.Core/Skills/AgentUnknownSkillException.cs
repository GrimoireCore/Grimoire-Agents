namespace AgentLearning.Core.Skills;

/// <summary>
/// 表示模型请求了本地不存在的技能。
/// 这类错误通常说明工具声明或模型协议出了问题，不应该交给模型自行解释。
/// </summary>
public sealed class AgentUnknownSkillException : InvalidOperationException
{
    public AgentUnknownSkillException(string skillName)
        : base($"Unknown skill: {skillName}")
    {
        SkillName = skillName;
    }

    public string SkillName { get; }
}
