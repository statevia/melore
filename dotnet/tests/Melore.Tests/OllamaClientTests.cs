using Melore.Providers.Ollama;

namespace Melore.Tests;

/// <summary>
/// Ollama クライアントの応答処理と、失敗時に null へ倒す挙動。
/// </summary>
public sealed class OllamaClientTests
{
    /// <summary>チャットの本文を返す。</summary>
    [Fact]
    public async Task Chat_ReturnsReply()
    {
        // Arrange
        using var ollama = new OllamaStub();
        using var client = new OllamaChatClient(ollama.Endpoint, "chat-model");

        // Act
        var reply = await client.CompleteAsync("system", "user", CancellationToken.None);

        // Assert
        Assert.Equal(OllamaStub.ChatReply, reply);
    }

    /// <summary>モデルの失敗は例外にせず null を返す。</summary>
    [Fact]
    public async Task Chat_ModelFailure_ReturnsNull()
    {
        // Arrange
        using var ollama = new OllamaStub(brokenModels: true);
        using var client = new OllamaChatClient(ollama.Endpoint, "chat-model");

        // Act
        var reply = await client.CompleteAsync("system", "user", CancellationToken.None);

        // Assert
        Assert.Null(reply);
    }

    /// <summary>接続できないときはモデルを呼ばず null を返す。</summary>
    [Fact]
    public async Task Chat_Unavailable_ReturnsNull()
    {
        // Arrange
        using var ollama = new OllamaStub(available: false);
        using var client = new OllamaChatClient(ollama.Endpoint, "chat-model");

        // Act
        var reply = await client.CompleteAsync("system", "user", CancellationToken.None);

        // Assert
        Assert.False(client.IsAvailable);
        Assert.Null(reply);
    }

    /// <summary>埋め込みのベクトルを返す。</summary>
    [Fact]
    public async Task Embed_ReturnsVector()
    {
        // Arrange
        using var ollama = new OllamaStub();
        using var client = new OllamaEmbeddingClient(ollama.Endpoint, "embed:model");

        // Act
        var vector = await client.EmbedAsync("本文", isQuery: true, CancellationToken.None);

        // Assert
        Assert.Equal(3, vector?.Length);
        Assert.Equal("embed:embed-model", client.AnalyzerVersion);
    }

    /// <summary>埋め込みの失敗は例外にせず null を返す。</summary>
    [Fact]
    public async Task Embed_ModelFailure_ReturnsNull()
    {
        // Arrange
        using var ollama = new OllamaStub(brokenModels: true);
        using var client = new OllamaEmbeddingClient(ollama.Endpoint, "embed-model");

        // Act
        var vector = await client.EmbedAsync("本文", isQuery: false, CancellationToken.None);

        // Assert
        Assert.Null(vector);
    }

    /// <summary>接続できないときはモデルを呼ばず null を返す。</summary>
    [Fact]
    public async Task Embed_Unavailable_ReturnsNull()
    {
        // Arrange
        using var ollama = new OllamaStub(available: false);
        using var client = new OllamaEmbeddingClient(ollama.Endpoint, "embed-model");

        // Act
        var vector = await client.EmbedAsync("本文", isQuery: false, CancellationToken.None);

        // Assert
        Assert.Null(vector);
    }

    /// <summary>末尾にスラッシュが無いパスでも、最後のセグメントを残す。</summary>
    [Fact]
    public void Client_EndpointWithoutTrailingSlash_KeepsPath()
    {
        // Arrange
        using var ollama = new OllamaStub();
        var endpoint = new Uri(ollama.Endpoint.ToString().TrimEnd('/'));

        // Act
        using var client = new OllamaChatClient(endpoint, "chat-model");

        // Assert
        Assert.True(client.IsAvailable);
    }

    /// <summary>接続を拒否されても例外にせず、利用不可として扱う。</summary>
    [Fact]
    public void Client_ConnectionRefused_IsUnavailable()
    {
        // Arrange
        var ollama = new OllamaStub();
        var endpoint = ollama.Endpoint;
        ollama.Dispose();

        // Act
        using var chat = new OllamaChatClient(endpoint, "chat-model");
        using var embedding = new OllamaEmbeddingClient(endpoint, "embed-model");

        // Assert
        Assert.False(chat.IsAvailable);
        Assert.False(embedding.IsAvailable);
    }
}
