namespace AgentLearning.App;

public static class EndToEndRagRegressionGate
{
    public static EndToEndRagRegressionGateResult Evaluate(
        EndToEndRagEvaluationReport report,
        EndToEndRagRegressionBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(baseline);
        List<string> failures = [];

        if (report.TotalCount < baseline.MinimumCaseCount)
        {
            failures.Add(
                $"Case count {report.TotalCount} is below required minimum {baseline.MinimumCaseCount}.");
        }

        HashSet<string> actualCaseIds = report.Results
            .Select(result => result.Case.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string requiredCaseId in baseline.RequiredCaseIds)
        {
            if (!actualCaseIds.Contains(requiredCaseId))
            {
                failures.Add($"Required evaluation case is missing: {requiredCaseId}.");
            }
        }

        AddMinimumFailure(
            failures,
            "Retrieval accuracy",
            report.RetrievalAccuracy,
            baseline.MinimumRetrievalAccuracy);
        AddMinimumFailure(
            failures,
            "Citation accuracy",
            report.CitationAccuracy,
            baseline.MinimumCitationAccuracy);
        AddMinimumFailure(
            failures,
            "Groundedness rate",
            report.GroundednessRate,
            baseline.MinimumGroundednessRate);
        AddMinimumFailure(
            failures,
            "End-to-end pass rate",
            report.PassRate,
            baseline.MinimumPassRate);

        if (report.CitationRepairRate > baseline.MaximumCitationRepairRate)
        {
            failures.Add(
                $"Citation repair rate {report.CitationRepairRate:P0} exceeds maximum {baseline.MaximumCitationRepairRate:P0}.");
        }

        return new EndToEndRagRegressionGateResult(failures.Count == 0, failures);
    }

    private static void AddMinimumFailure(
        ICollection<string> failures,
        string metricName,
        double actual,
        double minimum)
    {
        if (actual < minimum)
        {
            failures.Add($"{metricName} {actual:P0} is below minimum {minimum:P0}.");
        }
    }
}

public sealed record EndToEndRagRegressionGateResult(
    bool Passed,
    IReadOnlyList<string> Failures);
