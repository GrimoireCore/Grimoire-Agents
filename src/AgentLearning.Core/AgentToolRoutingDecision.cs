namespace AgentLearning.Core;

/// <summary>
/// Represents the selection returned by AI Tool Router.
/// The main agent receives full schemas only for the selected tools.
/// </summary>
public sealed record AgentToolRoutingDecision(
    /// <summary>Whether the router believes this turn needs tools.</summary>
    bool NeedTools,

    /// <summary>Tool names selected by the router; each must exist in the local registry.</summary>
    IReadOnlyList<string> SelectedToolNames,

    /// <summary>A short routing reason used for diagnostics and learning.</summary>
    string Reason);
