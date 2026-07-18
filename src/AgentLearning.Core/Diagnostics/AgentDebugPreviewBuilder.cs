using AgentLearning.Core.Skills;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgentLearning.Core.Diagnostics;

/// <summary>
/// Builds request previews for learning and troubleshooting.
/// The OpenAI SDK creates the real HTTP request; this is an equivalent teaching view.
/// </summary>
public static class AgentDebugPreviewBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds a preview of one Chat Completions request body.
    /// </summary>
    public static string BuildChatCompletionsRequestPreview(
        string model,
        bool stream,
        IEnumerable<AgentDebugMessage> messages,
        IEnumerable<IAgentSkill> skills,
        bool includeTools)
    {
        Dictionary<string, object?> request = new()
        {
            ["model"] = model,
            ["messages"] = messages.Select(ToMessageShape).ToArray(),
            ["stream"] = stream
        };

        if (includeTools)
        {
            request["tools"] = skills.Select(ToToolShape).ToArray();
        }

        string json = JsonSerializer.Serialize(request, JsonOptions);
        return RedactSensitiveValues(json);
    }

    /// <summary>
    /// Redacts common secrets so diagnostics do not print credentials.
    /// </summary>
    public static string RedactSensitiveValues(string text)
    {
        string redacted = Regex.Replace(
            text,
            """(?i)("(?:api_key|authorization)"\s*:\s*"Bearer\s+)[^"]+(")""",
            "$1[redacted]$2");

        redacted = Regex.Replace(
            redacted,
            """(?i)("(?:api_key)"\s*:\s*")[^"]+(")""",
            "$1[redacted]$2");

        redacted = Regex.Replace(
            redacted,
            """(?i)(Authorization\s*:\s*Bearer\s+)[^\s"]+""",
            "$1[redacted]");

        return redacted;
    }

    private static Dictionary<string, object?> ToMessageShape(AgentDebugMessage message)
    {
        Dictionary<string, object?> shape = new()
        {
            ["role"] = message.Role
        };

        if (message.ToolCalls.Count > 0)
        {
            shape["content"] = message.Content;
            shape["tool_calls"] = message.ToolCalls.Select(ToToolCallShape).ToArray();
            return shape;
        }

        if (message.Role.Equals("tool", StringComparison.Ordinal))
        {
            shape["tool_call_id"] = message.ToolCallId;
            shape["content"] = message.Content;
            return shape;
        }

        shape["content"] = message.Content;
        return shape;
    }

    private static Dictionary<string, object?> ToToolCallShape(AgentDebugToolCall toolCall)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = toolCall.Id,
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = toolCall.Name,
                ["arguments"] = toolCall.ArgumentsJson
            }
        };
    }

    private static Dictionary<string, object?> ToToolShape(IAgentSkill skill)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["parameters"] = ParseJsonObject(skill.ParametersJson)
            }
        };
    }

    private static JsonElement ParseJsonObject(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
