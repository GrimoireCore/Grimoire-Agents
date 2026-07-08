namespace AgentLearning.Core;

/// <summary>
/// 构建发回模型的工具审批观察消息。
/// 用户拒绝时，模型也需要知道工具没有执行，不能假装动作已经完成。
/// </summary>
public static class AgentToolApprovalObservation
{
    /// <summary>构建“用户拒绝执行工具”的观察消息。</summary>
    public static string BuildRejected(string toolName)
    {
        return $"Tool '{toolName}' was not executed because the user rejected the confirmation request.";
    }
}
