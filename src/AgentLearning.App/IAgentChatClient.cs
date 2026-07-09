using OpenAI.Chat;

namespace AgentLearning.App;

/// <summary>
/// AgentRunner 使用的模型客户端接口。
/// 生产环境由 OpenAI SDK 适配器实现，测试环境可以用假客户端返回固定模型结果。
/// </summary>
public interface IAgentChatClient
{
    /// <summary>发送非流式 Chat Completions 请求。</summary>
    Task<ChatCompletion> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions? options = null);

    /// <summary>发送流式 Chat Completions 请求。</summary>
    IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages);
}
