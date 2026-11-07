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
    /// ID・FQN・名前のいずれかで関連を返す。同じヒットは 1 件にまとめ、<paramref name="limit"/> 件で打ち切る。
    /// </summary>
    public IReadOnlyList<RelatedHit> GetReferences(string idOrFqn, int limit)
    {
        store.Initialize();
        var key = idOrFqn.Trim();
        if (key.Length == 0)
        {
            return [];
        }

        var byId = store.GetHitById(key);
        var symbols = store.FindSymbols(key, 8);
        var focus = byId ?? (symbols is [var first, ..] ? first : null);
        if (focus is null)
        {
            return [];
        }

        return RelatedTo(focus)
            .DistinctBy(item => item.Hit.Id)
            .Take(limit)
            .ToList();
    }

    private IEnumerable<RelatedHit> RelatedTo(SearchHit focus)
    {
        var stored = store.GetStoredRelationships(focus.Id);
        return focus.Kind switch
        {
            HitKind.DocumentChunk => store.GetChunksInSameDocument(focus.Id).Concat(stored),
            HitKind.Symbol or HitKind.Test => stored
                .Concat(store.GetDeclaredMembers(focus.Title))
                .Concat(store.GetTestsInSameFile(focus.Source.Path)),
            _ => stored
        };
    }
}
