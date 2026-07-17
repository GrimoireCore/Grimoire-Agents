using AgentLearning.App;
using AgentLearning.Core.Workflow;
using System.Text.Json;

namespace AgentLearning.Core.Tests;

public sealed class AgentExecutionTraceStoreTests
{
    [Fact]
    public async Task AppendAsync_writes_one_compact_json_object_per_line()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"agent-trace-store-{Guid.NewGuid():N}");
        string tracePath = Path.Combine(tempDirectory, "traces", "agent-runs.jsonl");
        AgentExecutionTraceStore store = new(tracePath);

        try
        {
            await store.AppendAsync(CreateTrace("trace_1", "run_1"));
            await store.AppendAsync(CreateTrace("trace_2", "run_2"));

            string[] lines = await File.ReadAllLinesAsync(tracePath);
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.DoesNotContain(Environment.NewLine, line));

            using JsonDocument first = JsonDocument.Parse(lines[0]);
            Assert.Equal("trace_1", first.RootElement.GetProperty("trace_id").GetString());
            Assert.Equal("run_1", first.RootElement.GetProperty("run_id").GetString());
            Assert.Equal("completed", first.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("finished", first.RootElement
                .GetProperty("final_state")
                .GetProperty("status")
                .GetString());
            JsonElement workflowStep = first.RootElement
                .GetProperty("workflow_steps")[0];
            Assert.Equal("Ask model", workflowStep.GetProperty("title").GetString());
            Assert.False(workflowStep.TryGetProperty("detail", out _));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static AgentExecutionTrace CreateTrace(string traceId, string runId)
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        AgentRunSnapshot finalState = new(
            AgentRunStatus.Finished,
            ModelRequestCount: 1,
            ToolCallCount: 0,
            LastToolName: null,
            WaitingForApproval: false,
            LastError: null);

        return new AgentExecutionTrace(
            FormatVersion: 1,
            traceId,
            runId,
            Operation: "run",
            StartedAtUtc: timestamp,
            CompletedAtUtc: timestamp,
            DurationMilliseconds: 12,
            Model: "test-model",
            UserInputLength: 5,
            ApprovalDecision: null,
            Outcome: AgentRunOutcome.Completed,
            FinalState: finalState,
            TokenUsage: new AgentTokenUsageTotals(1, 8, 4, 12),
            ModelCalls: [],
            ToolCalls: [],
            WorkflowSteps:
            [
                new AgentWorkflowStepTrace(
                    Number: 1,
                    AgentWorkflowStepKind.AskModel,
                    Title: "Ask model")
            ],
            Error: null);
    }
}
