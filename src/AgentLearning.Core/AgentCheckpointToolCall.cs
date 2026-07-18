using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Represents a tool-call snapshot stored in a checkpoint.
/// It stores only fields needed to restore context and does not depend on SDK types.
/// </summary>
public sealed record AgentCheckpointToolCall(
    /// <summary>The model-generated tool_call_id that later tool messages must match.</summary>
    [property: JsonPropertyName("id")]
    string Id,

    /// <summary>The tool name requested by the model.</summary>
    [property: JsonPropertyName("name")]
    string Name,

    /// <summary>The JSON arguments supplied by the model.</summary>
    [property: JsonPropertyName("arguments_json")]
    string ArgumentsJson);
