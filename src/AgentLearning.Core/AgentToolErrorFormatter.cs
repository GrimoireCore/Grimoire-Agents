using AgentLearning.Core.Skills;
using System.Text.Json;

namespace AgentLearning.Core;

/// <summary>
/// Converts recoverable tool exceptions into tool results the model can observe.
/// The model can then explain the error or retry with corrected arguments.
/// </summary>
public static class AgentToolErrorFormatter
{
    public static bool IsRecoverable(Exception exception)
    {
        return exception switch
        {
            AgentUnknownSkillException => false,
            TimeoutException => true,
            JsonException => true,
            ArgumentException => true,
            InvalidOperationException => true,
            FormatException => true,
            _ => false
        };
    }

    public static string FormatRecoverableError(string toolName, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(exception);

        return $"""
            [工具执行失败]
            工具名称：{toolName}
            错误类型：{exception.GetType().Name}
            错误信息：{exception.Message}

            你可以根据这个错误向用户解释失败原因，或者在修正参数后再次调用工具。
            """;
    }
}
