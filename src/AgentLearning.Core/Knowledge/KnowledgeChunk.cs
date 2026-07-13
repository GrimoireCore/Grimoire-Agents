namespace AgentLearning.Core.Knowledge;

/// <summary>
/// One searchable piece of a source document.
/// </summary>
public sealed record KnowledgeChunk(
    string SourcePath,
    int ChunkNumber,
    string Content);
