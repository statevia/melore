using Melore.Contracts;

namespace Melore.Core.Models;

/// <summary>
/// インデックス対象の 1 ファイル。
/// </summary>
/// <param name="RelativePath">`/` 区切りの相対パス。</param>
/// <param name="FullPath">絶対パス。</param>
/// <param name="BlobSha">Git blob SHA。未追跡なら内容ハッシュ。</param>
/// <param name="Language"><c>markdown</c> または <c>csharp</c>。</param>
/// <param name="Lifecycle">パスと <c>history</c> の glob から決めた区分。<c>history</c> が無い設定では常に現行。</param>
public sealed record RepositoryFile(
    string RelativePath,
    string FullPath,
    string BlobSha,
    string Language,
    DocumentLifecycle Lifecycle = DocumentLifecycle.Current);
