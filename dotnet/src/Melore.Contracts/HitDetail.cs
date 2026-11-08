namespace Melore.Contracts;

/// <summary>
/// MCP / CLI 向けのヒット。既定は snippet のみ。
/// </summary>
/// <param name="Lifecycle">出典ファイルの区分（<c>current</c> / <c>archived</c> / <c>rejected</c>）。</param>
public sealed record HitDetail(
    string Id,
    string Kind,
    string Title,
    string Snippet,
    string? Body,
    double Score,
    string Path,
    int StartLine,
    int EndLine,
    string? Revision,
    string? RelType,
    string? Basis,
    string Lifecycle)
{
    /// <summary>
    /// 検索ヒットから変換する。
    /// </summary>
    public static HitDetail From(SearchHit hit, string? body = null, string? relType = null, string? basis = null)
        => new(
            hit.Id,
            hit.Kind.ToString(),
            hit.Title,
            hit.Snippet,
            body,
            hit.Score,
            hit.Source.Path,
            hit.Source.StartLine,
            hit.Source.EndLine,
            hit.Source.Revision,
            relType,
            basis,
            LifecycleText.ToText(hit.Lifecycle));
}
