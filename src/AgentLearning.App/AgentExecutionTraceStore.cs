using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLearning.App;

/// <summary>Appends one compact JSON object per line to an agent trace file.</summary>
public sealed class AgentExecutionTraceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
        }
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AgentExecutionTraceStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public string FilePath => _filePath;

    public async Task AppendAsync(AgentExecutionTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string jsonLine = JsonSerializer.Serialize(trace, JsonOptions) + Environment.NewLine;
        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_filePath, jsonLine);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
