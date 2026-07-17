using AgentLearning.Core.Workflow;

namespace AgentLearning.App;

/// <summary>
/// A structured record for one executable segment of an agent run.
/// A resumed run keeps the same RunId but receives a new TraceId.
/// </summary>
public sealed record AgentExecutionTrace(
    int FormatVersion,
    string TraceId,
    string RunId,
    string Operation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long DurationMilliseconds,
    string Model,
    int? UserInputLength,
    bool? ApprovalDecision,
    AgentRunOutcome? Outcome,
    AgentRunSnapshot FinalState,
    AgentTokenUsageTotals TokenUsage,
    IReadOnlyList<AgentModelCallTrace> ModelCalls,
    IReadOnlyList<AgentToolCallTrace> ToolCalls,
    IReadOnlyList<AgentWorkflowStepTrace> WorkflowSteps,
    string? Error);

/// <summary>Token totals from model responses that included usage data.</summary>
public sealed record AgentTokenUsageTotals(
    int CallsWithUsage,
    int InputTokens,
    int OutputTokens,
    int TotalTokens);

/// <summary>Timing, outcome, and optional token usage for one model request.</summary>
public sealed record AgentModelCallTrace(
    int Sequence,
    string Stage,
    long DurationMilliseconds,
    bool Succeeded,
    string? FinishReason,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    string? Error);

/// <summary>Timing and outcome for one tool execution.</summary>
public sealed record AgentToolCallTrace(
    int Sequence,
    string ToolName,
    long DurationMilliseconds,
    bool Succeeded,
    string? Error);

/// <summary>A workflow step without message, argument, or tool-result content.</summary>
public sealed record AgentWorkflowStepTrace(
    int Number,
    AgentWorkflowStepKind Kind,
    string Title);
