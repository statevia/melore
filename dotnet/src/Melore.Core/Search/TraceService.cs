using Melore.Contracts;
using Melore.Core.Ports;

namespace Melore.Core.Search;

/// <summary>
/// 所属・継承を depth 制限付きでたどる。呼び出し関係は未抽出。
/// </summary>
/// <param name="store">索引。</param>
public sealed class TraceService(IMeloreStore store)
{
    /// <summary>
    /// FQN または ID からグラフを作る。
    /// </summary>
    public TraceResult Trace(string idOrFqn, int depth)
    {
        store.Initialize();
        var key = idOrFqn.Trim();
        var cap = Math.Clamp(depth, 1, 6);
        var root = store.GetHitById(key) ?? store.FindSymbols(key, 1).FirstOrDefault();
        if (root is null)
        {
            return new TraceResult(null, [], [], "指定したシンボルが見つかりません。");
        }

        var nodes = new Dictionary<string, SearchHit>(StringComparer.Ordinal) { [root.Id] = root };
        var edges = new List<TraceEdge>();
        var queue = new Queue<(SearchHit Hit, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (current, currentDepth) = queue.Dequeue();
            if (currentDepth >= cap)
            {
                continue;
            }

            var related = store.GetStoredRelationships(current.Id)
                .Concat(store.GetDeclaredMembers(current.Title))
                .ToList();
            foreach (var item in related)
            {
                edges.Add(new TraceEdge(current.Id, item.Hit.Id, item.RelType, item.Basis));
                if (nodes.TryAdd(item.Hit.Id, item.Hit))
                {
                    queue.Enqueue((item.Hit, currentDepth + 1));
                }
            }
        }

        return new TraceResult(
            root,
            nodes.Values.ToList(),
            Deduplicate(edges),
            "呼び出し関係（calls）は構文解析のみのため未抽出です。所属（declares）と基底型名（inherits, inferred）を返します。");
    }

    private static IReadOnlyList<TraceEdge> Deduplicate(IReadOnlyList<TraceEdge> edges)
        => edges
            .DistinctBy(e => e.FromId + "\t" + e.ToId + "\t" + e.RelType)
            .ToList();
}
