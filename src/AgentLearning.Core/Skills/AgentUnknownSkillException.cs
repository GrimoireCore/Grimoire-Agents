namespace AgentLearning.Core.Skills;

/// <summary>
/// Indicates that the model requested a skill that is not registered locally.
/// This usually means the tool declaration or protocol is invalid and needs host handling.
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
