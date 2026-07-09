using AgentLearning.Core.Workflow;

namespace AgentLearning.Core.Tests;

public sealed class AgentRunStateTests
{
    [Fact]
    public void New_state_starts_as_initialized()
    {
        AgentRunState state = new();

        AgentRunSnapshot snapshot = state.ToSnapshot();

        Assert.Equal(AgentRunStatus.Initialized, snapshot.Status);
        Assert.Equal(0, snapshot.ModelRequestCount);
        Assert.Equal(0, snapshot.ToolCallCount);
        Assert.False(snapshot.WaitingForApproval);
        Assert.Null(snapshot.LastToolName);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public void State_tracks_basic_agent_progress()
    {
        AgentRunState state = new();

        state.MarkReceivedInput();
        state.MarkBuiltContext();
        state.MarkRoutedTools();
        state.MarkAskedModel();

        AgentRunSnapshot snapshot = state.ToSnapshot();

        Assert.Equal(AgentRunStatus.AskedModel, snapshot.Status);
        Assert.Equal(1, snapshot.ModelRequestCount);
        Assert.Equal(0, snapshot.ToolCallCount);
    }

    [Fact]
    public void State_tracks_tool_approval_rejection_and_second_model_request()
    {
        AgentRunState state = new();

        state.MarkAskedModel();
        state.MarkToolRequested("write_note");
        state.MarkWaitingForApproval("write_note");
        AgentRunSnapshot waitingSnapshot = state.ToSnapshot();

        state.MarkToolRejected("write_note");
        state.MarkAskedModel();
        AgentRunSnapshot rejectedSnapshot = state.ToSnapshot();

        Assert.Equal(AgentRunStatus.WaitingForApproval, waitingSnapshot.Status);
        Assert.True(waitingSnapshot.WaitingForApproval);
        Assert.Equal("write_note", waitingSnapshot.LastToolName);
        Assert.Equal(1, waitingSnapshot.ToolCallCount);

        Assert.Equal(AgentRunStatus.AskedModel, rejectedSnapshot.Status);
        Assert.False(rejectedSnapshot.WaitingForApproval);
        Assert.Equal("write_note", rejectedSnapshot.LastToolName);
        Assert.Equal(2, rejectedSnapshot.ModelRequestCount);
    }

    [Fact]
    public void State_tracks_tool_execution_and_finish()
    {
        AgentRunState state = new();

        state.MarkAskedModel();
        state.MarkToolRequested("calculate");
        state.MarkToolExecuted("calculate");
        state.MarkFinished();

        AgentRunSnapshot snapshot = state.ToSnapshot();

        Assert.Equal(AgentRunStatus.Finished, snapshot.Status);
        Assert.Equal(1, snapshot.ToolCallCount);
        Assert.Equal("calculate", snapshot.LastToolName);
        Assert.False(snapshot.WaitingForApproval);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public void State_tracks_failures()
    {
        AgentRunState state = new();

        state.MarkToolFailed("calculate", "Division by zero is not allowed.");

        AgentRunSnapshot snapshot = state.ToSnapshot();

        Assert.Equal(AgentRunStatus.ToolFailed, snapshot.Status);
        Assert.Equal("calculate", snapshot.LastToolName);
        Assert.Equal("Division by zero is not allowed.", snapshot.LastError);
    }
}
