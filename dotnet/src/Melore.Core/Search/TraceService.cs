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
        var rootHit = store.GetHitById(key) ?? (store.FindSymbols(key, 1) is [var first, ..] ? first : null);
        if (rootHit is null)
        {
            return new TraceResult(null, [], [], "指定したシンボルが見つかりません。");
        }

        var root = ToNode(rootHit);
        var walk = new TraceWalk(root, capNodes, AllowedTypes(relTypes));
        while (walk.TryNext(out var current, out var currentDepth))
        {
            if (currentDepth < capDepth)
            {
                var related = store.GetStoredRelationships(current.Id)
                    .Concat(store.GetDeclaredMembers(current.Name))
                    .ToList();
                walk.Expand(current, currentDepth, related);
            }
        }

        return new TraceResult(root, walk.ListNodes(), Deduplicate(walk.Edges), Limitation(walk.Truncated));
    }

    private static HashSet<string>? AllowedTypes(IReadOnlyCollection<string>? relTypes)
        => relTypes is null || relTypes.Count == 0
            ? null
            : new HashSet<string>(relTypes, StringComparer.OrdinalIgnoreCase);

    private string? Limitation(bool truncated)
    {
        var limitation = store.CountRelationships("calls", null) == 0
            ? "呼び出し関係（calls）は未抽出です。所属（declares）と基底型名（inherits, inferred）を返します。"
            : null;
        return truncated ? limitation + "応答上限で打ち切りました。" : limitation;
    }

    private static TraceNode ToNode(SearchHit hit)
        => new(hit.Id, hit.Kind.ToString(), hit.Title, hit.Source.Path, hit.Source.StartLine, hit.Source.EndLine);

    private static List<TraceEdge> Deduplicate(List<TraceEdge> edges)
        => edges
            .DistinctBy(e => e.FromId + "\t" + e.ToId + "\t" + e.RelType)
            .ToList();

    /// <summary>
    /// 幅優先の走査状態。ノード数と概算文字数の上限を超える新規ノードは足さず、打ち切りとして記録する。
    /// </summary>
    /// <param name="root">起点ノード。</param>
    /// <param name="capNodes">ノード数の上限。</param>
    /// <param name="allowed">残す関係の種類。null なら全部。</param>
    private sealed class TraceWalk(TraceNode root, int capNodes, HashSet<string>? allowed)
    {
        private readonly Dictionary<string, TraceNode> _nodes = new(StringComparer.Ordinal) { [root.Id] = root };
        private readonly Queue<(TraceNode Node, int Depth)> _queue = new([(root, 0)]);
        private int _chars = Size(root);

        public List<TraceEdge> Edges { get; } = [];

        public bool Truncated { get; private set; }

        public List<TraceNode> ListNodes() => _nodes.Values.ToList();

        public bool TryNext(out TraceNode node, out int depth)
        {
            var found = _queue.TryDequeue(out var next);
            (node, depth) = next;
            return found;
        }

        public void Expand(TraceNode current, int depth, IEnumerable<RelatedHit> related)
        {
            foreach (var item in related.Where(r => allowed is null || allowed.Contains(r.RelType)))
            {
                var next = ToNode(item.Hit);
                var isNew = !_nodes.ContainsKey(next.Id);
                var addChars = item.RelType.Length + next.Id.Length + current.Id.Length + 16 + (isNew ? Size(next) : 0);
                if (isNew && (_nodes.Count >= capNodes || _chars + addChars > MaxResponseChars))
                {
                    Truncated = true;
                    continue;
                }

                Edges.Add(new TraceEdge(current.Id, next.Id, item.RelType, item.Basis));
                _chars += addChars;
                if (isNew)
                {
                    _nodes.Add(next.Id, next);
                    _queue.Enqueue((next, depth + 1));
                }
            }
        }

        private static int Size(TraceNode node)
            => node.Id.Length + node.Kind.Length + node.Name.Length + node.Path.Length + 24;
    }
}
