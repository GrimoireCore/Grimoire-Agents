namespace AgentLearning.Core;

/// <summary>
/// Resolves file paths from configuration.
/// Relative paths use the process working directory; absolute paths remain unchanged.
/// </summary>
public static class AgentPathResolver
{
    public static string ResolveRuntimePath(string baseDirectory, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new ArgumentException("Configured path cannot be empty.", nameof(configuredPath));
        }

        return Path.IsPathFullyQualified(configuredPath)
            ? configuredPath
            : Path.Combine(baseDirectory, configuredPath);
    }
}
