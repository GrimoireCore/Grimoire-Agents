namespace AgentLearning.Core.Skills;

/// <summary>
/// A skill that the agent can expose to the model and execute locally.
/// </summary>
public interface IAgentSkill
{
    /// <summary>The function name exposed to the model.</summary>
    string Name { get; }

    /// <summary>The description shown to the model.</summary>
    string Description { get; }

    /// <summary>The JSON Schema for model-generated arguments.</summary>
    string ParametersJson { get; }

    /// <summary>The risk level used by the harness permission policy.</summary>
    AgentSkillRiskLevel RiskLevel { get; }

    /// <summary>Whether this skill always requires human approval.</summary>
    bool RequiresConfirmation { get; }

    /// <summary>
    /// Executes the skill with model arguments and trusted harness metadata.
    /// </summary>
    Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default);
}
