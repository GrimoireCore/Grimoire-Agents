using AgentLearning.App;
using System.Text.Json;

namespace AgentLearning.Core.Tests;

public sealed class EndToEndRagEvaluationArtifactStoreTests
{
    [Fact]
    public async Task SaveAsync_writes_an_atomic_machine_readable_latest_report()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"e2e-rag-artifact-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "nested", "latest.json");
        EndToEndRagEvaluationReport report = new([
            EndToEndRagRegressionGateTests.CreateResult("case-1")
        ]);

        try
        {
            await EndToEndRagEvaluationArtifactStore.SaveAsync(
                filePath,
                report,
                "test-model",
                "test-embedding-model");

            Assert.True(File.Exists(filePath));
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            JsonElement root = document.RootElement;
            Assert.Equal(1, root.GetProperty("format_version").GetInt32());
            Assert.Equal("test-model", root.GetProperty("model").GetString());
            Assert.Equal(
                1,
                root.GetProperty("metrics").GetProperty("case_count").GetInt32());
            Assert.Equal(
                "case-1",
                root.GetProperty("cases")[0].GetProperty("id").GetString());
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(filePath)!,
                "*.tmp",
                SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }
}
