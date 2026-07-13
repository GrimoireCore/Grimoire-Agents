using AgentLearning.Core.Skills;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentLearning.App;

/// <summary>
/// Adapts one dynamically discovered MCP tool to the existing agent skill contract.
/// </summary>
public sealed class McpAgentSkill : IAgentSkill
{
    private readonly McpClientTool _tool;

    public McpAgentSkill(McpClientTool tool, McpToolPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(tool.Description))
        {
            throw new InvalidOperationException($"MCP tool '{tool.Name}' must provide a description.");
        }

        _tool = tool;
        Description = tool.Description;
        RiskLevel = policy.RiskLevel;
        RequiresConfirmation = policy.RequiresConfirmation;
    }

    public string Name => _tool.Name;

    public string Description { get; }

    public string ParametersJson => _tool.JsonSchema.GetRawText();

    public AgentSkillRiskLevel RiskLevel { get; }

    public bool RequiresConfirmation { get; }

    public async Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);

        Dictionary<string, object?> arguments = ParseArguments(argumentsJson);
        RequestOptions requestOptions = new()
        {
            Meta = new JsonObject
            {
                ["io.grimoire/run-id"] = executionContext.RunId,
                ["io.grimoire/tool-call-id"] = executionContext.ToolCallId,
                ["io.grimoire/idempotency-key"] = executionContext.IdempotencyKey
            }
        };

        CallToolResult result = await _tool.CallAsync(
            arguments,
            options: requestOptions,
            cancellationToken: cancellationToken);
        string resultText = ReadResultText(result);

        if (result.IsError is true)
        {
            throw new InvalidOperationException($"MCP tool '{Name}' failed: {resultText}");
        }

        return resultText;
    }

    private static Dictionary<string, object?> ParseArguments(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("MCP tool arguments must be a JSON object.");
        }

        return document.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
    }

    private static string ReadResultText(CallToolResult result)
    {
        string text = string.Join(
            Environment.NewLine,
            result.Content
                .OfType<TextContentBlock>()
                .Select(content => content.Text));

        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (result.StructuredContent is JsonElement structuredContent)
        {
            return structuredContent.GetRawText();
        }

        throw new InvalidOperationException("MCP tool returned no text or structured content.");
    }
}
