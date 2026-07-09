using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;

namespace AgentLearning.Core.Tests;

public sealed class AgentCheckpointStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_null_when_checkpoint_file_does_not_exist()
    {
        string tempDirectory = CreateTempDirectory();
        string checkpointFile = Path.Combine(tempDirectory, "checkpoints", "pending.json");

        try
        {
            AgentRunCheckpoint? checkpoint = await AgentCheckpointStore.LoadAsync(checkpointFile);

            Assert.Null(checkpoint);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_round_trip_pending_approval_checkpoint()
    {
        string tempDirectory = CreateTempDirectory();
        string checkpointFile = Path.Combine(tempDirectory, "checkpoints", "pending.json");
        AgentRunCheckpoint checkpoint = CreateCheckpoint();

        try
        {
            await AgentCheckpointStore.SaveAsync(checkpointFile, checkpoint);

            Assert.True(File.Exists(checkpointFile));
            string savedJson = await File.ReadAllTextAsync(checkpointFile);
            Assert.Contains("\"kind\": \"PendingToolApproval\"", savedJson);
            Assert.Contains("\"status\": \"WaitingForApproval\"", savedJson);
            Assert.Contains("\"risk_level\": \"Medium\"", savedJson);
            Assert.Contains("\"messages\": [", savedJson);
            Assert.Contains("\"tool_calls\": [", savedJson);
            Assert.Contains("\"selected_tool_names\": [", savedJson);
            Assert.Contains("\"write_note\"", savedJson);

            AgentRunCheckpoint? loaded = await AgentCheckpointStore.LoadAsync(checkpointFile);

            Assert.NotNull(loaded);
            Assert.Equal("run_abc", loaded.RunId);
            Assert.Equal(AgentCheckpointKind.PendingToolApproval, loaded.Kind);
            Assert.Equal(AgentRunStatus.WaitingForApproval, loaded.State.Status);
            Assert.Equal(["system", "user", "assistant"], loaded.Messages.Select(message => message.Role));
            Assert.Equal("call_123", loaded.Messages[2].ToolCalls[0].Id);
            Assert.Equal("write_note", loaded.Messages[2].ToolCalls[0].Name);
            Assert.Equal("""{"note":"hello"}""", loaded.Messages[2].ToolCalls[0].ArgumentsJson);
            Assert.Equal(["write_note"], loaded.SelectedToolNames);
            Assert.NotNull(loaded.PendingApproval);
            Assert.Equal("call_123", loaded.PendingApproval.ToolCallId);
            Assert.Equal("""{"note":"hello"}""", loaded.PendingApproval.ArgumentsJson);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_removes_checkpoint_file_and_ignores_missing_file()
    {
        string tempDirectory = CreateTempDirectory();
        string checkpointFile = Path.Combine(tempDirectory, "checkpoints", "pending.json");
        AgentRunCheckpoint checkpoint = CreateCheckpoint();

        try
        {
            await AgentCheckpointStore.SaveAsync(checkpointFile, checkpoint);

            await AgentCheckpointStore.DeleteAsync(checkpointFile);
            await AgentCheckpointStore.DeleteAsync(checkpointFile);

            Assert.False(File.Exists(checkpointFile));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static AgentRunCheckpoint CreateCheckpoint()
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

        return AgentRunCheckpoint.CreatePendingApproval(
            runId: "run_abc",
            createdAt: new DateTimeOffset(2026, 7, 9, 9, 30, 0, TimeSpan.FromHours(8)),
            request: request,
            state: snapshot,
            messages: messages,
            selectedToolNames: ["write_note"]);
    }

    private static string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }
}
