using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// 把聊天记忆保存到本地 JSON 文件。
/// 这一层只负责读写文件，不负责决定什么内容值得记住。
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
    /// 从 JSON 文件加载记忆；文件不存在时返回空记忆。
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
    /// 把当前记忆保存到 JSON 文件。
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
