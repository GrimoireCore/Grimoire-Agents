using AgentLearning.Core.Workflow;

namespace AgentLearning.App;

/// <summary>
/// 一次 Agent 运行的结果。
/// Program.cs 拿到它之后，只需要负责把最终回答打印给用户。
/// </summary>
public sealed record AgentRunResult(
    string AssistantReply,
    AgentWorkflowTrace WorkflowTrace,
    AgentRunSnapshot FinalState);
