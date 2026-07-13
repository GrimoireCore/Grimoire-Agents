namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Retrieves Markdown chunks by comparing normalized embedding vectors.
/// </summary>
public sealed class VectorKnowledgeIndex
{
    private const int MaximumResultCount = 10;
    private const double DefaultMinimumSimilarity = 0.45;

    private readonly ITextEmbeddingClient _embeddingClient;
    private readonly IReadOnlyList<VectorKnowledgeChunk> _chunks;
    private readonly int _embeddingDimensions;

    private VectorKnowledgeIndex(
        ITextEmbeddingClient embeddingClient,
        IReadOnlyList<VectorKnowledgeChunk> chunks,
        int embeddingDimensions,
        bool loadedFromCache)
    {
        _embeddingClient = embeddingClient;
        _chunks = chunks;
        _embeddingDimensions = embeddingDimensions;
        LoadedFromCache = loadedFromCache;
    }

    public int ChunkCount => _chunks.Count;

    public int EmbeddingDimensions => _embeddingDimensions;

    public bool LoadedFromCache { get; }

    public static async Task<VectorKnowledgeIndex> LoadFromDirectoryAsync(
        string directoryPath,
        ITextEmbeddingClient embeddingClient,
        int chunkSize = MarkdownKnowledgeLoader.DefaultChunkSize,
        int chunkOverlap = MarkdownKnowledgeLoader.DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embeddingClient);

