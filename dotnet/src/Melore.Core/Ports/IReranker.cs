using Melore.Contracts;

namespace Melore.Core.Ports;

/// <summary>
/// 統合後ヒットの並べ替え。専用モデルはゲート付き。
/// </summary>
public interface IReranker
{
    /// <summary>学習済み rerank モデルを使うなら true。既定の RRF パスは false。</summary>
    bool IsModelBacked { get; }

    /// <summary>
    /// クエリに対してヒットを並べ直す。件数は <paramref name="limit"/> 以下。
    /// </summary>
    IReadOnlyList<SearchHit> Rerank(string query, IReadOnlyList<SearchHit> hits, int limit);
}
