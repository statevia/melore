using Melore.Core.Evaluation;
using Melore.Core.Search;
using Melore.Infrastructure.Sqlite;
using Melore.Parsing.Markdown;

namespace Melore.Tests;

public sealed class EvalServiceTests
{
    /// <summary>ゴールデンは 15 件で ID が重複しない。</summary>
    [Fact]
    public void GoldenQueries_HasFifteenUniqueIds()
    {
        // Assert
        Assert.Equal(15, GoldenQueries.All.Count);
        Assert.Equal(15, GoldenQueries.All.Select(q => q.Id).Distinct().Count());
    }

    /// <summary>空インデックスでは Hit@8 が 0。</summary>
    [Fact]
    public async Task Eval_EmptyStore_IsZero()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var eval = new EvalService(new SearchService(store, new Melore.Core.Ports.NullEmbeddingClient(), new NoOpReranker()));

        // Act
        var report = await eval.RunAsync(8, CancellationToken.None);

        // Assert
        Assert.Equal(0, report.HitAtK);
        Assert.Equal(15, report.Rows.Count);
        Assert.All(report.Rows, r => Assert.False(r.Hit));
    }

    /// <summary>Q1 用チャンクがあればその行は HIT。</summary>
    [Fact]
    public async Task Eval_ForkJoinChunk_HitsQ1()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var file = new Melore.Core.Models.RepositoryFile(
            "docs/specifications/execution/fork-join.md",
            "C:\\tmp\\fork-join.md",
            "sha",
            "markdown");
        store.UpsertDocument(
            store.EnsureFile(file),
            file,
            new MarkdownDocumentParser().Parse(file.RelativePath, "# Fork Join\n\nHosted Fork の親 graph。"),
            "rev");
        var eval = new EvalService(new SearchService(store, new Melore.Core.Ports.NullEmbeddingClient(), new NoOpReranker()));

        // Act
        var report = await eval.RunAsync(8, CancellationToken.None);

        // Assert
        Assert.True(report.Rows.Single(r => r.Id == "Q1").Hit);
    }
}
