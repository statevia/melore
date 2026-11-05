namespace Melore.Core.Ports;

/// <summary>
/// Chat を使わないときの実装。
/// </summary>
public sealed class NullChatClient : IMeloreChatClient
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string ModelName => "none";

    /// <inheritdoc />
    public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}
