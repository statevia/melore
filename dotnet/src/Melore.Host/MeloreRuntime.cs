using Melore.Contracts;
using Melore.Core.Indexing;
using Melore.Core.Ports;
using Melore.Core.Search;
using Melore.Infrastructure.Git;
using Melore.Infrastructure.Sqlite;
using Melore.Parsing.Markdown;
using Melore.Parsing.Roslyn;
using Melore.Providers.Ollama;
using Microsoft.Extensions.Logging;

namespace Melore.Host;

/// <summary>
/// CLI と MCP が共有する実行時コンテキスト。
/// </summary>
public sealed class MeloreRuntime : IAsyncDisposable, IDisposable
{
    private MeloreRuntime()
    {
    }

    /// <summary>SQLite ストア。</summary>
    public required SqliteMeloreStore Store { get; init; }

    /// <summary>Embedding クライアント。</summary>
    public required IEmbeddingClient Embeddings { get; init; }

    /// <summary>Chat クライアント。</summary>
    public required IMeloreChatClient Chat { get; init; }

    /// <summary>インデクサ。</summary>
    public required IndexService Indexer { get; init; }

    /// <summary>統合検索。</summary>
    public required SearchService Search { get; init; }

    /// <summary>関連列挙。</summary>
    public required ReferenceService References { get; init; }

    /// <summary>追跡。</summary>
    public required TraceService Trace { get; init; }

    /// <summary>根拠付き説明。</summary>
    public required ExplainService Explain { get; init; }

    /// <summary>ログ。</summary>
    public required ILoggerFactory Loggers { get; init; }

    /// <summary>
    /// 設定からランタイムを組み立てる。
    /// </summary>
    public static MeloreRuntime Create(MeloreSettings settings, ILoggerFactory loggers)
    {
        var store = new SqliteMeloreStore(
            SettingsLoader.ResolveDatabasePath(settings),
            settings.RepositoryName,
            settings.RepositoryPath);
        IEmbeddingClient embeddings = new NullEmbeddingClient();
        IMeloreChatClient chat = new NullChatClient();
        var endpoint = new Uri(settings.Ollama.Endpoint);
        try
        {
            var ollama = new OllamaEmbeddingClient(endpoint, settings.Ollama.EmbeddingModel);
            if (ollama.IsAvailable)
            {
                embeddings = ollama;
            }
            else
            {
                ollama.Dispose();
            }
        }
        catch (Exception)
        {
            embeddings = new NullEmbeddingClient();
        }

        try
        {
            var ollamaChat = new OllamaChatClient(endpoint, settings.Ollama.ChatModel);
            if (ollamaChat.IsAvailable)
            {
                chat = ollamaChat;
            }
            else
            {
                ollamaChat.Dispose();
            }
        }
        catch (Exception)
        {
            chat = new NullChatClient();
        }

        var indexer = new IndexService(
            new GitWorkspace(settings),
            new MarkdownDocumentParser(),
            new RoslynSyntaxAnalyzer(),
            store,
            embeddings,
            loggers.CreateLogger<IndexService>(),
            new RoslynSemanticGraph());
        var search = new SearchService(store, embeddings, new NoOpReranker());
        var explain = new ExplainService(
            search,
            store,
            chat,
            loggers.CreateLogger<ExplainService>());
        return new MeloreRuntime
        {
            Store = store,
            Embeddings = embeddings,
            Chat = chat,
            Indexer = indexer,
            Search = search,
            References = new ReferenceService(store),
            Trace = new TraceService(store),
            Explain = explain,
            Loggers = loggers
        };
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Store.Dispose();
        if (Embeddings is IDisposable embeddingDisposable)
        {
            embeddingDisposable.Dispose();
        }

        if (Chat is IDisposable chatDisposable)
        {
            chatDisposable.Dispose();
        }

        Loggers.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
