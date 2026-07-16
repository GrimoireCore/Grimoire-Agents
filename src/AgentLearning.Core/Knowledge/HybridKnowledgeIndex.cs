namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Combines semantic vector retrieval with exact keyword retrieval.
/// </summary>
public sealed class HybridKnowledgeIndex
{
    private const int MaximumResultCount = 10;
    private const double DefaultMinimumCombinedScore = 0.50;
    private const double VectorWeight = 0.70;
    private const double KeywordWeight = 0.30;

    private readonly KeywordKnowledgeIndex _keywordIndex;
    private readonly double _minimumCombinedScore;
    private readonly VectorKnowledgeIndex _vectorIndex;

    public HybridKnowledgeIndex(
        KeywordKnowledgeIndex keywordIndex,
        VectorKnowledgeIndex vectorIndex,
        double minimumCombinedScore = DefaultMinimumCombinedScore)
    {
        ArgumentNullException.ThrowIfNull(keywordIndex);
        ArgumentNullException.ThrowIfNull(vectorIndex);
        if (minimumCombinedScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCombinedScore),
                "Minimum combined score must be between 0 and 1.");
        }

        _keywordIndex = keywordIndex;
        _vectorIndex = vectorIndex;
        _minimumCombinedScore = minimumCombinedScore;
    }

    public async Task<IReadOnlyList<HybridKnowledgeSearchResult>> SearchAsync(
        string query,
        int maxResults = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > MaximumResultCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"Result count must be between 1 and {MaximumResultCount}.");
        }

        IReadOnlyList<KnowledgeSearchResult> vectorResults = await _vectorIndex.SearchAsync(
            query,
            MaximumResultCount,
            cancellationToken: cancellationToken);
        IReadOnlyList<KnowledgeSearchResult> keywordResults = _keywordIndex.Search(
            query,
            MaximumResultCount);
        Dictionary<ChunkKey, HybridCandidate> candidates = new();

        foreach (KnowledgeSearchResult result in vectorResults)
        {
            HybridCandidate candidate = GetOrAddCandidate(candidates, result.Chunk);
            candidate.VectorScore = result.Score;
        }

        double maximumKeywordScore = keywordResults.Count == 0
            ? 1
            : keywordResults.Max(result => result.Score);
        foreach (KnowledgeSearchResult result in keywordResults)
        {
            HybridCandidate candidate = GetOrAddCandidate(candidates, result.Chunk);
            candidate.KeywordScore = result.Score / maximumKeywordScore;
        }

        return candidates.Values
            .Select(candidate => new HybridKnowledgeSearchResult(
                candidate.Chunk,
                VectorWeight * candidate.VectorScore + KeywordWeight * candidate.KeywordScore,
                candidate.VectorScore,
                candidate.KeywordScore))
            .Where(result => result.Score >= _minimumCombinedScore)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Chunk.SourcePath, StringComparer.Ordinal)
            .ThenBy(result => result.Chunk.ChunkNumber)
            .Take(maxResults)
            .ToArray();
    }

    private static HybridCandidate GetOrAddCandidate(
        IDictionary<ChunkKey, HybridCandidate> candidates,
        KnowledgeChunk chunk)
    {
        ChunkKey key = new(chunk.SourcePath, chunk.ChunkNumber);
        if (!candidates.TryGetValue(key, out HybridCandidate? candidate))
        {
            candidate = new HybridCandidate(chunk);
            candidates.Add(key, candidate);
            return candidate;
        }

        if (!string.Equals(candidate.Chunk.Content, chunk.Content, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Knowledge retrievers returned different content for '{chunk.SourcePath}' chunk {chunk.ChunkNumber}.");
        }

        return candidate;
    }

    private readonly record struct ChunkKey(string SourcePath, int ChunkNumber);

    private sealed class HybridCandidate(KnowledgeChunk chunk)
    {
        public KnowledgeChunk Chunk { get; } = chunk;

        public double VectorScore { get; set; }

        public double KeywordScore { get; set; }
    }
}
