using Melore.Core.Models;

namespace Melore.Core.Ports;

/// <summary>
/// 対象 Git リポジトリの列挙とリビジョン取得。
/// </summary>
public interface IGitWorkspace
{
    /// <summary>HEAD の SHA。Git でない場合は null。</summary>
    Task<string?> GetHeadRevisionAsync(CancellationToken cancellationToken);

    /// <summary>include/exclude 適用後の対象ファイル。</summary>
    Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="fromRevision"/> から HEAD までのリネーム。from が空なら空集合。
    /// </summary>
    Task<IReadOnlyList<GitRename>> ListRenamesAsync(string? fromRevision, CancellationToken cancellationToken);

    /// <summary>
    /// include に合う作業ツリー差分。未追跡を含む。Git でなければ空。
    /// </summary>
    Task<IReadOnlyList<WorktreeChange>> ListWorktreeChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 相対パスを 1 ファイルとして読む。対象外か欠けるときは null。
    /// </summary>
    Task<RepositoryFile?> TryReadFileAsync(string relativePath, CancellationToken cancellationToken);
}
