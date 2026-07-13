using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Reads and atomically writes the persisted vector index cache.
/// </summary>
internal static class VectorKnowledgeIndexCacheStore
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static async Task<VectorKnowledgeIndexCache?> LoadAsync(
        string cacheFilePath,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(cacheFilePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(fullPath);
            VectorKnowledgeIndexCache? cache = await JsonSerializer.DeserializeAsync<VectorKnowledgeIndexCache>(
                stream,
                JsonOptions,
                cancellationToken);
            return cache
                ?? throw new InvalidOperationException($"Vector index cache '{fullPath}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Vector index cache '{fullPath}' contains invalid JSON.",
                exception);
        }
    }

    public static async Task SaveAsync(
        string cacheFilePath,
        VectorKnowledgeIndexCache cache,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(cacheFilePath);
        string directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Vector index cache file must have a parent directory.");
        Directory.CreateDirectory(directoryPath);

        string tempFilePath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken);
            }

            File.Move(tempFilePath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    public static string BuildDocumentFingerprint(IReadOnlyList<KnowledgeChunk> chunks)
    {
        StringBuilder canonicalText = new();
        foreach (KnowledgeChunk chunk in chunks)
        {
            AppendLengthPrefixed(canonicalText, chunk.SourcePath);
            canonicalText.Append(chunk.ChunkNumber).Append(':');
            AppendLengthPrefixed(canonicalText, chunk.Content);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText.ToString())));
    }

    private static void AppendLengthPrefixed(StringBuilder output, string value)
    {
        output.Append(value.Length).Append(':').Append(value).Append(';');
    }
}

internal sealed record VectorKnowledgeIndexCache(
    int FormatVersion,
    string? EmbeddingModel,
    int ChunkSize,
    int ChunkOverlap,
    string? DocumentFingerprint,
    int EmbeddingDimensions,
    IReadOnlyList<VectorKnowledgeIndexCacheEntry>? Chunks);

internal sealed record VectorKnowledgeIndexCacheEntry(
    string? SourcePath,
    int ChunkNumber,
    string? Content,
    float[]? Embedding);
