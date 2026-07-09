using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// 把 Agent Checkpoint 保存到本地 JSON 文件。
/// 这是教学版实现；真实系统里通常会换成数据库、Redis 或队列。
/// </summary>
public static class AgentCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>从文件读取 Checkpoint。文件不存在时返回 null。</summary>
    public static async Task<AgentRunCheckpoint?> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<AgentRunCheckpoint>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    /// <summary>把 Checkpoint 保存到文件，必要时自动创建目录。</summary>
    public static async Task SaveAsync(
        string filePath,
        AgentRunCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, checkpoint, JsonOptions, cancellationToken);
    }

    /// <summary>删除 Checkpoint 文件。文件不存在时什么也不做。</summary>
    public static Task DeleteAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}
