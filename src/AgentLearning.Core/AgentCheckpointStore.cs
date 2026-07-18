using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Persists agent checkpoints in a local JSON file.
/// Production systems commonly replace this teaching implementation with durable storage.
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

    /// <summary>Loads a checkpoint, returning null when the file does not exist.</summary>
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

    /// <summary>Saves a checkpoint and creates its directory when necessary.</summary>
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

    /// <summary>Deletes a checkpoint file and does nothing when it is absent.</summary>
    public static Task DeleteAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}
