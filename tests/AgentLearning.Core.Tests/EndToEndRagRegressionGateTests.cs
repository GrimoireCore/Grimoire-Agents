using AgentLearning.App;

namespace AgentLearning.Core.Tests;

public sealed class EndToEndRagRegressionGateTests
{
    [Fact]
    public async Task LoadAsync_reads_and_validates_snake_case_baseline()
    {
        string filePath = Path.Combine(
            Path.GetTempPath(),
            $"e2e-rag-baseline-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(filePath, """
            {
              "format_version": 1,
              "minimum_case_count": 3,
              "required_case_ids": ["case-1", "case-2", "case-3"],
              "minimum_retrieval_accuracy": 1.0,
              "minimum_citation_accuracy": 1.0,
              "minimum_groundedness_rate": 1.0,
              "minimum_pass_rate": 1.0,
              "maximum_citation_repair_rate": 0.0
            }
            """);

        try
        {
            EndToEndRagRegressionBaseline baseline = await EndToEndRagRegressionBaseline.LoadAsync(
                filePath);

            Assert.Equal(3, baseline.MinimumCaseCount);
            Assert.Equal(1, baseline.MinimumPassRate);
            Assert.Equal(["case-1", "case-2", "case-3"], baseline.RequiredCaseIds);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Evaluate_passes_when_report_meets_every_baseline_requirement()
    {
        EndToEndRagEvaluationReport report = new([
            CreateResult("case-1"),
            CreateResult("case-2"),
            CreateResult("case-3")
        ]);
        EndToEndRagRegressionBaseline baseline = CreateBaseline();

        EndToEndRagRegressionGateResult result = EndToEndRagRegressionGate.Evaluate(
            report,
            baseline);

        Assert.True(result.Passed);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Evaluate_reports_missing_cases_metric_regressions_and_repair_growth()
    {
        EndToEndRagEvaluationReport report = new([
            CreateResult(
                "case-1",
                retrievalCorrect: false,
                citationCorrect: false,
                grounded: false,
                citationRepairAttempted: true)
        ]);
        EndToEndRagRegressionBaseline baseline = CreateBaseline();

        EndToEndRagRegressionGateResult result = EndToEndRagRegressionGate.Evaluate(
            report,
            baseline);

        Assert.False(result.Passed);
        Assert.Contains(result.Failures, failure => failure.Contains("Case count", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("case-2", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Retrieval accuracy", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Citation accuracy", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Groundedness rate", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("pass rate", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("repair rate", StringComparison.Ordinal));
    }

    private static EndToEndRagRegressionBaseline CreateBaseline()
    {
        return new EndToEndRagRegressionBaseline(
            FormatVersion: 1,
            MinimumCaseCount: 3,
            RequiredCaseIds: ["case-1", "case-2", "case-3"],
            MinimumRetrievalAccuracy: 1,
            MinimumCitationAccuracy: 1,
            MinimumGroundednessRate: 1,
            MinimumPassRate: 1,
            MaximumCitationRepairRate: 0);
    }

    internal static EndToEndRagEvaluationResult CreateResult(
        string id,
        bool retrievalCorrect = true,
        bool citationCorrect = true,
        bool grounded = true,
        bool citationRepairAttempted = false)
    {
        string expectedSource = "expected.md";
        string actualSource = retrievalCorrect ? expectedSource : "other.md";
        KnowledgeRetrievalEvaluationCase evaluationCase = new(
            id,
            "Test question",
            expectedSource);
        KnowledgeRetrievalMatch match = new(
            Rank: 1,
            actualSource,
            ChunkNumber: 1,
            CombinedScore: 0.9,
            VectorScore: 0.85,
            KeywordScore: 1);
        KnowledgeCitationValidationResult citationValidation = citationCorrect
            ? KnowledgeCitationValidationResult.Success()
            : KnowledgeCitationValidationResult.Failure("Missing citation.");
        GroundednessJudgment judgment = new(
            id,
            grounded,
            grounded ? 1 : 0,
            grounded ? [] : ["Unsupported claim."],
            grounded ? "Supported." : "Unsupported.");
        return new EndToEndRagEvaluationResult(
            evaluationCase,
            [match],
            "Test answer.",
            citationValidation,
            citationRepairAttempted,
            judgment);
    }
}
