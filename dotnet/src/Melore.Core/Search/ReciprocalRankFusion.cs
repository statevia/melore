using Melore.Contracts;

namespace Melore.Core.Search;

/// <summary>
/// 複数ランキングを RRF で統合する。
/// </summary>
public static class ReciprocalRankFusion
{
    /// <summary>標準的な定数 k=60。</summary>
    public const int DefaultK = 60;

    /// <summary>
    /// 同一 ID のヒットを融合し、スコア降順で返す。
    /// </summary>
    public static IReadOnlyList<SearchHit> Merge(
        IReadOnlyList<IReadOnlyList<SearchHit>> rankedLists,
        int limit,
        int k = DefaultK)
    {
        var scores = new Dictionary<string, (SearchHit Hit, double Score)>(StringComparer.Ordinal);
        foreach (var list in rankedLists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var hit = list[rank];
                var add = 1.0 / (k + rank + 1);
                if (scores.TryGetValue(hit.Id, out var existing))
                {
                    scores[hit.Id] = (existing.Hit, existing.Score + add);
                }
                else
                {
                    scores[hit.Id] = (hit, add);
                }
            }
        }

        return scores.Values
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Hit with { Score = x.Score })
            .ToList();
    }
}
