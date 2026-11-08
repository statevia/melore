using System.Collections.Frozen;
using Melore.Contracts;

namespace Melore.Core.Search;

/// <summary>
/// 検索スコープを、候補に入れる区分の集合に変える。
/// </summary>
/// <remarks>
/// store はスコープの意味を知らず、区分の集合だけを受け取る。スコープを増やすときはここだけを直す。
/// </remarks>
public static class SearchScopeResolver
{
    private static readonly FrozenSet<DocumentLifecycle> CurrentOnly =
        new[] { DocumentLifecycle.Current }.ToFrozenSet();

    private static readonly FrozenSet<DocumentLifecycle> HistoryOnly =
        new[] { DocumentLifecycle.Archived, DocumentLifecycle.Rejected }.ToFrozenSet();

    private static readonly FrozenSet<DocumentLifecycle> Everything =
        Enum.GetValues<DocumentLifecycle>().ToFrozenSet();

    /// <summary>スコープに対応する区分の集合。</summary>
    /// <exception cref="ArgumentOutOfRangeException">未定義のスコープ値のとき。</exception>
    public static IReadOnlySet<DocumentLifecycle> Resolve(SearchScope scope) => scope switch
    {
        SearchScope.Current => CurrentOnly,
        SearchScope.History => HistoryOnly,
        SearchScope.All => Everything,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
    };
}
