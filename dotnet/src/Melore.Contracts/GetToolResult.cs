namespace Melore.Contracts;

/// <summary>
/// MCP の <c>get</c> の応答。ID で引いた 1 ヒットに鮮度を添える。
/// </summary>
/// <param name="Hit">ID に一致したヒット。本文は <c>includeBody</c> を指定したときだけ入る。</param>
/// <param name="Dirty">差分が多すぎて再索引していない。</param>
/// <param name="DirtyFileCount">対象になった差分ファイル数。</param>
/// <param name="RefreshedFileCount">今回反映したファイル数。</param>
public sealed record GetToolResult(
    HitDetail Hit,
    bool Dirty,
    int DirtyFileCount,
    int RefreshedFileCount);
