using Melore.Contracts;
using Melore.Core.Ports;
using Melore.Core.Search;

namespace Melore.Tests;

public sealed class RerankerTests
{
    /// <summary>既定実装は専用モデルを使わず、入力順を保つ。</summary>
    [Fact]
    public void NoOpReranker_KeepsOrderWithoutModel()
    {
        // Arrange
        var reranker = new NoOpReranker();
        var hits = new[]
        {
            Hit("1", "A"),
            Hit("2", "B"),
            Hit("3", "C")
        };

        // Act
        var ranked = reranker.Rerank("q", hits, limit: 2);

        // Assert
        Assert.False(reranker.IsModelBacked);
        Assert.Equal(["1", "2"], ranked.Select(h => h.Id));
    }

    /// <summary>SearchService は RRF の後に IReranker を呼ぶ。</summary>
    [Fact]
    public async Task SearchService_AppliesRerankerAfterFusion()
    {
        // Arrange
        var captured = new List<string>();
        IReranker reranker = new CapturingReranker(captured);
        using var store = new Melore.Infrastructure.Sqlite.SqliteMeloreStore(
            Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db"),
            "fixture",
            "C:\\tmp\\fixture");
        store.Initialize();
        var search = new SearchService(store, new NullEmbeddingClient(), reranker);

        // Act
        var hits = await search.SearchAsync("nothing-at-all", 4, SearchScope.Current, CancellationToken.None);

        // Assert
        Assert.Empty(hits);
        Assert.Contains("nothing-at-all", captured);
    }

    private static SearchHit Hit(string id, string title)
        => new(id, HitKind.DocumentChunk, title, title, 0, new SourceSpan("docs/a.md", 1, 2, "abc"), DocumentLifecycle.Current);

    private sealed class CapturingReranker(List<string> queries) : IReranker
    {
        public bool IsModelBacked => false;

        public IReadOnlyList<SearchHit> Rerank(string query, IReadOnlyList<SearchHit> hits, int limit)
        {
            queries.Add(query);
            return hits.Take(limit).ToList();
        }
    }
}
