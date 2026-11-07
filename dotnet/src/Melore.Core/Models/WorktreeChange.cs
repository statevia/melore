namespace Melore.Core.Models;

/// <summary>
/// 作業ツリー上の 1 パス。削除なら索引から外す。
/// </summary>
/// <param name="RelativePath">リポジトリ相対パス。</param>
/// <param name="Remove">ファイルが消えた、またはリネーム元。</param>
public sealed record WorktreeChange(string RelativePath, bool Remove);
