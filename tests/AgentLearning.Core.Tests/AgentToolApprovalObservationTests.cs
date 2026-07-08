using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolApprovalObservationTests
{
    [Fact]
    public void BuildRejected_returns_model_visible_rejection_message()
    {
        string message = AgentToolApprovalObservation.BuildRejected("write_note");

        Assert.Contains("write_note", message);
        Assert.Contains("user rejected", message);
        Assert.Contains("was not executed", message);
    }

    [Fact]
    public void ConfirmationRequest_captures_tool_metadata()
    {
        AgentToolConfirmationRequest request = new(
            ToolName: "write_note",
            Description: "Append a note.",
            ArgumentsJson: """{"note":"hello"}""",
            RiskLevel: AgentSkillRiskLevel.Medium);

        Assert.Equal("write_note", request.ToolName);
        Assert.Equal("Append a note.", request.Description);
        Assert.Equal("""{"note":"hello"}""", request.ArgumentsJson);
        Assert.Equal(AgentSkillRiskLevel.Medium, request.RiskLevel);
    }
}
