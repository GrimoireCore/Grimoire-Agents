using AgentLearning.App;
using AgentLearning.Core;
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;
using OpenAI.Chat;

string tempDirectory = Path.Combine(
    Path.GetTempPath(),
    $"agent-harness-demo-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDirectory);

try
{
    AgentProfile profile = CreateProfile();
    DemoChatClient chatClient = new(
        CreateCalculatorToolCall(),
        CreateTextAnswer("The result is 20."));
    AgentRunner runner = new(
        profile,
        chatClient,
        new ChatMemory(),
        Path.Combine(tempDirectory, "memory.json"),
        new AgentSkillRegistry([new CalculatorSkill()]));

    AgentExecutionTrace? executionTrace = null;
    runner.WorkflowStepCreated += step =>
        Console.WriteLine(AgentWorkflowStepFormatter.Format(step));
    runner.ExecutionTraceCompletedAsync = trace =>
    {
        executionTrace = trace;
        return Task.CompletedTask;
    };

    const string userInput = "What is (2 + 3) * 4?";
    Console.WriteLine($"You> {userInput}");

    AgentRunResult result = await runner.RunAsync(userInput);

    Console.WriteLine($"Agent> {result.AssistantReply}");
    Console.WriteLine(
        $"State> {result.FinalState.Status}, "
        + $"model requests={result.FinalState.ModelRequestCount}, "
        + $"tool calls={result.FinalState.ToolCallCount}");
    Console.WriteLine(
        $"Trace> model calls={executionTrace?.ModelCalls.Count}, "
        + $"tool calls={executionTrace?.ToolCalls.Count}, "
        + $"tokens={executionTrace?.TokenUsage.TotalTokens}");
}
finally
{
    Directory.Delete(tempDirectory, recursive: true);
}

static AgentProfile CreateProfile()
{
    return new AgentProfile(
        Name: "Harness Demo",
        Model: "demo-model",
        BaseUrl: "https://example.invalid/v1",
        EmbeddingBaseUrl: "http://127.0.0.1:1234/v1",
        EmbeddingModel: "demo-embedding-model",
        EnvKey: "DEMO_API_KEY",
        WireApi: "chat_completions",
        Stream: false,
        NativeToolCalling: true,
        ToolRouterEnabled: false,
        MaxToolsPerRequest: 1,
        ShowDebugRequests: false,
        ShowWorkflowTrace: true,
        MemoryFile: "memory.json",
        MaxMemoryTurns: 6,
        MaxMemoryContentChars: 2000,
        MaxToolIterations: 3,
        MaxToolResultChars: 1200,
        ToolTimeoutSeconds: 5,
        ApiKey: null,
        Description: "A deterministic Agent Harness demo.",
        Instructions: "Use the calculator when arithmetic is required.");
}

static ChatCompletion CreateCalculatorToolCall()
{
    ChatToolCall toolCall = ChatToolCall.CreateFunctionToolCall(
        "call_calculate_1",
        "calculate",
        BinaryData.FromString("{\"expression\":\"(2 + 3) * 4\"}"));

    return OpenAIChatModelFactory.ChatCompletion(
        "completion_tool_call",
        ChatFinishReason.ToolCalls,
        [],
        null,
        [toolCall],
        ChatMessageRole.Assistant,
        null,
        [],
        [],
        DateTimeOffset.UtcNow,
        "demo-model",
        null,
        OpenAIChatModelFactory.ChatTokenUsage(8, 40, 48, null));
}

static ChatCompletion CreateTextAnswer(string text)
{
    return OpenAIChatModelFactory.ChatCompletion(
        "completion_final_answer",
        ChatFinishReason.Stop,
        [ChatMessageContentPart.CreateTextPart(text)],
        null,
        [],
        ChatMessageRole.Assistant,
        null,
        [],
        [],
        DateTimeOffset.UtcNow,
        "demo-model",
        null,
        OpenAIChatModelFactory.ChatTokenUsage(6, 52, 58, null));
}

sealed class DemoChatClient(params ChatCompletion[] responses) : IAgentChatClient
{
    private readonly Queue<ChatCompletion> _responses = new(responses);

    public Task<ChatCompletion> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions? options = null)
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("The demo has no model response left.");
        }

        return Task.FromResult(_responses.Dequeue());
    }

    public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages)
    {
        throw new NotSupportedException("The deterministic demo does not use streaming.");
    }
}
