using System.ComponentModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Melore.Contracts;
using Melore.Core.Indexing;
using Melore.Core.Ports;
using Melore.Core.Search;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Melore.Mcp;

/// <summary>
/// Cursor 向け MCP ツール。本文は既定で出さない。
/// </summary>
/// <param name="search">統合検索。</param>
/// <param name="references">関連。</param>
/// <param name="trace">追跡。</param>
/// <param name="explain">根拠付き説明。</param>
/// <param name="store">本文取得用。</param>
/// <param name="indexer">差分の再索引。</param>
/// <param name="settings">対象リポジトリ。</param>
[McpServerToolType]
public sealed class MeloreTools(
    SearchService search,
    ReferenceService references,
    TraceService trace,
    ExplainService explain,
    IMeloreStore store,
    IndexService indexer,
    MeloreSettings settings)
{
    private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 所在が分からない仕様・実装・テストを探す。grep より先に使う。
    /// </summary>
    [McpServerTool(Name = "search", Title = "Search Melore index")]
    [Description("Use before grep when the file location is unknown. Searches specifications, C# implementation, and tests. Example: Fork Join の親子. Every hit has a path, line range, and lifecycle (current, archived, rejected). Default body is a short snippet. Default scope is current. To find why something was decided or why a proposal was rejected, use scope=history (completed and rejected specs). Example: ノード ID を 12 桁の 16 進にした理由.")]
    public Task<SearchToolResult> Search(
        [Description("Natural language or keywords. Japanese is fine, for example 冪等性キー")] string query,
        [Description("Maximum hits")] int limit = 8,
        [Description("If true, include full chunk text")] bool includeBody = false,
        [Description("current (default), history (completed and rejected specs only), or all")] string? scope = null,
        CancellationToken cancellationToken = default)
        => InvokeAsync(
            "search",
            new { query, limit, includeBody, scope },
            async () =>
            {
                var resolved = ParseScope(scope);
                var freshness = await indexer.EnsureFreshAsync(settings, cancellationToken).ConfigureAwait(false);
                var hits = await search.SearchAsync(query, Math.Clamp(limit, 1, 32), resolved, cancellationToken)
                    .ConfigureAwait(false);
                return new SearchToolResult(
                    hits.Select(h => HitFormatter.ToDetail(store, h, includeBody)).ToList(),
                    freshness.Dirty,
                    freshness.DirtyFileCount,
                    freshness.RefreshedFileCount);
            },
            cancellationToken);

    /// <summary>
    /// 見つかったシンボルのまわりの文書・実装・テストを見る。先に search する。
    /// </summary>
    [McpServerTool(Name = "references", Title = "List related knowledge")]
    [Description("After search, list documents, implementations, and tests around a symbol or specification chunk. basis is explicit or inferred. Example target: ExecutionEngine.")]
    public Task<SearchToolResult> References(
        [Description("Chunk id, symbol id, or fully qualified name")] string target,
        [Description("Maximum hits")] int limit = 16,
        [Description("If true, include full chunk text")] bool includeBody = false,
        CancellationToken cancellationToken = default)
        => InvokeAsync(
            "references",
            new { target, limit, includeBody },
            async () =>
            {
                var freshness = await indexer.EnsureFreshAsync(settings, cancellationToken).ConfigureAwait(false);
                var hits = references.GetReferences(target, Math.Clamp(limit, 1, 64))
                    .Select(h => HitFormatter.ToDetail(store, h, includeBody))
                    .ToList();
                return new SearchToolResult(hits, freshness.Dirty, freshness.DirtyFileCount, freshness.RefreshedFileCount);
            },
            cancellationToken);

    /// <summary>
    /// 呼び出しと所属を、名前と行範囲だけでたどる。先に search する。
    /// </summary>
    [McpServerTool(Name = "trace", Title = "Trace symbol relations")]
    [Description("After search, trace contains, calls, and implements for a symbol. Nodes are name and line range only. Example: ExecutionEngine.")]
    public Task<TraceResult> Trace(
        [Description("Symbol id or fully qualified name")] string target,
        [Description("Traversal depth, 1 to 6")] int depth = 2,
        [Description("Maximum nodes")] int limit = TraceService.DefaultLimit,
        [Description("Relationship types to keep, for example calls. Empty means all.")] string[]? relTypes = null,
        CancellationToken cancellationToken = default)
        => InvokeAsync(
            "trace",
            new { target, depth, limit, relTypes },
            async () =>
            {
                await indexer.EnsureFreshAsync(settings, cancellationToken).ConfigureAwait(false);
                return trace.Trace(target, depth, limit, relTypes);
            },
            cancellationToken);

    /// <summary>
    /// 検索上位だけを根拠に短く説明する。先に search し、足りなければこれ。
    /// </summary>
    [McpServerTool(Name = "explain", Title = "Explain from search evidence")]
    [Description("Explain from top search hits only, with path and line citations. Use for a short answer after search. Example: Join で枝が書いた vars を親へ戻す規則. Does not invent missing types or paths. Use scope=history to explain why something was decided or rejected.")]
    public Task<ExplainResult> Explain(
        [Description("Natural language question")] string query,
        [Description("Maximum evidence hits, 1 to 8")] int limit = 8,
        [Description("current (default), history (completed and rejected specs only), or all")] string? scope = null,
        CancellationToken cancellationToken = default)
        => InvokeAsync(
            "explain",
            new { query, limit, scope },
            async () =>
            {
                var resolved = ParseScope(scope);
                var freshness = await indexer.EnsureFreshAsync(settings, cancellationToken).ConfigureAwait(false);
                var result = await explain.ExplainAsync(query, Math.Clamp(limit, 1, 8), resolved, cancellationToken)
                    .ConfigureAwait(false);
                return result with
                {
                    Dirty = freshness.Dirty,
                    DirtyFileCount = freshness.DirtyFileCount,
                    RefreshedFileCount = freshness.RefreshedFileCount
                };
            },
            cancellationToken);

    /// <summary>
    /// ツール引数の <c>scope</c> を解釈する。
    /// </summary>
    /// <exception cref="McpException">許可値以外のとき。メッセージは許可値だけを示し、内部情報を含めない。</exception>
    private static SearchScope ParseScope(string? scope)
        => LifecycleText.TryParseScope(scope, out var resolved)
            ? resolved
            : throw new McpException("scope は " + LifecycleText.AllowedScopes + " のどれかを指定してください。");

    private async Task<T> InvokeAsync<T>(
        string tool,
        object arguments,
        Func<Task<T>> body,
        CancellationToken cancellationToken)
        where T : notnull
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await body().ConfigureAwait(false);
            WriteLog(tool, arguments, result, Stopwatch.GetElapsedTime(started), success: true, error: null);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            WriteLog(tool, arguments, new { error = ex.Message }, Stopwatch.GetElapsedTime(started), success: false, error: ex.Message);
            throw;
        }
    }

    private void WriteLog(string tool, object arguments, object result, TimeSpan elapsed, bool success, string? error)
    {
        try
        {
            store.RecordMcpInvocation(
                tool,
                JsonSerializer.Serialize(arguments, LogJson),
                JsonSerializer.Serialize(result, LogJson),
                (int)elapsed.TotalMilliseconds,
                success,
                error);
        }
        catch (Exception)
        {
            // 利用ログの失敗でツール結果は返る。
        }
    }
}
