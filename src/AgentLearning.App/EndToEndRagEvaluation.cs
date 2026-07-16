namespace AgentLearning.App;

public sealed record EndToEndRagEvaluationResult(
    KnowledgeRetrievalEvaluationCase Case,
    IReadOnlyList<KnowledgeRetrievalMatch> RetrievedMatches,
    string Answer,
    KnowledgeCitationValidationResult CitationValidation,
    bool CitationRepairAttempted,
    GroundednessJudgment GroundednessJudgment)
{
    public bool RetrievalCorrect => string.IsNullOrWhiteSpace(Case.ExpectedSourcePath)
        ? RetrievedMatches.Count == 0
        : RetrievedMatches.FirstOrDefault()?.SourcePath == Case.ExpectedSourcePath;

    public bool CitationCorrect => CitationValidation.IsValid;

    public bool Grounded => GroundednessJudgment.Grounded;

    public bool Passed => RetrievalCorrect && CitationCorrect && Grounded;
}

public sealed record EndToEndRagEvaluationReport(
    IReadOnlyList<EndToEndRagEvaluationResult> Results)
{
    public int TotalCount => Results.Count;

    public int RetrievalCorrectCount => Results.Count(result => result.RetrievalCorrect);

    public int CitationCorrectCount => Results.Count(result => result.CitationCorrect);

    public int GroundedCount => Results.Count(result => result.Grounded);

    public int CitationRepairCount => Results.Count(result => result.CitationRepairAttempted);

    public int PassedCount => Results.Count(result => result.Passed);

    public double RetrievalAccuracy => CalculateRatio(RetrievalCorrectCount, TotalCount);

    public double CitationAccuracy => CalculateRatio(CitationCorrectCount, TotalCount);

    public double GroundednessRate => CalculateRatio(GroundedCount, TotalCount);

    public double CitationRepairRate => CalculateRatio(CitationRepairCount, TotalCount);

    public double PassRate => CalculateRatio(PassedCount, TotalCount);

    private static double CalculateRatio(int correctCount, int totalCount)
    {
        return totalCount == 0 ? 0 : correctCount / (double)totalCount;
    }
}