        IReadOnlyList<KnowledgeChunk> chunks = await MarkdownKnowledgeLoader.LoadFromDirectoryAsync(
            directoryPath,
            chunkSize,
            chunkOverlap,
            cancellationToken);
        return await CreateFromChunksAsync(
            chunks,
            embeddingClient,
            loadedFromCache: false,
            cancellationToken);
    }

    public static async Task<VectorKnowledgeIndex> LoadOrCreateAsync(
        string directoryPath,
        string cacheFilePath,
        string embeddingModel,
        ITextEmbeddingClient embeddingClient,
        int chunkSize = MarkdownKnowledgeLoader.DefaultChunkSize,
        int chunkOverlap = MarkdownKnowledgeLoader.DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentNullException.ThrowIfNull(embeddingClient);

        IReadOnlyList<KnowledgeChunk> chunks = await MarkdownKnowledgeLoader.LoadFromDirectoryAsync(
            directoryPath,
            chunkSize,
            chunkOverlap,
            cancellationToken);
        string documentFingerprint = VectorKnowledgeIndexCacheStore.BuildDocumentFingerprint(chunks);
        VectorKnowledgeIndexCache? cache = await VectorKnowledgeIndexCacheStore.LoadAsync(
            cacheFilePath,
            cancellationToken);

        if (cache is not null && IsCurrentCache(
                cache,
                embeddingModel.Trim(),
                chunkSize,
                chunkOverlap,
                documentFingerprint))
        {
            return CreateFromCache(chunks, embeddingClient, cache);
        }

        VectorKnowledgeIndex index = await CreateFromChunksAsync(
            chunks,
            embeddingClient,
            loadedFromCache: false,
            cancellationToken);
        VectorKnowledgeIndexCache newCache = index.CreateCache(
            embeddingModel.Trim(),
            chunkSize,
            chunkOverlap,
            documentFingerprint);
        await VectorKnowledgeIndexCacheStore.SaveAsync(
            cacheFilePath,
            newCache,
            cancellationToken);

        return index;
    }

    private static async Task<VectorKnowledgeIndex> CreateFromChunksAsync(
        IReadOnlyList<KnowledgeChunk> chunks,
        ITextEmbeddingClient embeddingClient,
        bool loadedFromCache,
        CancellationToken cancellationToken)
    {
        string[] inputs = chunks.Select(chunk => chunk.Content).ToArray();
        IReadOnlyList<float[]> embeddings = await embeddingClient.CreateEmbeddingsAsync(
            inputs,
            cancellationToken);

        ValidateEmbeddingCount(embeddings, chunks.Count);
        int embeddingDimensions = embeddings[0].Length;
        VectorKnowledgeChunk[] vectorChunks = chunks
            .Select((chunk, index) => new VectorKnowledgeChunk(
                chunk,
                NormalizeEmbedding(embeddings[index], embeddingDimensions)))
            .ToArray();

        return new VectorKnowledgeIndex(
            embeddingClient,
            vectorChunks,
            embeddingDimensions,
            loadedFromCache);
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int maxResults = 3,
        double minimumSimilarity = DefaultMinimumSimilarity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > MaximumResultCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"Result count must be between 1 and {MaximumResultCount}.");
        }

        if (minimumSimilarity is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSimilarity),
                "Minimum similarity must be between -1 and 1.");
        }

        IReadOnlyList<float[]> queryEmbeddings = await _embeddingClient.CreateEmbeddingsAsync(
            [query.Trim()],
            cancellationToken);
        ValidateEmbeddingCount(queryEmbeddings, expectedCount: 1);
        float[] normalizedQuery = NormalizeEmbedding(
            queryEmbeddings[0],
            _embeddingDimensions);

        return _chunks
            .Select(chunk => new KnowledgeSearchResult(
                chunk.Chunk,
                CalculateDotProduct(normalizedQuery, chunk.NormalizedEmbedding)))
            .Where(result => result.Score >= minimumSimilarity)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Chunk.SourcePath, StringComparer.Ordinal)
            .ThenBy(result => result.Chunk.ChunkNumber)
            .Take(maxResults)
            .ToArray();
    }

    private static void ValidateEmbeddingCount(
        IReadOnlyList<float[]> embeddings,
        int expectedCount)
    {
        if (embeddings.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Embedding service returned {embeddings.Count} vectors for {expectedCount} inputs.");
        }

        if (embeddings.Any(embedding => embedding.Length == 0))
        {
            throw new InvalidOperationException("Embedding service returned an empty vector.");
        }
    }

    private static bool IsCurrentCache(
        VectorKnowledgeIndexCache cache,
        string embeddingModel,
        int chunkSize,
        int chunkOverlap,
        string documentFingerprint)
    {
        return cache.FormatVersion == VectorKnowledgeIndexCacheStore.CurrentFormatVersion
            && string.Equals(cache.EmbeddingModel, embeddingModel, StringComparison.Ordinal)
            && cache.ChunkSize == chunkSize
            && cache.ChunkOverlap == chunkOverlap
            && string.Equals(
                cache.DocumentFingerprint,
                documentFingerprint,
                StringComparison.Ordinal);
    }

    private static VectorKnowledgeIndex CreateFromCache(
        IReadOnlyList<KnowledgeChunk> currentChunks,
        ITextEmbeddingClient embeddingClient,
        VectorKnowledgeIndexCache cache)
    {
        if (cache.EmbeddingDimensions <= 0 || cache.Chunks is null)
        {
            throw new InvalidOperationException("Vector index cache metadata is invalid.");
        }

        if (cache.Chunks.Count != currentChunks.Count)
        {
            throw new InvalidOperationException("Vector index cache chunk count is invalid.");
        }

        VectorKnowledgeChunk[] vectorChunks = new VectorKnowledgeChunk[currentChunks.Count];
        for (int index = 0; index < currentChunks.Count; index++)
        {
            KnowledgeChunk currentChunk = currentChunks[index];
            VectorKnowledgeIndexCacheEntry cachedChunk = cache.Chunks[index];
            if (!string.Equals(cachedChunk.SourcePath, currentChunk.SourcePath, StringComparison.Ordinal)
                || cachedChunk.ChunkNumber != currentChunk.ChunkNumber
                || !string.Equals(cachedChunk.Content, currentChunk.Content, StringComparison.Ordinal)
                || cachedChunk.Embedding is null)
            {
                throw new InvalidOperationException(
                    $"Vector index cache chunk #{index + 1} does not match the current document content.");
            }

            vectorChunks[index] = new VectorKnowledgeChunk(
                currentChunk,
                NormalizeEmbedding(cachedChunk.Embedding, cache.EmbeddingDimensions));
        }

        return new VectorKnowledgeIndex(
            embeddingClient,
            vectorChunks,
            cache.EmbeddingDimensions,
            loadedFromCache: true);
    }

    private VectorKnowledgeIndexCache CreateCache(
        string embeddingModel,
        int chunkSize,
        int chunkOverlap,
        string documentFingerprint)
    {
        VectorKnowledgeIndexCacheEntry[] entries = _chunks
            .Select(chunk => new VectorKnowledgeIndexCacheEntry(
                chunk.Chunk.SourcePath,
                chunk.Chunk.ChunkNumber,
                chunk.Chunk.Content,
                chunk.NormalizedEmbedding))
            .ToArray();

        return new VectorKnowledgeIndexCache(
            VectorKnowledgeIndexCacheStore.CurrentFormatVersion,
            embeddingModel,
            chunkSize,
            chunkOverlap,
            documentFingerprint,
            _embeddingDimensions,
            entries);
    }

    private static float[] NormalizeEmbedding(float[] embedding, int expectedDimensions)
    {
        if (embedding.Length != expectedDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimension changed from {expectedDimensions} to {embedding.Length}.");
        }

        double sumOfSquares = 0;
        foreach (float value in embedding)
        {
            sumOfSquares += value * value;
        }

        double length = Math.Sqrt(sumOfSquares);
        if (length == 0)
        {
            throw new InvalidOperationException("Embedding service returned a zero-length vector.");
        }

        return embedding.Select(value => (float)(value / length)).ToArray();
    }

    private static double CalculateDotProduct(
        IReadOnlyList<float> first,
        IReadOnlyList<float> second)
    {
        double sum = 0;
        for (int index = 0; index < first.Count; index++)
        {
            sum += first[index] * second[index];
        }

        return sum;
    }

    private sealed record VectorKnowledgeChunk(
        KnowledgeChunk Chunk,
        float[] NormalizedEmbedding);
}
