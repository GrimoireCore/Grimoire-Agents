using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class AgentPathResolverTests
{
    [Fact]
    public void ResolveRuntimePath_combines_relative_path_with_base_directory()
    {
        string result = AgentPathResolver.ResolveRuntimePath(
            baseDirectory: "/app/bin",
            configuredPath: "memory/chat-memory.json");

        Assert.Equal(Path.Combine("/app/bin", "memory/chat-memory.json"), result);
    }

    [Fact]
    public void ResolveRuntimePath_keeps_absolute_path()
    {
        string absolutePath = Path.Combine(Path.GetTempPath(), "chat-memory.json");

        string result = AgentPathResolver.ResolveRuntimePath(
            baseDirectory: "/app/bin",
            configuredPath: absolutePath);

        Assert.Equal(absolutePath, result);
    }
}
