using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;

namespace AgentLearning.Core.Tests;

public sealed class AgentRunCheckpointTests
{
    [Fact]
    public void CreatePendingApproval_captures_tool_request_state_messages_and_selected_tools()
    {
        AgentRunSnapshot snapshot = new(
            Status: AgentRunStatus.WaitingForApproval,
            ModelRequestCount: 1,
            ToolCallCount: 1,
            LastToolName: "write_note",
            WaitingForApproval: true,
            LastError: null);

        AgentToolConfirmationRequest request = new(
            ToolCallId: "call_123",
            ToolName: "write_note",
            Description: "Append a note.",
            ArgumentsJson: """{"note":"hello"}""",
            RiskLevel: AgentSkillRiskLevel.Medium);

        AgentCheckpointMessage[] messages =
        [
            AgentCheckpointMessage.Text("system", "You are a teacher."),
            AgentCheckpointMessage.Text("user", "please save a note"),
            AgentCheckpointMessage.AssistantToolCalls(
            [
                new AgentCheckpointToolCall(
                    Id: "call_123",
                    Name: "write_note",
                    ArgumentsJson: """{"note":"hello"}""")
            ])
        ];
        string[] selectedToolNames = ["write_note"];

        AgentRunCheckpoint checkpoint = AgentRunCheckpoint.CreatePendingApproval(
            runId: "run_abc",
            createdAt: new DateTimeOffset(2026, 7, 9, 9, 30, 0, TimeSpan.FromHours(8)),
            request: request,
            state: snapshot,
            messages: messages,
            selectedToolNames: selectedToolNames);

        Assert.Equal("run_abc", checkpoint.RunId);
        Assert.Equal(AgentCheckpointKind.PendingToolApproval, checkpoint.Kind);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 9, 30, 0, TimeSpan.FromHours(8)), checkpoint.CreatedAt);
        Assert.Equal(AgentRunStatus.WaitingForApproval, checkpoint.State.Status);
        Assert.Equal(["system", "user", "assistant"], checkpoint.Messages.Select(message => message.Role));
        Assert.Equal("You are a teacher.", checkpoint.Messages[0].Content);
        Assert.Equal("call_123", checkpoint.Messages[2].ToolCalls[0].Id);
        Assert.Equal("write_note", checkpoint.Messages[2].ToolCalls[0].Name);
        Assert.Equal("""{"note":"hello"}""", checkpoint.Messages[2].ToolCalls[0].ArgumentsJson);
        Assert.Equal(["write_note"], checkpoint.SelectedToolNames);
        Assert.NotNull(checkpoint.PendingApproval);
        Assert.Equal("call_123", checkpoint.PendingApproval.ToolCallId);
        Assert.Equal("write_note", checkpoint.PendingApproval.ToolName);
        Assert.Equal("""{"note":"hello"}""", checkpoint.PendingApproval.ArgumentsJson);
        Assert.Equal(AgentSkillRiskLevel.Medium, checkpoint.PendingApproval.RiskLevel);
    }

    [Fact]
    public void CreatePendingApproval_rejects_state_that_is_not_waiting_for_approval()
    {
        AgentRunSnapshot snapshot = new(
            Status: AgentRunStatus.AskedModel,
            ModelRequestCount: 1,
            ToolCallCount: 0,
            LastToolName: null,
            WaitingForApproval: false,
            LastError: null);

        AgentToolConfirmationRequest request = new(
            ToolCallId: "call_123",
            ToolName: "write_note",
            Description: "Append a note.",
            ArgumentsJson: """{"note":"hello"}""",
            RiskLevel: AgentSkillRiskLevel.Medium);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AgentRunCheckpoint.CreatePendingApproval(
                runId: "run_abc",
                createdAt: DateTimeOffset.Now,
                request: request,
                state: snapshot,
                messages: [],
                selectedToolNames: []));

        Assert.Contains("WaitingForApproval", exception.Message);
    }
}
