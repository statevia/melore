using Melore.Core.Ports;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Melore.Providers.Ollama;

/// <summary>
/// Ollama `/api/embed` 経由の Embedding。デーモンが無いときは利用不可。
/// </summary>
public sealed class OllamaEmbeddingClient : IEmbeddingClient, IDisposable
{
    private const string QueryInstruction =
        "Instruct: Given a search query, retrieve relevant specification and code passages\nQuery:";

    private readonly OllamaApiClient _client;
    private readonly HttpClient _probe;

    /// <summary>
    /// ローカル Ollama に接続する。
    /// </summary>
    public OllamaEmbeddingClient(Uri endpoint, string modelName)
    {
        ModelName = modelName;
        var baseUri = endpoint.ToString().EndsWith('/') ? endpoint : new Uri(endpoint.ToString() + "/");
        _client = new OllamaApiClient(baseUri, modelName);
        _probe = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(2) };
        IsAvailable = Probe();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public string ModelName { get; }

    /// <inheritdoc />
    public string AnalyzerVersion => "embed:" + ModelName.Replace(":", "-", StringComparison.Ordinal);

    /// <inheritdoc />
    public async Task<float[]?> EmbedAsync(string text, bool isQuery, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var payload = isQuery ? QueryInstruction + text : text;
        try
        {
            IEmbeddingGenerator<string, Embedding<float>> generator = _client;
            var embedding = await generator.GenerateAsync(payload, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return embedding.Vector.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _probe.Dispose();
        _client.Dispose();
    }

    private bool Probe()
    {
        try
        {
            using var response = _probe.GetAsync("api/tags").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
