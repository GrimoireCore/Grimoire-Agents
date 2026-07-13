namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Loads Markdown files and splits their text into overlapping chunks.
/// </summary>
public static class MarkdownKnowledgeLoader
{
    public const int DefaultChunkSize = 800;
    public const int DefaultChunkOverlap = 120;

    public static async Task<IReadOnlyList<KnowledgeChunk>> LoadFromDirectoryAsync(
        string directoryPath,
        int chunkSize = DefaultChunkSize,
        int chunkOverlap = DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ValidateChunkSettings(chunkSize, chunkOverlap);

        string fullDirectoryPath = Path.GetFullPath(directoryPath.Trim());
        if (!Directory.Exists(fullDirectoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Knowledge directory '{fullDirectoryPath}' was not found.");
        }

        string[] markdownFiles = Directory
            .EnumerateFiles(fullDirectoryPath, "*.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (markdownFiles.Length == 0)
        {
            throw new InvalidOperationException(
                $"Knowledge directory '{fullDirectoryPath}' contains no Markdown files.");
        }

        List<KnowledgeChunk> chunks = [];
        foreach (string filePath in markdownFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            string sourcePath = Path
                .GetRelativePath(fullDirectoryPath, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            chunks.AddRange(SplitIntoChunks(sourcePath, content, chunkSize, chunkOverlap));
        }

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException(
                $"Knowledge directory '{fullDirectoryPath}' contains no searchable text.");
        }

        return chunks;
    }

    private static IReadOnlyList<KnowledgeChunk> SplitIntoChunks(
        string sourcePath,
        string content,
        int chunkSize,
        int chunkOverlap)
    {
        string normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalizedContent.Length == 0)
        {
            return [];
        }

        List<KnowledgeChunk> chunks = [];
        int stepSize = chunkSize - chunkOverlap;
        int chunkNumber = 1;
        for (int startIndex = 0; startIndex < normalizedContent.Length; startIndex += stepSize)
        {
            int length = Math.Min(chunkSize, normalizedContent.Length - startIndex);
            string chunkContent = normalizedContent.Substring(startIndex, length).Trim();
            if (chunkContent.Length > 0)
            {
                chunks.Add(new KnowledgeChunk(sourcePath, chunkNumber++, chunkContent));
            }

            if (startIndex + length >= normalizedContent.Length)
            {
                break;
            }
        }

        return chunks;
    }

    private static void ValidateChunkSettings(int chunkSize, int chunkOverlap)
    {
        if (chunkSize < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be at least 20 characters.");
        }

        if (chunkOverlap < 0 || chunkOverlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkOverlap),
                "Chunk overlap must be non-negative and smaller than the chunk size.");
        }
    }
}
