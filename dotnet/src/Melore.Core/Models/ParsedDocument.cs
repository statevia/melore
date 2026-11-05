namespace Melore.Core.Models;

/// <summary>
/// Markdown 1 ファイルの解析結果。
/// </summary>
public sealed record ParsedDocument(
    string Title,
    string DocKind,
    IReadOnlyList<DocumentChunk> Chunks,
    IReadOnlyList<DocumentLink> Links);

/// <summary>
/// 見出しセクション。
/// </summary>
public sealed record DocumentChunk(
    string HeadingPath,
    string? SpecId,
    string Text,
    int StartLine,
    int EndLine,
    int Ordinal);

/// <summary>
/// Markdown 相対リンク。
/// </summary>
public sealed record DocumentLink(string TargetRelativePath, int Line);
