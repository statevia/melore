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
        var bundle = await eval.RunAsync(8, CancellationToken.None);

        // Assert
        Assert.Equal(0, bundle.Dev.HitAt8);
        Assert.Equal(0, bundle.Test.HitAt1);
        Assert.Equal(15, bundle.Dev.Rows.Count);
        Assert.Equal(27, bundle.Test.Rows.Count);
        Assert.All(bundle.Dev.Rows, r => Assert.False(r.HitAt8));
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
        var bundle = await eval.RunAsync(8, CancellationToken.None);

        // Assert
        Assert.True(bundle.Dev.Rows.Single(r => r.Id == "Q1").HitAt8);
    }

    /// <summary>検証用は 27 件で H12 を含まない。</summary>
    [Fact]
    public void HoldoutQueries_ExcludesUntrackedFixture()
    {
        // Assert
        Assert.Equal(27, HoldoutQueries.All.Count);
        Assert.DoesNotContain(HoldoutQueries.All, q => q.Id == "H12");
        Assert.Equal(27, HoldoutQueries.All.Select(q => q.Id).Distinct().Count());
    }
}
