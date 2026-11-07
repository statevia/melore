namespace Melore.Contracts;

/// <summary>
/// 検索前に見た作業ツリーと索引の差。
/// </summary>
/// <param name="Dirty">上限を超えて再索引しなかったとき true。</param>
/// <param name="DirtyFileCount">include に合う差分ファイル数。</param>
/// <param name="RefreshedFileCount">今回索引へ反映したファイル数。</param>
public sealed record IndexFreshness(bool Dirty, int DirtyFileCount, int RefreshedFileCount);
