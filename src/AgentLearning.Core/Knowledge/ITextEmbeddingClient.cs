namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Converts text inputs into numeric embedding vectors.
/// </summary>
public interface ITextEmbeddingClient
{
    Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
