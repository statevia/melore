namespace Melore.Core.Ports;

/// <summary>
/// Embedding を使わないときの実装。
/// </summary>
public sealed class NullEmbeddingClient : IEmbeddingClient
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string ModelName => "none";

    /// <inheritdoc />
    public string AnalyzerVersion => "embed:none";

    /// <inheritdoc />
    public Task<float[]?> EmbedAsync(string text, bool isQuery, CancellationToken cancellationToken)
        => Task.FromResult<float[]?>(null);
}
