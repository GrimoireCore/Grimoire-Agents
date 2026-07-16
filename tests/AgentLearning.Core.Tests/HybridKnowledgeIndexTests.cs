using AgentLearning.Core.Knowledge;

namespace AgentLearning.Core.Tests;

public sealed class HybridKnowledgeIndexTests
{
    [Fact]
    public async Task SearchAsync_uses_vector_score_for_semantically_related_text()
    {
        string directoryPath = await CreateKnowledgeDirectoryAsync();
        try
        {
            HybridKnowledgeIndex index = await CreateIndexAsync(
                directoryPath,
                documentEmbeddings: [[1, 0], [0, 1]],
                queryEmbedding: [0.95f, 0.05f]);

            IReadOnlyList<HybridKnowledgeSearchResult> results = await index.SearchAsync(
                "怎样避免系统擅自运行敏感功能？");

            Assert.Equal("approval.md", results[0].Chunk.SourcePath);
            Assert.True(results[0].VectorScore > 0.9);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_keyword_score_breaks_equal_vector_score_tie()
    {
        string directoryPath = await CreateKnowledgeDirectoryAsync();
        try
        {
            HybridKnowledgeIndex index = await CreateIndexAsync(
                directoryPath,
                documentEmbeddings: [[1, 0], [1, 0]],
                queryEmbedding: [1, 0]);

            IReadOnlyList<HybridKnowledgeSearchResult> results = await index.SearchAsync(
                "E401 怎么处理？");

            Assert.Equal("error.md", results[0].Chunk.SourcePath);
            Assert.Equal(1, results[0].KeywordScore);
            Assert.True(results[0].Score > results[1].Score);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_rejects_candidates_below_minimum_combined_score()
    {
        string directoryPath = await CreateKnowledgeDirectoryAsync();
        try
        {
            HybridKnowledgeIndex index = await CreateIndexAsync(
                directoryPath,
                documentEmbeddings: [[0.60f, 0.80f], [0.50f, 0.8660254f]],
                queryEmbedding: [1, 0]);

            IReadOnlyList<HybridKnowledgeSearchResult> results = await index.SearchAsync(
                "completely unrelated topic");

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static async Task<HybridKnowledgeIndex> CreateIndexAsync(
        string directoryPath,
        IReadOnlyList<float[]> documentEmbeddings,
        float[] queryEmbedding)
    {
        KeywordKnowledgeIndex keywordIndex = await KeywordKnowledgeIndex.LoadFromDirectoryAsync(
            directoryPath);
        QueueEmbeddingClient embeddingClient = new(
        [
            documentEmbeddings,
            [queryEmbedding]
        ]);
        VectorKnowledgeIndex vectorIndex = await VectorKnowledgeIndex.LoadFromDirectoryAsync(
            directoryPath,
            embeddingClient);
        return new HybridKnowledgeIndex(keywordIndex, vectorIndex);
    }

    private static async Task<string> CreateKnowledgeDirectoryAsync()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"hybrid-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(directoryPath, "approval.md"),
            "人工确认用于阻止高风险工具直接执行。");
        await File.WriteAllTextAsync(
            Path.Combine(directoryPath, "error.md"),
            "错误码 E401 表示凭证已经失效。");
        return directoryPath;
    }

    private sealed class QueueEmbeddingClient(
        IEnumerable<IReadOnlyList<float[]>> responses) : ITextEmbeddingClient
    {
        private readonly Queue<IReadOnlyList<float[]>> _responses = new(responses);

        public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
