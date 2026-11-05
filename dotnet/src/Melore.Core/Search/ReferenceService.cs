using Melore.Contracts;
using Melore.Core.Ports;

namespace Melore.Core.Search;

/// <summary>
/// 仕様・シンボルに関連するドキュメント・実装・テストを列挙する。
/// </summary>
/// <param name="store">索引。</param>
public sealed class ReferenceService(IMeloreStore store)
{
    /// <summary>
    /// ID・FQN・名前のいずれかで関連を返す。
    /// </summary>
    public IReadOnlyList<RelatedHit> GetReferences(string idOrFqn, int limit)
    {
        store.Initialize();
        var key = idOrFqn.Trim();
        if (key.Length == 0)
        {
            return [];
        }

        var results = new List<RelatedHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddRange(IEnumerable<RelatedHit> items)
        {
            foreach (var item in items)
            {
                if (seen.Add(item.Hit.Id))
                {
                    results.Add(item);
                    if (results.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }

        var byId = store.GetHitById(key);
        var symbols = store.FindSymbols(key, 8);
        if (byId is { Kind: HitKind.DocumentChunk })
        {
            AddRange(store.GetChunksInSameDocument(byId.Id));
            AddRange(store.GetStoredRelationships(byId.Id));
            return results;
        }

        var focus = byId ?? symbols.FirstOrDefault();
        if (focus is null)
        {
            return [];
        }

        AddRange(store.GetStoredRelationships(focus.Id));
        if (focus.Kind is HitKind.Symbol or HitKind.Test)
        {
            AddRange(store.GetDeclaredMembers(focus.Title));
            AddRange(store.GetTestsInSameFile(focus.Source.Path));
        }

        return results;
    }
}
