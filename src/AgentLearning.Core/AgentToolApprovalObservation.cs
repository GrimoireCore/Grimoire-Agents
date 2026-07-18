namespace AgentLearning.Core;

/// <summary>
/// Builds the tool-approval observation returned to the model.
/// A rejection must tell the model that the action did not run.
/// </summary>
public static class AgentToolApprovalObservation
{
    /// <summary>Builds an observation stating that the user rejected execution.</summary>
    public static string BuildRejected(string toolName)
    {
        return $"Tool '{toolName}' was not executed because the user rejected the confirmation request.";
    }
}
