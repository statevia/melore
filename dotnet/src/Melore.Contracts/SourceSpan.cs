namespace Melore.Contracts;

/// <summary>
/// ファイル上の出典範囲。行番号は 1-origin で両端を含む。
/// </summary>
/// <param name="Path">リポジトリ相対パス（`/` 区切り）。</param>
/// <param name="StartLine">開始行。</param>
/// <param name="EndLine">終了行。</param>
/// <param name="Revision">解析時の Git SHA。未コミットのみの場合は null。</param>
public sealed record SourceSpan(
    string Path,
    int StartLine,
    int EndLine,
    string? Revision);
