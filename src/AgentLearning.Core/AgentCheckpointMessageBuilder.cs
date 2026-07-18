using AgentLearning.Core.Diagnostics;

namespace AgentLearning.Core;

/// <summary>
/// Converts diagnostic messages into persistable checkpoint messages.
/// Diagnostic messages target human preview; checkpoint messages target resumed execution.
/// </summary>
public static class AgentCheckpointMessageBuilder
{
    /// <summary>Creates checkpoint messages from the current request snapshot.</summary>
    public static IReadOnlyList<AgentCheckpointMessage> FromDebugMessages(
        IReadOnlyList<AgentDebugMessage> debugMessages)
    {
        ArgumentNullException.ThrowIfNull(debugMessages);

        List<AgentCheckpointMessage> checkpointMessages = [];
        foreach (AgentDebugMessage message in debugMessages)
        {
            checkpointMessages.Add(ConvertMessage(message));
        }

        return checkpointMessages;
    }

    private static AgentCheckpointMessage ConvertMessage(AgentDebugMessage message)
    {
        return message.Role switch
        {
            "system" or "user" => AgentCheckpointMessage.Text(message.Role, message.Content ?? string.Empty),
            "assistant" when message.ToolCalls.Count > 0 => ConvertAssistantToolCallMessage(message),
            "assistant" => AgentCheckpointMessage.Text("assistant", message.Content ?? string.Empty),
            "tool" => AgentCheckpointMessage.Tool(
                RequireToolCallId(message),
                message.Content ?? string.Empty),
            _ => throw new InvalidOperationException($"Unsupported checkpoint message role: {message.Role}")
        };
    }

    private static AgentCheckpointMessage ConvertAssistantToolCallMessage(AgentDebugMessage message)
    {
        AgentCheckpointToolCall[] toolCalls = message.ToolCalls
            .Select(toolCall => new AgentCheckpointToolCall(
                toolCall.Id,
                toolCall.Name,
                toolCall.ArgumentsJson))
            .ToArray();

        return AgentCheckpointMessage.AssistantToolCalls(toolCalls);
    }

    private static string RequireToolCallId(AgentDebugMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.ToolCallId))
        {
            throw new InvalidOperationException("Tool checkpoint message requires tool_call_id.");
        }

        return message.ToolCallId;
    }
}
