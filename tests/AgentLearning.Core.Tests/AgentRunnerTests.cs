using AgentLearning.App;
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;
using OpenAI.Chat;

namespace AgentLearning.Core.Tests;

public sealed class AgentRunnerTests
{
    [Fact]
    public async Task RunAsync_executes_tool_and_returns_final_answer()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"agent-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            FakeAgentChatClient chatClient = new(
                CreateToolCallCompletion(),
                CreateTextCompletion("The result is 20."));
            AgentRunner runner = new(
                CreateProfile(),
                chatClient,
                new ChatMemory(),
                Path.Combine(tempDirectory, "memory.json"),
                new AgentSkillRegistry([new CalculatorSkill()]));
            List<AgentWorkflowStepKind> observedSteps = [];
            runner.WorkflowStepCreated += step => observedSteps.Add(step.Kind);

            AgentRunResult result = await runner.RunAsync("What is (2 + 3) * 4?");

            Assert.Equal("The result is 20.", result.AssistantReply);
            Assert.Equal(AgentRunStatus.Finished, result.FinalState.Status);
            Assert.Equal(2, result.FinalState.ModelRequestCount);
            Assert.Equal(1, result.FinalState.ToolCallCount);
            Assert.Contains(AgentWorkflowStepKind.RouteTools, observedSteps);
            Assert.Equal(2, observedSteps.Count(step => step == AgentWorkflowStepKind.AskModel));
            Assert.Contains(AgentWorkflowStepKind.ToolRequested, observedSteps);
            Assert.Contains(AgentWorkflowStepKind.ToolExecuted, observedSteps);
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
            "call_calculate",
            "calculate",
            BinaryData.FromString("{\"expression\":\"(2 + 3) * 4\"}"));

        return OpenAIChatModelFactory.ChatCompletion(
            "chatcmpl_tool",
            ChatFinishReason.ToolCalls,
            [],
            null,
            [toolCall],
            ChatMessageRole.Assistant,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "test-model",
            null,
            OpenAIChatModelFactory.ChatTokenUsage(3, 8, 11, null));
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
            DateTimeOffset.UtcNow,
            "test-model",
            null,
            OpenAIChatModelFactory.ChatTokenUsage(4, 10, 14, null));
    }

    private sealed class FakeAgentChatClient(params ChatCompletion[] responses) : IAgentChatClient
    {
        private readonly Queue<ChatCompletion> _responses = new(responses);

        public Task<ChatCompletion> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null)
        {
            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
