using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Persists chat memory in a local JSON file.
/// This layer only performs file I/O; it does not decide what is worth remembering.
/// </summary>
public static class ChatMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads memory from JSON, returning empty memory when the file does not exist.
    /// </summary>
    public static async Task<ChatMemory> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new ChatMemory();
        }

        await using FileStream stream = File.OpenRead(filePath);
        StoredMemory? storedMemory = await JsonSerializer.DeserializeAsync<StoredMemory>(
            stream,
            JsonOptions,
            cancellationToken);

        if (storedMemory is null)
        {
            throw new InvalidOperationException("Memory file is empty or invalid.");
        }

        ChatMemory memory = new();
        foreach (StoredTurn turn in storedMemory.Turns)
        {
            AddStoredTurn(memory, turn);
        }

        return memory;
    }

    /// <summary>
    /// Saves the current memory to a JSON file.
    /// </summary>
    public static async Task SaveAsync(
        string filePath,
        ChatMemory memory,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        StoredMemory storedMemory = new(
            memory.Turns
                .Select(turn => new StoredTurn(ToStoredRole(turn.Role), turn.Content))
                .ToArray());

        await using FileStream stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, storedMemory, JsonOptions, cancellationToken);
    }

    private static void AddStoredTurn(ChatMemory memory, StoredTurn turn)
    {
        if (turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            memory.AddUserMessage(turn.Content);
            return;
        }

        if (turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
        {
            memory.AddAssistantMessage(turn.Content);
            return;
        }

        throw new InvalidOperationException($"Unsupported memory role: {turn.Role}");
    }

    private static string ToStoredRole(ChatRole role)
    {
        return role switch
        {
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            _ => throw new InvalidOperationException($"Unsupported chat role: {role}")
        };
    }

    private sealed record StoredMemory(
        [property: JsonPropertyName("turns")]
        IReadOnlyList<StoredTurn> Turns);

    private sealed record StoredTurn(
        [property: JsonPropertyName("role")]
        string Role,

        [property: JsonPropertyName("content")]
        string Content);
}
