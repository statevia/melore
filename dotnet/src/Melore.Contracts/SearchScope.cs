namespace Melore.Contracts;

/// <summary>
/// 検索で候補に入れる区分の範囲。
/// </summary>
/// <remarks>
/// 既定は <see cref="Current"/>。履歴文書は件数が多く、現行の文書と混ぜると上位を奪うため、
/// 経緯を調べるときだけ <see cref="History"/> を指定する。
/// </remarks>
public enum SearchScope
{
    /// <summary>現行（<see cref="DocumentLifecycle.Current"/>）だけ。</summary>
    Current,

    /// <summary>履歴文書（<see cref="DocumentLifecycle.Archived"/> と <see cref="DocumentLifecycle.Rejected"/>）だけ。</summary>
    History,

    /// <summary>すべての区分。順位の補正はしない。</summary>
    All
}
