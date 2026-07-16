using System.Text.Json.Serialization;

namespace AgentLearning.App;

public sealed record GroundednessEvaluationCase(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("expected_grounded")] bool ExpectedGrounded);

public sealed record GroundednessJudgment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("grounded")] bool Grounded,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("unsupported_claims")] IReadOnlyList<string> UnsupportedClaims,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record GroundednessEvaluationResult(
    GroundednessEvaluationCase Case,
    GroundednessJudgment Judgment)
{
    public bool IsCorrect => Case.ExpectedGrounded == Judgment.Grounded;
}

public sealed record GroundednessEvaluationReport(
    IReadOnlyList<GroundednessEvaluationResult> Results)
{
    public int TotalCount => Results.Count;

    public int CorrectCount => Results.Count(result => result.IsCorrect);

    public int GroundedCaseCount => Results.Count(result => result.Case.ExpectedGrounded);

    public int AcceptedGroundedCount => Results.Count(
        result => result.Case.ExpectedGrounded && result.Judgment.Grounded);

    public int UnsupportedCaseCount => Results.Count(result => !result.Case.ExpectedGrounded);

    public int RejectedUnsupportedCount => Results.Count(
        result => !result.Case.ExpectedGrounded && !result.Judgment.Grounded);

    public double Accuracy => CalculateRatio(CorrectCount, TotalCount);

    public double GroundedAcceptanceRate => CalculateRatio(
        AcceptedGroundedCount,
        GroundedCaseCount);

    public double UnsupportedRejectionRate => CalculateRatio(
        RejectedUnsupportedCount,
        UnsupportedCaseCount);

    private static double CalculateRatio(int correctCount, int totalCount)
    {
        return totalCount == 0 ? 0 : correctCount / (double)totalCount;
    }
}
