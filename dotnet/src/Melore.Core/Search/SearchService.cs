using Melore.Contracts;
using Melore.Core.Ports;

namespace Melore.Core.Search;

/// <summary>
/// FTS・シンボル・ベクトルを統合する。
/// </summary>
/// <param name="store">索引。</param>
/// <param name="embeddings">クエリ側 Embedding。不可ならベクトル枝を省略。</param>
/// <param name="reranker">RRF 後の並べ替え。既定は専用モデルなし。</param>
public sealed class SearchService(IMeloreStore store, IEmbeddingClient embeddings, IReranker reranker)
{
    /// <summary>
    /// 自然言語またはキーワードで検索する。
    /// </summary>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        store.Initialize();
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var pool = Math.Clamp(limit * 4, limit, 32);
        var variants = QueryNormalizer.Expand(trimmed);
        var ftsLists = variants.Select(v => store.SearchFullText(v, pool)).ToList();
        var symbolLists = variants.Select(v => store.SearchSymbols(v, pool)).ToList();
        IReadOnlyList<SearchHit> vectors = [];
        if (embeddings.IsAvailable && store.VectorSearchEnabled)
        {
            var vector = await embeddings.EmbedAsync(trimmed, isQuery: true, cancellationToken)
                .ConfigureAwait(false);
            if (vector is { Length: > 0 })
            {
                vectors = store.SearchVectors(vector, pool);
            }
        }

        var ranked = new List<IReadOnlyList<SearchHit>>();
        ranked.AddRange(ftsLists);
        ranked.AddRange(symbolLists);
        ranked.Add(vectors);
        var merged = ReciprocalRankFusion.Merge(ranked, pool);
        var reranked = reranker.Rerank(trimmed, merged, pool);
        return TestRankDemoter.Apply(trimmed, reranked).Take(limit).ToList();
    }

    /// <summary>索引の状態。</summary>
    public IndexStatus Status(string repositoryName)
    {
        store.Initialize();
        return store.GetStatus(repositoryName);
    }
}
