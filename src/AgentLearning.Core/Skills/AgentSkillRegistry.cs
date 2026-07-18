namespace AgentLearning.Core.Skills;

/// <summary>
/// Registry for all skills available to the agent.
/// It resolves and executes skills by name so Program.cs does not need a large switch.
/// </summary>
public sealed class AgentSkillRegistry
{
    private readonly Dictionary<string, IAgentSkill> _skills;

    public AgentSkillRegistry(IEnumerable<IAgentSkill> skills)
    {
        _skills = skills.ToDictionary(skill => skill.Name, StringComparer.Ordinal);
    }

    /// <summary>All registered skills.</summary>
    public IReadOnlyCollection<IAgentSkill> Skills => _skills.Values;

    /// <summary>Gets skill metadata by function name and throws a clear error when missing.</summary>
    public IAgentSkill GetRequiredSkill(string skillName)
    {
        if (!_skills.TryGetValue(skillName, out IAgentSkill? skill))
        {
            throw new AgentUnknownSkillException(skillName);
        }

        return skill;
    }

    /// <summary>Executes a skill by function name.</summary>
    public async Task<string> ExecuteAsync(
        string skillName,
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);

        IAgentSkill skill = GetRequiredSkill(skillName);
        return await skill.ExecuteAsync(argumentsJson, executionContext, cancellationToken);
    }
}
