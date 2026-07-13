using AgentLearning.Core.Knowledge;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AgentLearning.McpServer;

/// <summary>
/// Calls an LM Studio OpenAI-compatible embeddings endpoint.
/// </summary>
public sealed class LmStudioEmbeddingClient : ITextEmbeddingClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public LmStudioEmbeddingClient(string baseUrl, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        string normalizedBaseUrl = baseUrl.Trim().TrimEnd('/') + "/";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _model = model.Trim();
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0 || inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Embedding inputs must contain non-empty text.", nameof(inputs));
        }

        EmbeddingRequest request = new(_model, inputs);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Could not connect to the LM Studio embeddings endpoint '{_httpClient.BaseAddress}'.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"LM Studio embeddings request failed with HTTP {(int)response.StatusCode}: {errorBody}");
            }

            EmbeddingResponse? result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
                cancellationToken: cancellationToken);
            if (result?.Data is null)
            {
                throw new InvalidOperationException("LM Studio returned an invalid embeddings response.");
            }

            EmbeddingData[] orderedData = result.Data
                .OrderBy(item => item.Index)
                .ToArray();
            if (orderedData.Length != inputs.Count)
            {
                throw new InvalidOperationException(
                    $"LM Studio returned {orderedData.Length} vectors for {inputs.Count} inputs.");
            }

            return orderedData.Select(item => item.Embedding).ToArray();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
