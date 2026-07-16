using AgentLearning.App;
using AgentLearning.Core.Skills;
using OpenAI.Chat;

namespace AgentLearning.Core.Tests;

public sealed class AgentRunnerKnowledgeGroundingTests
{
    [Fact]
    public async Task RunAsync_sends_grounded_knowledge_context_to_the_model_after_search()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"agent-grounding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(),
            CreateTextCompletion("Harness 控制工具执行。[source: agent-harness.md, chunk 1]"));
        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            Path.Combine(tempDirectory, "memory.json"),
            new AgentSkillRegistry([new FakeKnowledgeSearchSkill()]));

        try
        {
            AgentRunResult result = await runner.RunAsync("Harness 有什么作用？");

            Assert.Equal(AgentRunOutcome.Completed, result.Outcome);
            Assert.Equal(2, chatClient.Requests.Count);
            ToolChatMessage toolMessage = Assert.Single(
                chatClient.Requests[1].OfType<ToolChatMessage>());
            string toolContent = string.Concat(toolMessage.Content.Select(part => part.Text));
            Assert.Contains("KNOWLEDGE GROUNDING RULES", toolContent, StringComparison.Ordinal);
            Assert.Contains("agent-harness.md (chunk 1)", toolContent, StringComparison.Ordinal);
            Assert.Contains("[source: <file>, chunk <number>]", toolContent, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_repairs_an_answer_that_omits_its_retrieved_source()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"agent-grounding-repair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(),
            CreateTextCompletion("Harness 控制工具执行。"),
            CreateTextCompletion("Harness 控制工具执行。[source: agent-harness.md, chunk 1]"));
        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            Path.Combine(tempDirectory, "memory.json"),
            new AgentSkillRegistry([new FakeKnowledgeSearchSkill()]));

        try
        {
            AgentRunResult result = await runner.RunAsync("Harness 有什么作用？");

            Assert.Contains("[source: agent-harness.md, chunk 1]", result.AssistantReply);
            Assert.Equal(3, result.FinalState.ModelRequestCount);
            Assert.Contains(
                result.WorkflowTrace.Steps,
                step => step.Kind == AgentLearning.Core.Workflow.AgentWorkflowStepKind.AnswerRejected);
            UserChatMessage repairMessage = Assert.IsType<UserChatMessage>(
                chatClient.Requests[2].Last());
            string repairContent = string.Concat(repairMessage.Content.Select(part => part.Text));
            Assert.Contains("HARNESS CITATION VALIDATION FAILED", repairContent, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_stops_after_one_failed_citation_repair()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"agent-grounding-failed-repair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(),
            CreateTextCompletion("第一次回答没有引用。"),
            CreateTextCompletion("第二次回答仍然没有引用。"));
        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            Path.Combine(tempDirectory, "memory.json"),
            new AgentSkillRegistry([new FakeKnowledgeSearchSkill()]));

        try
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => runner.RunAsync("Harness 有什么作用？"));

            Assert.Contains("after 1 repair attempt", exception.Message, StringComparison.Ordinal);
            Assert.Equal(3, chatClient.Requests.Count);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static AgentProfile CreateProfile()
    {
        return new AgentProfile(
            Name: "Test Agent",
            Model: "test-model",
            BaseUrl: "https://example.test/v1",
            EmbeddingBaseUrl: "http://127.0.0.1:1234/v1",
            EmbeddingModel: "test-embedding-model",
            EnvKey: "TEST_API_KEY",
            WireApi: "chat_completions",
            Stream: false,
            NativeToolCalling: true,
            ToolRouterEnabled: false,
            MaxToolsPerRequest: 2,
            ShowDebugRequests: false,
            ShowWorkflowTrace: false,
            MemoryFile: "memory.json",
            MaxMemoryTurns: 6,
            MaxMemoryContentChars: 2000,
            MaxToolIterations: 3,
            MaxToolResultChars: 1200,
            ToolTimeoutSeconds: 5,
            ApiKey: "test-key",
            Description: "Test agent.",
            Instructions: "Answer briefly.");
    }

    private static ChatCompletion CreateToolCallCompletion()
    {
        ChatToolCall toolCall = ChatToolCall.CreateFunctionToolCall(
            "call_search",
            KnowledgeGroundingPolicy.SearchToolName,
            BinaryData.FromString("{\"query\":\"Harness 有什么作用？\"}"));
        return OpenAIChatModelFactory.ChatCompletion(
            "chatcmpl_search",
            ChatFinishReason.ToolCalls,
            [],
            null,
            [toolCall],
            ChatMessageRole.Assistant,
            null,
            [],
            [],
            DateTimeOffset.Now,
            "test-model",
            null,
            null);
    }

    private static ChatCompletion CreateTextCompletion(string text)
    {
        return OpenAIChatModelFactory.ChatCompletion(
            "chatcmpl_answer",
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
                Scores: combined=0.754, vector=0.734, keyword=0.800
                Harness controls the model and tool loop.
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
