using AgentLearning.App;
using AgentLearning.Core.Skills;
using System.Text.Json;

namespace AgentLearning.Core.Tests;

public sealed class KnowledgeRetrievalEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_calculates_top1_recall_at3_and_no_answer_metrics()
    {
        string evaluationFilePath = Path.Combine(
            Path.GetTempPath(),
            $"rag-evaluation-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            evaluationFilePath,
            """
            [
              {
                "id": "top1",
                "question": "question one",
                "expected_source_path": "expected-one.md"
              },
              {
                "id": "top3",
                "question": "question two",
                "expected_source_path": "expected-two.md"
              },
              {
                "id": "no-answer",
                "question": "question three",
                "expected_source_path": null
              }
            ]
            """);
        AgentSkillRegistry registry = new([
            new FakeKnowledgeSearchSkill(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["question one"] = """
                    Knowledge search results:

                    [1] Source: expected-one.md (chunk 1)
                    Scores: combined=0.800, vector=0.750, keyword=0.917
                    Result one.
                    """,
                ["question two"] = """
                    Knowledge search results:

                    [1] Source: other.md (chunk 1)
                    Scores: combined=0.700, vector=0.700, keyword=0.700
                    Other result.

                    [2] Source: expected-two.md (chunk 1)
                    Scores: combined=0.650, vector=0.650, keyword=0.650
                    Expected result.
                    """,
                ["question three"] = "No relevant knowledge was found."
            })
        ]);

        try
        {
            KnowledgeRetrievalEvaluator evaluator = new(registry);

            KnowledgeRetrievalEvaluationReport report = await evaluator.EvaluateAsync(
                evaluationFilePath);

            Assert.Equal(2, report.AnswerCaseCount);
            Assert.Equal(1, report.Top1CorrectCount);
            Assert.Equal(0.5, report.Top1Accuracy);
            Assert.Equal(2, report.RecallAt3CorrectCount);
            Assert.Equal(1, report.RecallAt3);
            Assert.Equal(1, report.NoAnswerCorrectCount);
            Assert.Equal(1, report.NoAnswerAccuracy);
            Assert.Equal(0.8, report.Results[0].RetrievedMatches[0].CombinedScore);
            Assert.Equal(0.75, report.Results[0].RetrievedMatches[0].VectorScore);
            Assert.Equal(0.917, report.Results[0].RetrievedMatches[0].KeywordScore);
        }
        finally
        {
            File.Delete(evaluationFilePath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_rejects_unparseable_search_output()
    {
        string evaluationFilePath = Path.Combine(
            Path.GetTempPath(),
            $"rag-evaluation-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            evaluationFilePath,
            """
            [
              {
                "id": "invalid-output",
                "question": "question",
                "expected_source_path": "expected.md"
              }
            ]
            """);
        AgentSkillRegistry registry = new([
            new FakeKnowledgeSearchSkill(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["question"] = "Knowledge search results with an unexpected format."
            })
        ]);

        try
        {
            KnowledgeRetrievalEvaluator evaluator = new(registry);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => evaluator.EvaluateAsync(evaluationFilePath));

            Assert.Contains("parseable ranked results", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(evaluationFilePath);
        }
    }

    private sealed class FakeKnowledgeSearchSkill(
        IReadOnlyDictionary<string, string> responses) : IAgentSkill
    {
        public string Name => "search_knowledge";

        public string Description => "Search test knowledge.";

        public string ParametersJson => """{"type":"object"}""";

        public AgentSkillRiskLevel RiskLevel => AgentSkillRiskLevel.Low;

        public bool RequiresConfirmation => false;

        public Task<string> ExecuteAsync(
            string argumentsJson,
            AgentToolExecutionContext executionContext,
            CancellationToken cancellationToken = default)
        {
            using JsonDocument arguments = JsonDocument.Parse(argumentsJson);
            string query = arguments.RootElement.GetProperty("query").GetString()
                ?? throw new InvalidOperationException("Missing query.");
            return Task.FromResult(responses[query]);
        }
    }
}
