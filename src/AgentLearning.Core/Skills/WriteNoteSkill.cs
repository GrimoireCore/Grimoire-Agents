using System.Text.Json;

namespace AgentLearning.Core.Skills;

/// <summary>
/// Appends notes to a local Markdown file without duplicating the same logical tool call.
/// </summary>
public sealed class WriteNoteSkill : IAgentSkill
{
    private const int LockAttemptCount = 100;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly string _notesFilePath;
    private readonly Func<DateTimeOffset> _clock;

    public WriteNoteSkill(string notesFilePath)
        : this(notesFilePath, () => DateTimeOffset.Now)
    {
    }

    public WriteNoteSkill(string notesFilePath, Func<DateTimeOffset> clock)
    {
        if (string.IsNullOrWhiteSpace(notesFilePath))
        {
            throw new ArgumentException("Notes file path cannot be empty.", nameof(notesFilePath));
        }

        ArgumentNullException.ThrowIfNull(clock);

        _notesFilePath = Path.GetFullPath(notesFilePath.Trim());
        _clock = clock;
    }

    public string Name => "write_note";

    public string Description => "Append a note to a local markdown notes file.";

    public string ParametersJson => """
        {
          "type": "object",
          "properties": {
            "note": {
              "type": "string",
              "description": "The note content to append to the local notes file."
            }
          },
          "required": ["note"],
          "additionalProperties": false
        }
        """;

    public AgentSkillRiskLevel RiskLevel => AgentSkillRiskLevel.Medium;

    public bool RequiresConfirmation => true;

    public async Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);

        string note = ReadNote(argumentsJson);
        string directory = Path.GetDirectoryName(_notesFilePath)
            ?? throw new InvalidOperationException("The notes file must have a parent directory.");
        Directory.CreateDirectory(directory);

        string operationMarker = BuildOperationMarker(executionContext.IdempotencyKey);
        string lockFilePath = $"{_notesFilePath}.lock";
        await using FileStream lockStream = await AcquireFileLockAsync(lockFilePath, cancellationToken);
        string existingContent = File.Exists(_notesFilePath)
            ? await File.ReadAllTextAsync(_notesFilePath, cancellationToken)
            : string.Empty;

        if (existingContent.Contains(operationMarker, StringComparison.Ordinal))
        {
            return BuildSuccessResult();
        }

        string entry = BuildEntry(operationMarker, note);
        string updatedContent = AppendEntry(existingContent, entry);
        await WriteAtomicallyAsync(updatedContent, cancellationToken);

        return BuildSuccessResult();
    }

    private string BuildEntry(string operationMarker, string note)
    {
        return $"""
            {operationMarker}
            - {_clock():O}
              {note}

            """;
    }

    private string BuildSuccessResult()
    {
        return $"Note saved to {_notesFilePath}.";
    }

    private async Task WriteAtomicallyAsync(string content, CancellationToken cancellationToken)
    {
        string tempFilePath = $"{_notesFilePath}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempFilePath, content, cancellationToken);
            File.Move(tempFilePath, _notesFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private static async Task<FileStream> AcquireFileLockAsync(
        string lockFilePath,
        CancellationToken cancellationToken)
    {
        IOException? lastException = null;
        for (int attempt = 0; attempt < LockAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException exception)
            {
                lastException = exception;
            }

            await Task.Delay(LockRetryDelay, cancellationToken);
        }

        throw new IOException($"Timed out while locking notes file '{lockFilePath}'.", lastException);
    }

    private static string BuildOperationMarker(string idempotencyKey)
    {
        return $"<!-- grimoire-agent-operation:{idempotencyKey} -->";
    }

    private static string AppendEntry(string existingContent, string entry)
    {
        if (existingContent.Length == 0 || existingContent.EndsWith('\n'))
        {
            return existingContent + entry;
        }

        return existingContent + Environment.NewLine + entry;
    }

    private static string ReadNote(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);

        if (!document.RootElement.TryGetProperty("note", out JsonElement noteElement))
        {
            throw new InvalidOperationException("Write note skill requires a non-empty 'note' argument.");
        }

        string? note = noteElement.GetString();
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new InvalidOperationException("Write note skill requires a non-empty 'note' argument.");
        }

        return note.Trim();
    }
}
