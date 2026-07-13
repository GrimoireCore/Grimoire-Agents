using AgentLearning.Core.Knowledge;

namespace AgentLearning.Core.Tests;

public sealed class KeywordKnowledgeIndexTests
{
    [Fact]
    public async Task Search_returns_the_most_relevant_markdown_chunk()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "refund-policy.md"),
                "购买超过七天后，退款请求需要人工审核。");
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "office-hours.md"),
                "客服工作时间是工作日上午九点到下午五点。");
            KeywordKnowledgeIndex index = await KeywordKnowledgeIndex.LoadFromDirectoryAsync(
                directoryPath,
                chunkSize: 40,
                chunkOverlap: 10);

            IReadOnlyList<KnowledgeSearchResult> results = index.Search("超过七天退款怎么办");

            KnowledgeSearchResult firstResult = Assert.IsType<KnowledgeSearchResult>(results[0]);
            Assert.Equal("refund-policy.md", firstResult.Chunk.SourcePath);
            Assert.Contains("人工审核", firstResult.Chunk.Content);
            Assert.True(firstResult.Score > 0);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_returns_empty_when_no_keyword_matches()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "agent.md"),
                "Agent Harness controls tool execution and approval.");
            KeywordKnowledgeIndex index = await KeywordKnowledgeIndex.LoadFromDirectoryAsync(directoryPath);

            IReadOnlyList<KnowledgeSearchResult> results = index.Search("量子纠缠实验");

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_rejects_directory_without_markdown_files()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => KeywordKnowledgeIndex.LoadFromDirectoryAsync(directoryPath));

            Assert.Contains("contains no Markdown files", exception.Message);
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
            $"keyword-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }
}
