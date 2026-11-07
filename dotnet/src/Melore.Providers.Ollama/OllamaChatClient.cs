using Melore.Core.Ports;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace Melore.Providers.Ollama;

/// <summary>
/// Ollama chat。思考トークンは出さない。応答後にモデルを unload する。
/// </summary>
public sealed class OllamaChatClient : IMeloreChatClient, IDisposable
{
    private readonly OllamaApiClient _client;
    private readonly HttpClient _probe;

    /// <summary>
    /// ローカル Ollama に接続する。
    /// </summary>
    public OllamaChatClient(Uri endpoint, string modelName)
    {
        ModelName = modelName;
        var baseUri = OllamaEndpoint.WithTrailingSlash(endpoint);
        _client = new OllamaApiClient(baseUri, modelName);
        _probe = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(2) };
        IsAvailable = Probe();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public string ModelName { get; }

    /// <inheritdoc />
    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var request = new ChatRequest
        {
            Model = ModelName,
            Stream = false,
            Think = false,
            KeepAlive = "0",
            Options = new RequestOptions
            {
                Temperature = 0.1f,
                NumCtx = 8192
            },
            Messages =
            [
                new Message { Role = ChatRole.System, Content = systemPrompt },
                new Message { Role = ChatRole.User, Content = userPrompt }
            ]
        };

        try
        {
            var parts = new List<string>();
            await foreach (var chunk in _client.ChatAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk?.Message?.Content))
                {
                    parts.Add(chunk.Message.Content);
                }
            }

            var text = string.Concat(parts).Trim();
            return text.Length == 0 ? null : text;
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
