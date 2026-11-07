namespace Melore.Contracts;

/// <summary>
/// MCP / CLI の検索応答。ヒットに鮮度を添える。
/// </summary>
/// <param name="Hits">順位付きヒット。</param>
/// <param name="Dirty">差分が多すぎて再索引していない。</param>
/// <param name="DirtyFileCount">対象になった差分ファイル数。</param>
/// <param name="RefreshedFileCount">今回反映したファイル数。</param>
public sealed record SearchToolResult(
    IReadOnlyList<HitDetail> Hits,
    bool Dirty,
    int DirtyFileCount,
    int RefreshedFileCount);
