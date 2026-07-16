using System.Text.Json;

namespace AgentLearning.App;

public sealed record EndToEndRagRegressionBaseline(
    int FormatVersion,
    int MinimumCaseCount,
    IReadOnlyList<string> RequiredCaseIds,
    double MinimumRetrievalAccuracy,
    double MinimumCitationAccuracy,
    double MinimumGroundednessRate,
    double MinimumPassRate,
    double MaximumCitationRepairRate)
{
    public const int CurrentFormatVersion = 1;

    public static async Task<EndToEndRagRegressionBaseline> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("End-to-end RAG baseline file was not found.", filePath);
        }

        await using FileStream stream = File.OpenRead(filePath);
        EndToEndRagRegressionBaseline? baseline = await JsonSerializer.DeserializeAsync<
            EndToEndRagRegressionBaseline>(
            stream,
            EndToEndRagEvaluationJson.Options,
            cancellationToken);
        if (baseline is null)
        {
            throw new InvalidOperationException("End-to-end RAG baseline file is empty.");
        }

        baseline.Validate();
        return baseline;
    }

    private void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported end-to-end RAG baseline format version: {FormatVersion}.");
        }

        if (MinimumCaseCount <= 0)
        {
            throw new InvalidOperationException("Baseline minimum_case_count must be greater than zero.");
        }

        if (RequiredCaseIds is null
            || RequiredCaseIds.Count == 0
            || RequiredCaseIds.Any(string.IsNullOrWhiteSpace)
            || RequiredCaseIds.Distinct(StringComparer.Ordinal).Count() != RequiredCaseIds.Count)
        {
            throw new InvalidOperationException(
                "Baseline required_case_ids must contain unique non-empty ids.");
        }

        ValidateRate(MinimumRetrievalAccuracy, "minimum_retrieval_accuracy");
        ValidateRate(MinimumCitationAccuracy, "minimum_citation_accuracy");
        ValidateRate(MinimumGroundednessRate, "minimum_groundedness_rate");
        ValidateRate(MinimumPassRate, "minimum_pass_rate");
        ValidateRate(MaximumCitationRepairRate, "maximum_citation_repair_rate");
    }

    private static void ValidateRate(double value, string propertyName)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new InvalidOperationException(
                $"Baseline {propertyName} must be between 0 and 1.");
        }
    }
}
