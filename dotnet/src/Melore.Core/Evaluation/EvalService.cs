using Melore.Core.Search;

namespace Melore.Core.Evaluation;

/// <summary>
/// 調整用と検証用の Hit@1 / Hit@8 を測る。
/// </summary>
/// <param name="search">統合検索。</param>
public sealed class EvalService(SearchService search)
{
    /// <summary>検証用 Hit@8 の合格ライン。</summary>
    public const double TestHitAt8Threshold = 0.93;

    /// <summary>検証用 Hit@1 の合格ライン。</summary>
    public const double TestHitAt1Threshold = 0.65;

    /// <summary>
    /// dev と test を別々に評価する。
    /// </summary>
    public async Task<EvalBundle> RunAsync(int limit, CancellationToken cancellationToken)
    {
        var cap = Math.Clamp(limit, 1, 32);
        var dev = await ScoreAsync("dev", GoldenQueries.All, cap, cancellationToken).ConfigureAwait(false);
        var test = await ScoreAsync("test", HoldoutQueries.All, cap, cancellationToken).ConfigureAwait(false);
        return new EvalBundle(dev, test);
    }

    private async Task<EvalReport> ScoreAsync(
        string name,
        IReadOnlyList<GoldenQuery> queries,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<EvalRow>();
        foreach (var item in queries)
        {
            var hits = await search.SearchAsync(item.Query, limit, cancellationToken).ConfigureAwait(false);
            var paths = hits.Select(h => h.Source.Path).ToList();
            var hitAt1 = paths.Count > 0 && Matches(item, paths[0]);
            var hitAt8 = paths.Take(8).Any(path => Matches(item, path));
            rows.Add(new EvalRow(item.Id, item.Query, hitAt1, hitAt8, paths));
        }

        var hit1 = rows.Count == 0 ? 0 : rows.Count(r => r.HitAt1) / (double)rows.Count;
        var hit8 = rows.Count == 0 ? 0 : rows.Count(r => r.HitAt8) / (double)rows.Count;
        return new EvalReport(name, limit, hit1, hit8, rows);
    }

    private static bool Matches(GoldenQuery item, string path)
        => item.ExpectedPathSubstrings.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase));
}

/// <summary>調整用と検証用。</summary>
public sealed record EvalBundle(EvalReport Dev, EvalReport Test);

/// <summary>1 セットの集計。</summary>
public sealed record EvalReport(
    string Name,
    int Limit,
    double HitAt1,
    double HitAt8,
    IReadOnlyList<EvalRow> Rows);

/// <summary>1 クエリの結果。</summary>
public sealed record EvalRow(
    string Id,
    string Query,
    bool HitAt1,
    bool HitAt8,
    IReadOnlyList<string> Paths);
