using AgentLearning.Core.Workflow;
using OpenAI.Chat;
using System.Diagnostics;

namespace AgentLearning.App;

/// <summary>
/// Collects telemetry while AgentRunner is executing and creates one immutable trace at the end.
/// </summary>
internal sealed class AgentExecutionTraceBuilder
{
    private readonly Stopwatch _runStopwatch = Stopwatch.StartNew();
    private readonly List<AgentModelCallTrace> _modelCalls = [];
    private readonly List<AgentToolCallTrace> _toolCalls = [];

    public AgentExecutionTraceBuilder(
        string runId,
        string operation,
        string model,
        int? userInputLength = null,
        bool? approvalDecision = null)
    {
        RunId = runId;
        Operation = operation;
        Model = model;
        UserInputLength = userInputLength;
        ApprovalDecision = approvalDecision;
        TraceId = $"trace_{Guid.NewGuid():N}";
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public string TraceId { get; }

    public string RunId { get; }

    public string Operation { get; }

    public string Model { get; }

    public int? UserInputLength { get; }

    public bool? ApprovalDecision { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public void RecordModelCall(
        string stage,
        TimeSpan duration,
        ChatCompletion? completion,
        Exception? error = null)
    {
        ChatTokenUsage? usage = completion?.Usage;
        _modelCalls.Add(new AgentModelCallTrace(
            Sequence: _modelCalls.Count + 1,
            Stage: stage,
            DurationMilliseconds: ToMilliseconds(duration),
            Succeeded: error is null,
            FinishReason: completion?.FinishReason.ToString(),
            InputTokens: usage?.InputTokenCount,
            OutputTokens: usage?.OutputTokenCount,
            TotalTokens: usage?.TotalTokenCount,
            Error: error?.Message));
    }

    public void RecordToolCall(string toolName, TimeSpan duration, Exception? error = null)
    {
        _toolCalls.Add(new AgentToolCallTrace(
            Sequence: _toolCalls.Count + 1,
            ToolName: toolName,
            DurationMilliseconds: ToMilliseconds(duration),
            Succeeded: error is null,
            Error: error?.Message));
    }

    public AgentExecutionTrace Build(
        AgentRunOutcome? outcome,
        AgentRunSnapshot finalState,
        IReadOnlyList<AgentWorkflowStep> workflowSteps,
        Exception? error = null)
    {
        _runStopwatch.Stop();
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        AgentModelCallTrace[] callsWithUsage = _modelCalls
            .Where(call => call.TotalTokens.HasValue)
            .ToArray();

        AgentTokenUsageTotals tokenUsage = new(
            CallsWithUsage: callsWithUsage.Length,
            InputTokens: callsWithUsage.Sum(call => call.InputTokens ?? 0),
            OutputTokens: callsWithUsage.Sum(call => call.OutputTokens ?? 0),
            TotalTokens: callsWithUsage.Sum(call => call.TotalTokens ?? 0));

        return new AgentExecutionTrace(
            FormatVersion: 1,
            TraceId,
            RunId,
            Operation,
            StartedAtUtc,
            CompletedAtUtc: completedAtUtc,
            DurationMilliseconds: ToMilliseconds(_runStopwatch.Elapsed),
            Model,
            UserInputLength,
            ApprovalDecision,
            Outcome: outcome,
            FinalState: finalState,
            TokenUsage: tokenUsage,
            ModelCalls: _modelCalls.ToArray(),
            ToolCalls: _toolCalls.ToArray(),
            WorkflowSteps: workflowSteps
                .Select(step => new AgentWorkflowStepTrace(
                    step.Number,
                    step.Kind,
                    step.Title))
                .ToArray(),
            Error: error?.Message);
    }

    private static long ToMilliseconds(TimeSpan duration)
    {
        return Math.Max(0, (long)Math.Round(duration.TotalMilliseconds));
    }
}
