using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Checkpoint 里保存的工具调用快照。
/// 它只记录恢复模型上下文需要的最小字段，不绑定 OpenAI SDK 类型。
/// </summary>
public sealed record AgentCheckpointToolCall(
    /// <summary>模型生成的 tool_call_id，后续 tool 消息必须用它对上。</summary>
    [property: JsonPropertyName("id")]
    string Id,

    /// <summary>模型想调用的工具名。</summary>
    [property: JsonPropertyName("name")]
    string Name,

    /// <summary>模型传给工具的 JSON 参数。</summary>
    [property: JsonPropertyName("arguments_json")]
    string ArgumentsJson);
