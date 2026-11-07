using Melore.Contracts;
using Melore.Core.Ports;

namespace Melore.Core.Search;

/// <summary>
/// 所属・継承・解決済み呼び出しを depth と応答サイズで切ってたどる。
/// </summary>
/// <param name="store">索引。</param>
public sealed class TraceService(IMeloreStore store)
{
    /// <summary>既定のノード数。</summary>
    public const int DefaultLimit = 64;

    /// <summary>ノードと辺の概算文字数の上限。</summary>
    public const int MaxResponseChars = 8000;

    /// <summary>
    /// FQN または ID からグラフを作る。
    /// </summary>
    public TraceResult Trace(
        string idOrFqn,
        int depth,
        int limit = DefaultLimit,
        IReadOnlyCollection<string>? relTypes = null)
    {
        store.Initialize();
        var key = idOrFqn.Trim();
        var capDepth = Math.Clamp(depth, 1, 6);
        var capNodes = Math.Clamp(limit, 1, 256);
        var allowed = relTypes is null || relTypes.Count == 0
            ? null
            : new HashSet<string>(relTypes, StringComparer.OrdinalIgnoreCase);
        var rootHit = store.GetHitById(key) ?? store.FindSymbols(key, 1).FirstOrDefault();
        if (rootHit is null)
        {
            return new TraceResult(null, [], [], "指定したシンボルが見つかりません。");
        }

        var root = ToNode(rootHit);
        var nodes = new Dictionary<string, TraceNode>(StringComparer.Ordinal) { [root.Id] = root };
        var edges = new List<TraceEdge>();
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((root.Id, 0));
        var chars = Size(root);
        var truncated = false;

        while (queue.Count > 0)
        {
            var (currentId, currentDepth) = queue.Dequeue();
            if (currentDepth >= capDepth)
            {
                continue;
            }

            var current = nodes[currentId];
            var related = store.GetStoredRelationships(currentId)
                .Concat(store.GetDeclaredMembers(current.Name))
                .ToList();
            foreach (var item in related)
            {
                if (allowed is not null && !allowed.Contains(item.RelType))
                {
                    continue;
                }

                var next = ToNode(item.Hit);
                var isNew = !nodes.ContainsKey(next.Id);
                var addChars = item.RelType.Length + next.Id.Length + currentId.Length + 16 + (isNew ? Size(next) : 0);
                if (isNew && (nodes.Count >= capNodes || chars + addChars > MaxResponseChars))
                {
                    truncated = true;
                    continue;
                }

                edges.Add(new TraceEdge(currentId, next.Id, item.RelType, item.Basis));
                chars += addChars;
                if (isNew)
                {
                    nodes.Add(next.Id, next);
                    queue.Enqueue((next.Id, currentDepth + 1));
                }
            }
        }

        string? limitation = store.CountRelationships("calls", null) == 0
            ? "呼び出し関係（calls）は未抽出です。所属（declares）と基底型名（inherits, inferred）を返します。"
            : null;
        if (truncated)
        {
            const string note = "応答上限で打ち切りました。";
            limitation = limitation is null ? note : limitation + note;
        }

        return new TraceResult(root, nodes.Values.ToList(), Deduplicate(edges), limitation);
    }

    private static TraceNode ToNode(SearchHit hit)
        => new(hit.Id, hit.Kind.ToString(), hit.Title, hit.Source.Path, hit.Source.StartLine, hit.Source.EndLine);

    private static int Size(TraceNode node)
        => node.Id.Length + node.Kind.Length + node.Name.Length + node.Path.Length + 24;

    private static IReadOnlyList<TraceEdge> Deduplicate(IReadOnlyList<TraceEdge> edges)
        => edges
            .DistinctBy(e => e.FromId + "\t" + e.ToId + "\t" + e.RelType)
            .ToList();
}
