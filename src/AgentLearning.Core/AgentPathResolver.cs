namespace AgentLearning.Core;

/// <summary>
/// 解析配置里的文件路径。
/// 相对路径基于程序运行目录，绝对路径保持原样。
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
