namespace AgentLearning.Core.Skills;

/// <summary>
/// Defines the risk level of a skill.
/// Higher levels should not run without explicit user approval.
/// </summary>
public enum AgentSkillRiskLevel
{
    /// <summary>Read-only or pure computation that does not change external state.</summary>
    Low,

    /// <summary>Writes local files, sends drafts, or changes low-impact state.</summary>
    Medium,

    /// <summary>May delete data, call external systems, or affect user or business state.</summary>
    High,

    /// <summary>Payments, purchases, bulk deletion, or other actions requiring strong confirmation.</summary>
    Critical
}
