using AgentLearning.Core.Skills;
using System.Text.Json;

namespace AgentLearning.Core;

/// <summary>
/// Parses and validates JSON returned by AI Tool Router.
/// The model makes the semantic choice; this code enforces engineering boundaries.
/// </summary>
public static class AgentToolRoutingDecisionParser
{
    /// <summary>
    /// Parses router JSON and validates tool names and the selection limit.
    /// </summary>
    public static AgentToolRoutingDecision Parse(
        string routerJson,
        IEnumerable<IAgentSkill> availableSkills,
        int maxToolsPerRequest)
    {
        if (maxToolsPerRequest <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxToolsPerRequest), "maxToolsPerRequest must be greater than zero.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(routerJson);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AgentToolRoutingException("Tool router response must be a JSON object.");
            }

            bool needTools = ReadRequiredBoolean(root, "need_tools");
            IReadOnlyList<string> selectedToolNames = ReadRequiredStringArray(root, "selected_tools");
            string reason = ReadRequiredString(root, "reason");

            ValidateSelectionConsistency(needTools, selectedToolNames);
            ValidateSelectionCount(selectedToolNames, maxToolsPerRequest);
            ValidateSelectedToolsExist(selectedToolNames, availableSkills);

            return new AgentToolRoutingDecision(needTools, selectedToolNames, reason);
        }
        catch (JsonException exception)
        {
            throw new AgentToolRoutingException("Tool router response must be valid JSON.", exception);
        }
    }

    private static bool ReadRequiredBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new AgentToolRoutingException($"Tool router response field '{propertyName}' must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new AgentToolRoutingException($"Tool router response field '{propertyName}' must be a string.");
        }

        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AgentToolRoutingException($"Tool router response field '{propertyName}' cannot be empty.");
        }

        return text;
    }

    private static IReadOnlyList<string> ReadRequiredStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw new AgentToolRoutingException($"Tool router response field '{propertyName}' must be an array.");
        }

        List<string> items = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new AgentToolRoutingException($"Tool router response field '{propertyName}' must only contain strings.");
            }

            string? toolName = item.GetString();
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new AgentToolRoutingException($"Tool router response field '{propertyName}' cannot contain empty names.");
            }

            items.Add(toolName);
        }

        return items;
    }

    private static void ValidateSelectionConsistency(bool needTools, IReadOnlyList<string> selectedToolNames)
    {
        if (needTools && selectedToolNames.Count == 0)
        {
            throw new AgentToolRoutingException("Tool router said need_tools=true, but selected_tools is empty.");
        }

        if (!needTools && selectedToolNames.Count > 0)
        {
            throw new AgentToolRoutingException("Tool router said need_tools=false, but selected_tools is not empty.");
        }
    }

    private static void ValidateSelectionCount(IReadOnlyList<string> selectedToolNames, int maxToolsPerRequest)
    {
        if (selectedToolNames.Count > maxToolsPerRequest)
        {
            throw new AgentToolRoutingException(
                $"Tool router selected {selectedToolNames.Count} tools, but max_tools_per_request is {maxToolsPerRequest}.");
        }

        HashSet<string> seenNames = new(StringComparer.Ordinal);
        foreach (string toolName in selectedToolNames)
        {
            if (!seenNames.Add(toolName))
            {
                throw new AgentToolRoutingException($"Tool router selected duplicate tool '{toolName}'.");
            }
        }
    }

    private static void ValidateSelectedToolsExist(
        IReadOnlyList<string> selectedToolNames,
        IEnumerable<IAgentSkill> availableSkills)
    {
        HashSet<string> availableToolNames = availableSkills
            .Select(skill => skill.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string toolName in selectedToolNames)
        {
            if (!availableToolNames.Contains(toolName))
            {
                throw new AgentToolRoutingException($"Tool router selected unknown tool '{toolName}'.");
            }
        }
    }
}
