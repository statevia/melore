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
    private MeloreRuntime(
        SqliteMeloreStore store,
        IEmbeddingClient embeddings,
        IMeloreChatClient chat,
        IndexService indexer,
        SearchService search,
        ReferenceService references,
        TraceService trace,
        ExplainService explain,
        ILoggerFactory loggers)
    {
        Store = store;
        Embeddings = embeddings;
        Chat = chat;
        Indexer = indexer;
        Search = search;
        References = references;
        Trace = trace;
        Explain = explain;
        Loggers = loggers;
    }

    /// <summary>SQLite ストア。</summary>
    public SqliteMeloreStore Store { get; }

    /// <summary>Embedding クライアント。</summary>
    public IEmbeddingClient Embeddings { get; }

    /// <summary>Chat クライアント。</summary>
    public IMeloreChatClient Chat { get; }

    /// <summary>インデクサ。</summary>
    public IndexService Indexer { get; }

    /// <summary>統合検索。</summary>
    public SearchService Search { get; }

    /// <summary>関連列挙。</summary>
    public ReferenceService References { get; }

    /// <summary>追跡。</summary>
    public TraceService Trace { get; }

    /// <summary>根拠付き説明。</summary>
    public ExplainService Explain { get; }

    /// <summary>ログ。</summary>
    public ILoggerFactory Loggers { get; }

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
            loggers.CreateLogger<IndexService>());
        var search = new SearchService(store, embeddings, new NoOpReranker());
        var explain = new ExplainService(
            search,
            store,
            chat,
            loggers.CreateLogger<ExplainService>());
        return new MeloreRuntime(
            store,
            embeddings,
            chat,
            indexer,
            search,
            new ReferenceService(store),
            new TraceService(store),
            explain,
            loggers);
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
