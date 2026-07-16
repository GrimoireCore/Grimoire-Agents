namespace AgentLearning.Core.Knowledge;

/// <summary>
/// One chunk with its combined, vector, and normalized keyword scores.
/// </summary>
public sealed record HybridKnowledgeSearchResult(
    KnowledgeChunk Chunk,
    double Score,
    double VectorScore,
    double KeywordScore);
