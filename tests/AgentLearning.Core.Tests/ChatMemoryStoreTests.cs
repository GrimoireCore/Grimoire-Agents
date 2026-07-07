using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class ChatMemoryStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_empty_memory_when_file_does_not_exist()
    {
        string tempDirectory = CreateTempDirectory();
        string memoryFile = Path.Combine(tempDirectory, "memory", "chat-memory.json");

        try
        {
            ChatMemory memory = await ChatMemoryStore.LoadAsync(memoryFile);

            Assert.Empty(memory.Turns);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_round_trip_chat_turns()
    {
        string tempDirectory = CreateTempDirectory();
        string memoryFile = Path.Combine(tempDirectory, "memory", "chat-memory.json");
        ChatMemory memory = new();
        memory.AddUserMessage("  What is memory + persistence?  ");
        memory.AddAssistantMessage("Memory lets the agent keep useful context.");

        try
        {
            await ChatMemoryStore.SaveAsync(memoryFile, memory);

            Assert.True(File.Exists(memoryFile));
            string savedJson = await File.ReadAllTextAsync(memoryFile);
            Assert.Contains("What is memory + persistence?", savedJson);

            ChatMemory loaded = await ChatMemoryStore.LoadAsync(memoryFile);

            Assert.Collection(
                loaded.Turns,
                first =>
                {
                    Assert.Equal(ChatRole.User, first.Role);
                    Assert.Equal("What is memory + persistence?", first.Content);
                },
                second =>
                {
                    Assert.Equal(ChatRole.Assistant, second.Role);
                    Assert.Equal("Memory lets the agent keep useful context.", second.Content);
                });
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-memory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }
}
