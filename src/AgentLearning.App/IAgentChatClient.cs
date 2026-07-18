using OpenAI.Chat;

namespace AgentLearning.App;

/// <summary>
/// Defines the model-client operations used by AgentRunner.
/// Production uses an OpenAI SDK adapter, while tests can return deterministic results.
/// </summary>
public interface IAgentChatClient
{
    /// <summary>Sends a non-streaming Chat Completions request.</summary>
    Task<ChatCompletion> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions? options = null);

    /// <summary>Sends a streaming Chat Completions request.</summary>
    IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages);
}
