using AgentLearning.Core;
using AgentLearning.Core.Workflow;

namespace AgentLearning.App;

/// <summary>
/// The externally visible result of one agent run.
/// </summary>
public sealed record AgentRunResult(
    string RunId,
    AgentRunOutcome Outcome,
    string? AssistantReply,
    AgentToolConfirmationRequest? PendingApproval,
    AgentWorkflowTrace WorkflowTrace,
    AgentRunSnapshot FinalState);
