using AgentLearning.Core.Skills;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AgentLearning.Core;

/// <summary>
/// Converts the complete skill list into a lightweight tool catalog.
/// Tool Router needs names and purposes, not every complete parameter schema.
/// </summary>
public static class AgentToolCatalogBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Builds lightweight catalog objects for testing and extension.</summary>
    public static IReadOnlyList<AgentToolCatalogItem> Build(IEnumerable<IAgentSkill> skills)
    {
        return skills
            .Select(skill => new AgentToolCatalogItem(
                skill.Name,
                skill.Description,
                skill.RiskLevel.ToString(),
                AgentToolPermissionPolicy.RequiresConfirmation(skill)))
            .ToArray();
    }

    /// <summary>Builds the lightweight catalog JSON shown to the model.</summary>
    public static string BuildJson(IEnumerable<IAgentSkill> skills)
    {
        return JsonSerializer.Serialize(Build(skills), JsonOptions);
    }
}
