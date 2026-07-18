using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// A lightweight catalog entry sent to Tool Router.
/// It omits full parameter schemas so a growing tool set does not inflate context too quickly.
/// </summary>
public sealed record AgentToolCatalogItem(
    /// <summary>The real function name, which the router must return unchanged.</summary>
    [property: JsonPropertyName("name")]
    string Name,

    /// <summary>A short description that helps the router decide when to select the tool.</summary>
    [property: JsonPropertyName("description")]
    string Description,

    /// <summary>The risk level, indicating whether the tool may affect external state.</summary>
    [property: JsonPropertyName("risk_level")]
    string RiskLevel,

    /// <summary>Whether the tool requires user approval before execution.</summary>
    [property: JsonPropertyName("requires_confirmation")]
    bool RequiresConfirmation);
