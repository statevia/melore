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
    /// <param name="query">利用者のクエリ。</param>
    /// <param name="limit">返す最大件数。</param>
    /// <param name="scope">
    /// 候補に入れる区分の範囲。既定値は持たせず、呼び出し側が明示する。
    /// 絞り込みは全文・シンボル・ベクトルのそれぞれで候補を取る段階で行う（融合の後に除外すると、
    /// 候補の上限を履歴文書が占めて既定のスコープの結果が崩れるため）。
    /// </param>
    /// <param name="cancellationToken">キャンセル。</param>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        int limit,
        SearchScope scope,
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
        var lifecycles = SearchScopeResolver.Resolve(scope);
        var ftsLists = variants.Select(v => store.SearchFullText(v, pool, lifecycles)).ToList();
        var symbolLists = variants.Select(v => store.SearchSymbols(v, pool, lifecycles)).ToList();
        IReadOnlyList<SearchHit> vectors = [];
        if (embeddings.IsAvailable && store.VectorSearchEnabled)
        {
            var vector = await embeddings.EmbedAsync(trimmed, isQuery: true, cancellationToken)
                .ConfigureAwait(false);
            if (vector is { Length: > 0 })
            {
                vectors = store.SearchVectors(vector, pool, lifecycles);
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
