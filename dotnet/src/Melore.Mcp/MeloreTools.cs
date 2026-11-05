using System.ComponentModel;
using Melore.Contracts;
using Melore.Core.Ports;
using Melore.Core.Search;
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
[McpServerToolType]
public sealed class MeloreTools(
    SearchService search,
    ReferenceService references,
    TraceService trace,
    ExplainService explain,
    IMeloreStore store)
{
    /// <summary>
    /// 仕様とコードを検索する。出典（パスと行番号）付き。
    /// </summary>
    [McpServerTool(Name = "search", Title = "Search Melore index")]
    [Description("Search specifications and C# symbols in the indexed repository. Every hit includes file path and line range. Default body is a short snippet.")]
    public async Task<IReadOnlyList<HitDetail>> Search(
        [Description("Natural language or keywords, for example Fork Join")] string query,
        [Description("Maximum hits")] int limit = 8,
        [Description("If true, include full chunk text")] bool includeBody = false,
        CancellationToken cancellationToken = default)
    {
        var hits = await search.SearchAsync(query, Math.Clamp(limit, 1, 32), cancellationToken)
            .ConfigureAwait(false);
        return hits.Select(h => HitFormatter.ToDetail(store, h, includeBody)).ToList();
    }

    /// <summary>
    /// 関連するドキュメント・実装・テストを列挙する。
    /// </summary>
    [McpServerTool(Name = "references", Title = "List related knowledge")]
    [Description("List documents, implementations, and tests related to a specification chunk or C# symbol. basis is explicit or inferred.")]
    public IReadOnlyList<HitDetail> References(
        [Description("Chunk id, symbol id, or fully qualified name")] string target,
        [Description("Maximum hits")] int limit = 16,
        [Description("If true, include full chunk text")] bool includeBody = false)
    {
        return references.GetReferences(target, Math.Clamp(limit, 1, 64))
            .Select(h => HitFormatter.ToDetail(store, h, includeBody))
            .ToList();
    }

    /// <summary>
    /// 所属と継承を追跡する。
    /// </summary>
    [McpServerTool(Name = "trace", Title = "Trace symbol relations")]
    [Description("Trace containing types, members, and base type names. Call graph is not available yet and is stated in limitation.")]
    public TraceDto Trace(
        [Description("Symbol id or fully qualified name")] string target,
        [Description("Traversal depth, 1 to 6")] int depth = 2)
    {
        var result = trace.Trace(target, depth);
        return new TraceDto(
            result.Root is null ? null : HitFormatter.ToDetail(store, result.Root, includeBody: false),
            result.Nodes.Select(n => HitFormatter.ToDetail(store, n, includeBody: false)).ToList(),
            result.Edges,
            result.Limitation);
    }

    /// <summary>
    /// 検索上位チャンクだけを根拠に説明する。
    /// </summary>
    [McpServerTool(Name = "explain", Title = "Explain from search evidence")]
    [Description("Explain using only top search hits as evidence. Cites path and line range. Does not invent missing types or paths.")]
    public Task<ExplainResult> Explain(
        [Description("Natural language question, for example Hosted Fork の親子")] string query,
        [Description("Maximum evidence hits, 1 to 8")] int limit = 8,
        CancellationToken cancellationToken = default)
        => explain.ExplainAsync(query, Math.Clamp(limit, 1, 8), cancellationToken);
}

/// <summary>
/// MCP 用の追跡応答。
/// </summary>
public sealed record TraceDto(
    HitDetail? Root,
    IReadOnlyList<HitDetail> Nodes,
    IReadOnlyList<TraceEdge> Edges,
    string? Limitation);
