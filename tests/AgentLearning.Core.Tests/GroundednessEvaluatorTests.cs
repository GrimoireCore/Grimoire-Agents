using AgentLearning.App;
using OpenAI.Chat;

namespace AgentLearning.Core.Tests;

public sealed class GroundednessEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_calculates_acceptance_and_rejection_metrics()
    {
        string evaluationFilePath = await CreateEvaluationFileAsync();
        FakeAgentChatClient client = new(CreateTextCompletion("""
            {
              "results": [
                {
                  "id": "grounded",
                  "grounded": true,
                  "score": 0.98,
                  "unsupported_claims": [],
                  "reason": "The answer is supported."
                },
                {
                  "id": "unsupported",
                  "grounded": false,
                  "score": 0.05,
                  "unsupported_claims": ["The answer reverses the reference."],
                  "reason": "The answer contradicts the reference."
                }
              ]
            }
            """));

        try
        {
            GroundednessEvaluator evaluator = new(client);

            GroundednessEvaluationReport report = await evaluator.EvaluateAsync(
                evaluationFilePath);

            Assert.Equal(2, report.CorrectCount);
            Assert.Equal(1, report.AcceptedGroundedCount);
            Assert.Equal(1, report.RejectedUnsupportedCount);
            Assert.Equal(1, report.Accuracy);
            Assert.Single(client.Requests);
            string judgeInput = ReadMessageText(client.Requests[0][1]);
            Assert.DoesNotContain("expected_grounded", judgeInput, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(evaluationFilePath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_rejects_non_json_model_output()
    {
        string evaluationFilePath = await CreateEvaluationFileAsync();
        FakeAgentChatClient client = new(CreateTextCompletion("```json\n{}\n```"));

        try
        {
            GroundednessEvaluator evaluator = new(client);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => evaluator.EvaluateAsync(evaluationFilePath));

            Assert.Contains("valid JSON", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(evaluationFilePath);
        }
    }

    [Fact]
    public async Task EvaluateAsync_rejects_inconsistent_grounded_judgment()
    {
        string evaluationFilePath = await CreateEvaluationFileAsync();
        FakeAgentChatClient client = new(CreateTextCompletion("""
            {
              "results": [
                {
                  "id": "grounded",
                  "grounded": true,
                  "score": 0.9,
                  "unsupported_claims": ["Unexpected claim."],
                  "reason": "Inconsistent result."
                },
                {
                  "id": "unsupported",
                  "grounded": false,
                  "score": 0.1,
                  "unsupported_claims": ["Contradiction."],
                  "reason": "Unsupported."
                }
              ]
            }
            """));

        try
        {
            GroundednessEvaluator evaluator = new(client);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => evaluator.EvaluateAsync(evaluationFilePath));

            Assert.Contains("cannot contain unsupported claims", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(evaluationFilePath);
        }
    }

    private static async Task<string> CreateEvaluationFileAsync()
    {
        string evaluationFilePath = Path.Combine(
            Path.GetTempPath(),
            $"groundedness-evaluation-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(evaluationFilePath, """
            [
              {
                "id": "grounded",
                "question": "What does the reference say?",
                "reference": "The harness pauses risky tools.",
                "answer": "The harness pauses risky tools.",
                "expected_grounded": true
              },
              {
                "id": "unsupported",
                "question": "What does the reference say?",
                "reference": "The harness pauses risky tools.",
                "answer": "The harness always runs risky tools.",
                "expected_grounded": false
              }
            ]
            """);
        return evaluationFilePath;
    }

    private static ChatCompletion CreateTextCompletion(string text)
    {
        return OpenAIChatModelFactory.ChatCompletion(
            "chatcmpl_groundedness",
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

    private static string ReadMessageText(ChatMessage message)
    {
        return string.Concat(message.Content.Select(part => part.Text));
    }

    private sealed class FakeAgentChatClient(ChatCompletion response) : IAgentChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatCompletion> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null)
        {
            Requests.Add(messages.ToArray());
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages)
        {
            throw new NotSupportedException("Streaming is not used by this test.");
        }
    }
}
