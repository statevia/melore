using Melore.Core.Search;

namespace Melore.Core.Evaluation;

/// <summary>
/// ゴールデン Hit@8 を測る。
/// </summary>
/// <param name="search">統合検索。</param>
public sealed class EvalService(SearchService search)
{
    /// <summary>
    /// 各クエリの上位 <paramref name="limit"/> 件を評価する。
    /// </summary>
    public async Task<EvalReport> RunAsync(int limit, CancellationToken cancellationToken)
    {
        var cap = Math.Clamp(limit, 1, 32);
        var rows = new List<EvalRow>();
        foreach (var item in GoldenQueries.All)
        {
            var hits = await search.SearchAsync(item.Query, cap, cancellationToken).ConfigureAwait(false);
            var hit = hits.Any(h => item.ExpectedPathSubstrings.Any(s =>
                h.Source.Path.Contains(s, StringComparison.OrdinalIgnoreCase)));
            rows.Add(new EvalRow(item.Id, item.Query, hit, hits.Select(h => h.Source.Path).ToList()));
        }

        var hitsAtK = rows.Count == 0 ? 0 : rows.Count(r => r.Hit) / (double)rows.Count;
        return new EvalReport(cap, hitsAtK, rows);
    }
}

/// <summary>
/// ゴールデン評価の集計。
/// </summary>
public sealed record EvalReport(int Limit, double HitAtK, IReadOnlyList<EvalRow> Rows);

/// <summary>
/// 1 クエリの結果。
/// </summary>
public sealed record EvalRow(string Id, string Query, bool Hit, IReadOnlyList<string> Paths);
