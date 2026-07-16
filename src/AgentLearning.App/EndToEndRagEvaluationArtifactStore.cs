using System.Text.Json;

namespace AgentLearning.App;

public static class EndToEndRagEvaluationArtifactStore
{
    public static async Task SaveAsync(
        string filePath,
        EndToEndRagEvaluationReport report,
        string model,
        string embeddingModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);

        EndToEndRagEvaluationArtifact artifact = CreateArtifact(
            report,
            model.Trim(),
            embeddingModel.Trim());
        string fullPath = Path.GetFullPath(filePath);
        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string tempFilePath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    artifact,
                    EndToEndRagEvaluationJson.Options,
                    cancellationToken);
            }

            File.Move(tempFilePath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private static EndToEndRagEvaluationArtifact CreateArtifact(
        EndToEndRagEvaluationReport report,
        string model,
        string embeddingModel)
    {
        EndToEndRagEvaluationArtifactCase[] cases = report.Results
            .Select(result => new EndToEndRagEvaluationArtifactCase(
                result.Case.Id,
                result.Case.Question,
                result.Case.ExpectedSourcePath,
                result.RetrievedMatches
                    .Select(match => new EndToEndRagEvaluationArtifactSource(
                        match.SourcePath,
                        match.ChunkNumber,
                        match.CombinedScore))
                    .ToArray(),
                result.Answer,
                result.RetrievalCorrect,
                result.CitationCorrect,
                result.CitationRepairAttempted,
                result.Grounded,
                result.GroundednessJudgment.Score,
                result.GroundednessJudgment.UnsupportedClaims,
                result.Passed))
            .ToArray();

        return new EndToEndRagEvaluationArtifact(
            FormatVersion: 1,
            GeneratedAt: DateTimeOffset.UtcNow,
            model,
            embeddingModel,
            new EndToEndRagEvaluationArtifactMetrics(
                report.TotalCount,
                report.RetrievalAccuracy,
                report.CitationAccuracy,
                report.GroundednessRate,
                report.CitationRepairRate,
                report.PassRate),
            cases);
    }
}

public sealed record EndToEndRagEvaluationArtifact(
    int FormatVersion,
    DateTimeOffset GeneratedAt,
    string Model,
    string EmbeddingModel,
    EndToEndRagEvaluationArtifactMetrics Metrics,
    IReadOnlyList<EndToEndRagEvaluationArtifactCase> Cases);

public sealed record EndToEndRagEvaluationArtifactMetrics(
    int CaseCount,
    double RetrievalAccuracy,
    double CitationAccuracy,
    double GroundednessRate,
    double CitationRepairRate,
    double PassRate);

public sealed record EndToEndRagEvaluationArtifactCase(
    string Id,
    string Question,
    string? ExpectedSourcePath,
    IReadOnlyList<EndToEndRagEvaluationArtifactSource> RetrievedSources,
    string Answer,
    bool RetrievalCorrect,
    bool CitationCorrect,
    bool CitationRepairAttempted,
    bool Grounded,
    double GroundednessScore,
    IReadOnlyList<string> UnsupportedClaims,
    bool Passed);

public sealed record EndToEndRagEvaluationArtifactSource(
    string SourcePath,
    int ChunkNumber,
    double CombinedScore);
