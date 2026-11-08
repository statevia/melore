using Melore.Contracts;
using Melore.Core.Models;
using Melore.Infrastructure.Sqlite;

namespace Melore.Tests;

public sealed class SqliteMeloreStoreTests
{
    private static readonly HashSet<DocumentLifecycle> CurrentOnly = [DocumentLifecycle.Current];

    /// <summary>チャンクを入れて FTS で取り出せる。</summary>
    [Fact]
    public void SearchFullText_FindsIndexedHeading()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var file = new RepositoryFile("docs/specifications/execution/fork-join.md", "C:\\tmp\\fixture\\docs\\x.md", "sha", "markdown");
        var fileId = store.EnsureFile(file);
        store.UpsertDocument(
            fileId,
            file,
            new ParsedDocument(
                "Fork Join",
                "specification",
                [
                    new DocumentChunk("Specifications / Fork Join", null, "Hosted Fork の親 graph を合成する。", 10, 20, 0)
                ],
                []),
            "deadbeef");

        // Act
        var hits = store.SearchFullText("Fork Join", 8, CurrentOnly);

        // Assert
        var hit = Assert.Single(hits);
        Assert.Equal("docs/specifications/execution/fork-join.md", hit.Source.Path);
        Assert.Equal(10, hit.Source.StartLine);
        Assert.Equal(20, hit.Source.EndLine);
        Assert.Contains("親 graph", hit.Snippet, StringComparison.Ordinal);
    }

    /// <summary>blob SHA が同じなら差分対象から除外できる。</summary>
    [Fact]
    public void GetIndexedBlobShas_ReturnsStoredHash()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var file = new RepositoryFile("docs/a.md", "C:\\tmp\\a.md", "abc123", "markdown");
        store.EnsureFile(file);

        // Act
        var map = store.GetIndexedBlobShas();

        // Assert
        Assert.Equal("abc123", map["docs/a.md"]);
    }

    /// <summary>`$.vars` が `$` と `vars` のトークンとしてヒットする。</summary>
    [Fact]
    public void SearchFullText_FindsDollarVarsPath()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var file = new RepositoryFile("docs/specifications/definition.md", "C:\\tmp\\def.md", "sha", "markdown");
        store.UpsertDocument(
            store.EnsureFile(file),
            file,
            new ParsedDocument(
                "Definition",
                "specification",
                [
                    new DocumentChunk(
                        "output フィールド",
                        null,
                        "State 完了時に戻り値を $.vars へ代入する。許可は $.vars.user。",
                        456,
                        462,
                        0)
                ],
                []),
            "deadbeef");

        // Act
        var dollarVars = store.SearchFullText("$.vars", 8, CurrentOnly);
        var varsOnly = store.SearchFullText("vars", 8, CurrentOnly);

        // Assert
        Assert.Equal("\"$\" AND \"vars\"", SqliteMeloreStore.ToFtsQuery("$.vars"));
        Assert.Contains(dollarVars, h => h.Source.Path.EndsWith("definition.md", StringComparison.Ordinal));
        Assert.Contains(varsOnly, h => h.Snippet.Contains("$.vars", StringComparison.Ordinal));
    }

    /// <summary>助詞付きクエリでも見出しに当たる。</summary>
    [Fact]
    public void SearchFullText_IgnoresStopwordsInQuery()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var file = new RepositoryFile("docs/specifications/execution/fork-join.md", "C:\\tmp\\x.md", "sha", "markdown");
        store.UpsertDocument(
            store.EnsureFile(file),
            file,
            new ParsedDocument(
                "Fork Join",
                "specification",
                [new DocumentChunk("Specifications / Fork Join", null, "Hosted Fork", 1, 2, 0)],
                []),
            "rev");

        // Act
        var hits = store.SearchFullText("Fork Join の", 8, CurrentOnly);

        // Assert
        Assert.Contains(hits, h => h.Source.Path.Contains("fork-join", StringComparison.Ordinal));
        Assert.Equal("\"Fork\" AND \"Join\"", SqliteMeloreStore.ToFtsQuery("Fork Join の"));
    }
}
