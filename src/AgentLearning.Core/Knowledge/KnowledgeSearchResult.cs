namespace AgentLearning.Core.Knowledge;

/// <summary>
/// A knowledge chunk and its relevance score for one query.
/// </summary>
public sealed record KnowledgeSearchResult(
    KnowledgeChunk Chunk,
    double Score);
