using Melore.Contracts;
using Melore.Core.Ports;

namespace Melore.Core.Search;

/// <summary>
/// 専用 rerank モデルを使わず、入力順（RRF 済み）を保つ。
/// </summary>
public sealed class NoOpReranker : IReranker
{
    /// <inheritdoc />
    public bool IsModelBacked => false;

    /// <inheritdoc />
    public IReadOnlyList<SearchHit> Rerank(string query, IReadOnlyList<SearchHit> hits, int limit)
        => hits.Take(limit).ToList();
}
