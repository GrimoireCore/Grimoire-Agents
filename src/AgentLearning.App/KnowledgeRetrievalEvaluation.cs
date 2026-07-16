using System.Text.Json.Serialization;

namespace AgentLearning.App;

/// <summary>
/// One expected retrieval outcome in the RAG evaluation dataset.
/// </summary>
public sealed record KnowledgeRetrievalEvaluationCase(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("expected_source_path")] string? ExpectedSourcePath);

/// <summary>
/// One ranked chunk returned by the knowledge search tool.
/// </summary>
public sealed record KnowledgeRetrievalMatch(
    int Rank,
    string SourcePath,
    int ChunkNumber,
    double CombinedScore,
    double VectorScore,
    double KeywordScore);

/// <summary>
/// The observed ranking for one evaluation case.
/// </summary>
public sealed record KnowledgeRetrievalEvaluationResult(
    KnowledgeRetrievalEvaluationCase Case,
    IReadOnlyList<KnowledgeRetrievalMatch> RetrievedMatches)
{
    public bool ExpectsNoAnswer => string.IsNullOrWhiteSpace(Case.ExpectedSourcePath);

    public bool Top1Correct => !ExpectsNoAnswer
        && RetrievedMatches.FirstOrDefault()?.SourcePath == Case.ExpectedSourcePath;

    public bool RecallAt3Correct => !ExpectsNoAnswer
        && RetrievedMatches
            .Take(3)
            .Any(match => match.SourcePath == Case.ExpectedSourcePath);

    public bool NoAnswerCorrect => ExpectsNoAnswer && RetrievedMatches.Count == 0;
}

/// <summary>
/// Aggregate metrics and case details for one evaluation run.
/// </summary>
public sealed record KnowledgeRetrievalEvaluationReport(
    IReadOnlyList<KnowledgeRetrievalEvaluationResult> Results)
{
    public int AnswerCaseCount => Results.Count(result => !result.ExpectsNoAnswer);

    public int NoAnswerCaseCount => Results.Count(result => result.ExpectsNoAnswer);

    public int Top1CorrectCount => Results.Count(result => result.Top1Correct);

    public int RecallAt3CorrectCount => Results.Count(result => result.RecallAt3Correct);

    public int NoAnswerCorrectCount => Results.Count(result => result.NoAnswerCorrect);

    public double Top1Accuracy => CalculateRatio(Top1CorrectCount, AnswerCaseCount);

    public double RecallAt3 => CalculateRatio(RecallAt3CorrectCount, AnswerCaseCount);

    public double NoAnswerAccuracy => CalculateRatio(NoAnswerCorrectCount, NoAnswerCaseCount);

    private static double CalculateRatio(int correctCount, int totalCount)
    {
        return totalCount == 0 ? 0 : correctCount / (double)totalCount;
    }
}
