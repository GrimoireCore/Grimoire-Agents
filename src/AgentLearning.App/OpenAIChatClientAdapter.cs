using OpenAI.Chat;

namespace AgentLearning.App;

/// <summary>
/// Adapts the OpenAI SDK ChatClient to the interface required by AgentRunner.
/// This keeps the runner replaceable and makes model responses controllable in tests.
/// </summary>
public sealed class OpenAIChatClientAdapter : IAgentChatClient
{
    private readonly ChatClient _client;

    public OpenAIChatClientAdapter(ChatClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<ChatCompletion> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions? options = null)
    {
        return await _client.CompleteChatAsync(messages, options);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages)
    {
        await foreach (StreamingChatCompletionUpdate update in _client.CompleteChatStreamingAsync(messages))
        {
            yield return update;
        }
    }
}
