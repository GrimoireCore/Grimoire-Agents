using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class WriteNoteSkillTests
{
    [Fact]
    public async Task ExecuteAsync_appends_note_to_configured_file()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        DateTimeOffset fixedNow = new(2026, 7, 8, 10, 30, 0, TimeSpan.FromHours(8));
        WriteNoteSkill skill = new(notesFile, () => fixedNow);
        AgentToolExecutionContext executionContext = new("run_note", "call_note");

        string result = await skill.ExecuteAsync(
            """{"note":"记住：Tool Calling 需要权限控制。"}""",
            executionContext);

        string savedText = await File.ReadAllTextAsync(notesFile);
        Assert.Equal("write_note", skill.Name);
        Assert.Equal(AgentSkillRiskLevel.Medium, skill.RiskLevel);
        Assert.True(skill.RequiresConfirmation);
        Assert.Contains("Note saved to", result);
        Assert.Contains(executionContext.IdempotencyKey, savedText);
        Assert.Contains("2026-07-08T10:30:00.0000000+08:00", savedText);
        Assert.Contains("记住：Tool Calling 需要权限控制。", savedText);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_empty_note()
    {
        string notesFile = Path.Combine(Path.GetTempPath(), $"notes-{Guid.NewGuid():N}.md");
        WriteNoteSkill skill = new(notesFile);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => skill.ExecuteAsync(
                """{"note":" "}""",
                new AgentToolExecutionContext("run_note", "call_empty")));

        Assert.Contains("note", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_same_operation_twice_writes_note_once()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        Func<DateTimeOffset> clock =
            () => new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.FromHours(8));
        WriteNoteSkill firstProcessSkill = new(
            notesFile,
            clock);
        WriteNoteSkill restartedProcessSkill = new(notesFile, clock);
        AgentToolExecutionContext executionContext = new("run_same", "call_same");
        const string argumentsJson = """{"note":"This operation must be written once."}""";

        string firstResult = await firstProcessSkill.ExecuteAsync(argumentsJson, executionContext);
        string secondResult = await restartedProcessSkill.ExecuteAsync(argumentsJson, executionContext);

        string savedText = await File.ReadAllTextAsync(notesFile);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal(1, CountOccurrences(savedText, executionContext.IdempotencyKey));
        Assert.Equal(1, CountOccurrences(savedText, "This operation must be written once."));
        Assert.False(File.Exists($"{notesFile}.tmp"));
    }

    [Fact]
    public async Task ExecuteAsync_different_operations_can_write_the_same_note_twice()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        WriteNoteSkill skill = new(notesFile);
        AgentToolExecutionContext firstContext = new("run_first", "call_note");
        AgentToolExecutionContext secondContext = new("run_second", "call_note");
        const string argumentsJson = """{"note":"The same text is intentional."}""";

        await skill.ExecuteAsync(argumentsJson, firstContext);
        await skill.ExecuteAsync(argumentsJson, secondContext);

        string savedText = await File.ReadAllTextAsync(notesFile);
        Assert.Equal(2, CountOccurrences(savedText, "The same text is intentional."));
        Assert.Contains(firstContext.IdempotencyKey, savedText);
        Assert.Contains(secondContext.IdempotencyKey, savedText);
    }

    [Fact]
    public async Task ExecuteAsync_concurrent_operations_preserve_every_note()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        WriteNoteSkill skill = new(notesFile);

        Task<string>[] writes = Enumerable.Range(1, 8)
            .Select(index => skill.ExecuteAsync(
                $"{{\"note\":\"Concurrent note {index}\"}}",
                new AgentToolExecutionContext("run_concurrent", $"call_{index}")))
            .ToArray();

        await Task.WhenAll(writes);

        string savedText = await File.ReadAllTextAsync(notesFile);
        for (int index = 1; index <= writes.Length; index++)
        {
            Assert.Equal(1, CountOccurrences(savedText, $"Concurrent note {index}"));
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
