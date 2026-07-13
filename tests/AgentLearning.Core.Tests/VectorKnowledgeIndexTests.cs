using AgentLearning.Core.Knowledge;
using System.Text.Json;

namespace AgentLearning.Core.Tests;

public sealed class VectorKnowledgeIndexTests
{
    [Fact]
    public async Task SearchAsync_returns_chunk_with_highest_cosine_similarity()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "approval.md"),
                "Harness requires approval before risky tools execute.");
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "weather.md"),
                "Sunny weather is suitable for walking outside.");
            QueueEmbeddingClient embeddingClient = new(
            [
                [[1, 0], [0, 1]],
                [[0.9f, 0.1f]]
            ]);
            VectorKnowledgeIndex index = await VectorKnowledgeIndex.LoadFromDirectoryAsync(
                directoryPath,
                embeddingClient);

            IReadOnlyList<KnowledgeSearchResult> results = await index.SearchAsync(
                "How are dangerous tools controlled?");

            KnowledgeSearchResult result = Assert.Single(results);
            Assert.Equal("approval.md", result.Chunk.SourcePath);
            Assert.True(result.Score > 0.9);
            Assert.Equal(2, index.EmbeddingDimensions);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_rejects_query_vector_with_changed_dimensions()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            QueueEmbeddingClient embeddingClient = new(
            [
                [[1, 0]],
                [[1, 0, 0]]
            ]);
            VectorKnowledgeIndex index = await VectorKnowledgeIndex.LoadFromDirectoryAsync(
                directoryPath,
                embeddingClient);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => index.SearchAsync("Agent question"));

            Assert.Contains("dimension changed", exception.Message);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_second_load_uses_persisted_vectors()
    {
        string directoryPath = CreateTempDirectory();
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            QueueEmbeddingClient firstClient = new([[[1, 0]]]);

            VectorKnowledgeIndex firstIndex = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                firstClient);
            QueueEmbeddingClient secondClient = new([]);
            VectorKnowledgeIndex secondIndex = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                secondClient);

            Assert.False(firstIndex.LoadedFromCache);
            Assert.True(secondIndex.LoadedFromCache);
            Assert.Equal(1, firstClient.CallCount);
            Assert.Equal(0, secondClient.CallCount);
            Assert.True(File.Exists(cacheFilePath));

            using JsonDocument cache = JsonDocument.Parse(await File.ReadAllTextAsync(cacheFilePath));
            Assert.Equal("embedding-model-v1", cache.RootElement.GetProperty("embedding_model").GetString());
            Assert.Equal(1, cache.RootElement.GetProperty("chunks").GetArrayLength());
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_document_change_rebuilds_cache()
    {
        string directoryPath = CreateTempDirectory();
        string documentPath = Path.Combine(directoryPath, "agent.md");
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(documentPath, "Original agent knowledge.");
            await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                new QueueEmbeddingClient([[[1, 0]]]));
            await File.WriteAllTextAsync(documentPath, "Updated agent knowledge.");
            QueueEmbeddingClient updatedClient = new([[[0, 1]]]);

            VectorKnowledgeIndex updatedIndex = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                updatedClient);

            Assert.False(updatedIndex.LoadedFromCache);
            Assert.Equal(1, updatedClient.CallCount);
            Assert.Contains("Updated agent knowledge", await File.ReadAllTextAsync(cacheFilePath));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_embedding_model_change_rebuilds_cache()
    {
        string directoryPath = CreateTempDirectory();
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                new QueueEmbeddingClient([[[1, 0]]]));
            QueueEmbeddingClient newModelClient = new([[[0, 1]]]);

            VectorKnowledgeIndex index = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v2",
                newModelClient);

            Assert.False(index.LoadedFromCache);
            Assert.Equal(1, newModelClient.CallCount);
            Assert.Contains("embedding-model-v2", await File.ReadAllTextAsync(cacheFilePath));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_invalid_cache_json_fails_clearly()
    {
        string directoryPath = CreateTempDirectory();
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            await File.WriteAllTextAsync(cacheFilePath, "{invalid-json");

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => VectorKnowledgeIndex.LoadOrCreateAsync(
                    directoryPath,
                    cacheFilePath,
                    "embedding-model-v1",
                    new QueueEmbeddingClient([])));

            Assert.Contains("contains invalid JSON", exception.Message);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"vector-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private sealed class QueueEmbeddingClient(
        IEnumerable<IReadOnlyList<float[]>> responses) : ITextEmbeddingClient
    {
        private readonly Queue<IReadOnlyList<float[]>> _responses = new(responses);

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected embedding request in test.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
