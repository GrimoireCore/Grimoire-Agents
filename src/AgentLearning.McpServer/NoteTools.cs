using AgentLearning.Core.Skills;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentLearning.McpServer;

/// <summary>
/// Note tools that modify the configured local Markdown file.
/// </summary>
[McpServerToolType]
public static class NoteTools
{
    private const string RunIdMetaKey = "io.grimoire/run-id";
    private const string ToolCallIdMetaKey = "io.grimoire/tool-call-id";
    private const string IdempotencyKeyMetaKey = "io.grimoire/idempotency-key";

    [McpServerTool(
        Name = "write_note",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Appends a note to the learner's local Markdown notes file.")]
    public static Task<string> WriteNoteAsync(
        [Description("The note content to append.")] string note,
        WriteNoteSkill writeNoteSkill,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new McpException("write_note requires a non-empty note.");
        }

        AgentToolExecutionContext executionContext = ReadExecutionContext(requestContext);
        string argumentsJson = JsonSerializer.Serialize(new { note });

        return writeNoteSkill.ExecuteAsync(argumentsJson, executionContext, cancellationToken);
    }

    private static AgentToolExecutionContext ReadExecutionContext(
        RequestContext<CallToolRequestParams> requestContext)
    {
        JsonObject? meta = requestContext.Params?.Meta;
        string runId = ReadRequiredMeta(meta, RunIdMetaKey);
        string toolCallId = ReadRequiredMeta(meta, ToolCallIdMetaKey);
        string receivedIdempotencyKey = ReadRequiredMeta(meta, IdempotencyKeyMetaKey);
        AgentToolExecutionContext executionContext = new(runId, toolCallId);

        if (!string.Equals(
                receivedIdempotencyKey,
                executionContext.IdempotencyKey,
                StringComparison.Ordinal))
        {
            throw new McpException("The MCP idempotency metadata does not match the tool call identity.");
        }

        return executionContext;
    }

    private static string ReadRequiredMeta(JsonObject? meta, string propertyName)
    {
        if (meta?[propertyName] is not JsonValue value
            || !value.TryGetValue(out string? text)
            || string.IsNullOrWhiteSpace(text))
        {
            throw new McpException($"Missing required MCP metadata '{propertyName}'.");
        }

        return text.Trim();
    }
}
