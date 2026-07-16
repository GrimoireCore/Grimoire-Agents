using AgentLearning.App;
using AgentLearning.Core.Skills;
using OpenAI.Chat;

namespace AgentLearning.Core.Tests;

public sealed class EndToEndRagEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_runs_retrieval_generation_repair_and_groundedness_judging()
    {
        string evaluationFilePath = Path.Combine(
            Path.GetTempPath(),
            $"e2e-rag-evaluation-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(evaluationFilePath, """
            [
              {
                "id": "harness",
                "question": "Harness 有什么作用？",
                "expected_source_path": "agent-harness.md"
              }
            ]
            """);
        FakeAgentChatClient client = new(
            CreateTextCompletion("Harness controls tool execution."),
            CreateTextCompletion(
                "Harness controls tool execution. [source: agent-harness.md, chunk 1]"),
            CreateTextCompletion("""
                {
                  "results": [
                    {
                      "id": "harness",
                      "grounded": true,
                      "score": 1.0,
                      "unsupported_claims": [],
                      "reason": "The answer is fully supported."
                    }
                  ]
                }
                """));
        AgentSkillRegistry registry = new([new FakeKnowledgeSearchSkill()]);

        try
        {
            EndToEndRagEvaluator evaluator = new(registry, client);

            EndToEndRagEvaluationReport report = await evaluator.EvaluateAsync(
                evaluationFilePath);

            EndToEndRagEvaluationResult result = Assert.Single(report.Results);
            Assert.True(result.RetrievalCorrect);
            Assert.True(result.CitationCorrect);
            Assert.True(result.CitationRepairAttempted);
            Assert.True(result.Grounded);
            Assert.True(result.Passed);
            Assert.Equal(1, report.PassRate);
            Assert.Equal(3, client.Requests.Count);
        }
        finally
        {
            File.Delete(evaluationFilePath);
        }
    }

    private static ChatCompletion CreateTextCompletion(string text)
    {
        return OpenAIChatModelFactory.ChatCompletion(
            $"chatcmpl_{Guid.NewGuid():N}",
            ChatFinishReason.Stop,
            [ChatMessageContentPart.CreateTextPart(text)],
            null,
            [],
            ChatMessageRole.Assistant,
            null,
            [],
            [],
            DateTimeOffset.Now,
            "test-model",
            null,
            null);
    }

    private sealed class FakeKnowledgeSearchSkill : IAgentSkill
    {
        public string Name => KnowledgeGroundingPolicy.SearchToolName;

        public string Description => "Search test knowledge.";

        public string ParametersJson => """{"type":"object"}""";

        public AgentSkillRiskLevel RiskLevel => AgentSkillRiskLevel.Low;

        public bool RequiresConfirmation => false;

        public Task<string> ExecuteAsync(
            string argumentsJson,
            AgentToolExecutionContext executionContext,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("""
                Knowledge search results:

                [1] Source: agent-harness.md (chunk 1)
                Scores: combined=0.900, vector=0.860, keyword=1.000
                Harness controls tool execution.
                """);
        }
    }

    private sealed class FakeAgentChatClient(params ChatCompletion[] responses) : IAgentChatClient
    {
        private readonly Queue<ChatCompletion> _responses = new(responses);

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatCompletion> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null)
        {
            Requests.Add(messages.ToArray());
            return Task.FromResult(_responses.Dequeue());
        }

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages)
        {
            throw new NotSupportedException("Streaming is not used by this test.");
        }
    }
}
