using AgentLearning.App;
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;
using OpenAI.Chat;

namespace AgentLearning.Core.Tests;

public sealed class AgentRunnerExecutionTraceTests
{
    [Fact]
    public async Task RunAsync_publishes_model_tool_and_token_telemetry()
    {
        string tempDirectory = CreateTempDirectory();
        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(),
            CreateTextCompletion("The result is 4."));
        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            Path.Combine(tempDirectory, "memory.json"),
            new AgentSkillRegistry([new CalculatorSkill()]));
        AgentExecutionTrace? capturedTrace = null;
        runner.ExecutionTraceCompletedAsync = trace =>
        {
            capturedTrace = trace;
            return Task.CompletedTask;
        };

        try
        {
            AgentRunResult result = await runner.RunAsync("What is 2 + 2?");

            AgentExecutionTrace trace = Assert.IsType<AgentExecutionTrace>(capturedTrace);
            Assert.Equal(result.RunId, trace.RunId);
            Assert.StartsWith("trace_", trace.TraceId, StringComparison.Ordinal);
            Assert.Equal("run", trace.Operation);
            Assert.Equal(AgentRunOutcome.Completed, trace.Outcome);
            Assert.Equal(AgentRunStatus.Finished, trace.FinalState.Status);
            Assert.Equal(2, trace.ModelCalls.Count);
            Assert.All(trace.ModelCalls, call => Assert.Equal("main_agent", call.Stage));
            Assert.All(trace.ModelCalls, call => Assert.True(call.Succeeded));
            Assert.Equal(2, trace.TokenUsage.CallsWithUsage);
            Assert.Equal(18, trace.TokenUsage.InputTokens);
            Assert.Equal(7, trace.TokenUsage.OutputTokens);
            Assert.Equal(25, trace.TokenUsage.TotalTokens);

            AgentToolCallTrace toolCall = Assert.Single(trace.ToolCalls);
            Assert.Equal("calculate", toolCall.ToolName);
            Assert.True(toolCall.Succeeded);
            Assert.Null(toolCall.Error);
            Assert.All(trace.WorkflowSteps, step => Assert.False(string.IsNullOrWhiteSpace(step.Title)));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_publishes_a_failed_trace_when_the_model_request_throws()
    {
        string tempDirectory = CreateTempDirectory();
        AgentRunner runner = new(
            CreateProfile(),
            new ThrowingAgentChatClient(),
            new ChatMemory(),
            Path.Combine(tempDirectory, "memory.json"),
            new AgentSkillRegistry([]));
        AgentExecutionTrace? capturedTrace = null;
        runner.ExecutionTraceCompletedAsync = trace =>
        {
            capturedTrace = trace;
            return Task.CompletedTask;
        };

        try
        {
            HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => runner.RunAsync("Hello"));

            Assert.Equal("Router unavailable.", exception.Message);
            AgentExecutionTrace trace = Assert.IsType<AgentExecutionTrace>(capturedTrace);
            Assert.Null(trace.Outcome);
            Assert.Equal(AgentRunStatus.Failed, trace.FinalState.Status);
            Assert.Equal("Router unavailable.", trace.Error);
            AgentModelCallTrace modelCall = Assert.Single(trace.ModelCalls);
            Assert.False(modelCall.Succeeded);
            Assert.Equal("Router unavailable.", modelCall.Error);
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
            BinaryData.FromString("{\"expression\":\"2 + 2\"}"));
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

    private static string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"agent-runner-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
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

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages)
        {
            throw new NotSupportedException("Streaming is not used by this test.");
        }
    }

    private sealed class ThrowingAgentChatClient : IAgentChatClient
    {
        public Task<ChatCompletion> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null)
        {
            throw new HttpRequestException("Router unavailable.");
        }

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages)
        {
            throw new NotSupportedException("Streaming is not used by this test.");
        }
    }
}
