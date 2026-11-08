using System.Text.Json;
using Melore.Cli;
using Melore.Contracts;
using Melore.Core.Indexing;
using Melore.Core.Models;
using Melore.Core.Ports;
using Melore.Core.Search;
using Melore.Host;
using Melore.Infrastructure.Git;
using Melore.Infrastructure.Sqlite;
using Melore.Parsing.Markdown;
using Melore.Parsing.Roslyn;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Melore.Tests;

/// <summary>
/// 履歴文書（archive / reject）の区分と、検索スコープによる絞り込み。
/// </summary>
public sealed class HistoryDocumentScopeTests
{
    private static readonly HistorySettings StateviaLikeHistory = new()
    {
        Archived = [".spec/archive/**"],
        Rejected = [".spec/reject/**"]
    };

    /// <summary>archived の glob に当たるパスは Archived。</summary>
    [Fact]
    public void Classify_ArchivePath_IsArchived()
    {
        // Arrange
        var classifier = new GlobLifecycleClassifier(StateviaLikeHistory);

        // Act
        var lifecycle = classifier.Classify(".spec/archive/specs/old/requirements.md");

        // Assert
        Assert.Equal(DocumentLifecycle.Archived, lifecycle);
    }

    /// <summary>rejected の glob に当たるパスは Rejected。</summary>
    [Fact]
    public void Classify_RejectPath_IsRejected()
    {
        // Arrange
        var classifier = new GlobLifecycleClassifier(StateviaLikeHistory);

        // Act
        var lifecycle = classifier.Classify(".spec/reject/specs/idea/design.md");

        // Assert
        Assert.Equal(DocumentLifecycle.Rejected, lifecycle);
    }

    /// <summary>どの glob にも当たらないパスは Current。</summary>
    [Fact]
    public void Classify_OtherPath_IsCurrent()
    {
        // Arrange
        var classifier = new GlobLifecycleClassifier(StateviaLikeHistory);

        // Act
        var lifecycle = classifier.Classify(".spec/specs/active/requirements.md");

        // Assert
        Assert.Equal(DocumentLifecycle.Current, lifecycle);
    }

    /// <summary>include と同じく、大文字小文字と \ 区切りの違いを吸収する。</summary>
    [Fact]
    public void Classify_BackslashAndUpperCase_IsArchived()
    {
        // Arrange
        var classifier = new GlobLifecycleClassifier(StateviaLikeHistory);

        // Act
        var lifecycle = classifier.Classify(@".SPEC\Archive\specs\old\tasks.md");

        // Assert
        Assert.Equal(DocumentLifecycle.Archived, lifecycle);
    }

    /// <summary>history が無い設定では、すべて Current。</summary>
    [Fact]
    public void Classify_NoHistory_IsCurrent()
    {
        // Arrange
        var classifier = new GlobLifecycleClassifier(new HistorySettings());

        // Act
        var lifecycle = classifier.Classify(".spec/archive/specs/old/requirements.md");

        // Assert
        Assert.Equal(DocumentLifecycle.Current, lifecycle);
    }

    /// <summary>archived と rejected の両方に当たるパスは、判定順に頼らず例外にする。</summary>
    [Fact]
    public void Classify_BothPatternsMatch_Throws()
    {
        // Arrange
        var classifier = new GlobLifecycleClassifier(new HistorySettings
        {
            Archived = [".spec/**"],
            Rejected = [".spec/reject/**"]
        });

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => classifier.Classify(".spec/reject/specs/x.md"));

        // Assert
        Assert.Contains(".spec/reject/specs/x.md", error.Message, StringComparison.Ordinal);
    }

    /// <summary>通常の相対 glob は検証を通る。</summary>
    [Fact]
    public void ValidateHistory_RelativeGlobs_Passes()
    {
        // Act
        var error = Record.Exception(() => SettingsLoader.ValidateHistory(StateviaLikeHistory));

        // Assert
        Assert.Null(error);
    }

    /// <summary>空・絶対パス・..・長すぎる glob は検証で弾く。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:/outside/**")]
    [InlineData("/etc/**")]
    [InlineData("../other/**")]
    [InlineData(".spec/../../x/**")]
    public void ValidateHistory_InvalidGlob_Throws(string pattern)
    {
        // Arrange
        var history = new HistorySettings { Archived = [pattern] };

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => SettingsLoader.ValidateHistory(history));

        // Assert
        Assert.Contains("history.archived", error.Message, StringComparison.Ordinal);
    }

    /// <summary>上限を超える長さの glob は弾く。</summary>
    [Fact]
    public void ValidateHistory_TooLongGlob_Throws()
    {
        // Arrange
        var history = new HistorySettings { Rejected = [new string('a', SettingsLoader.MaxHistoryPatternLength + 1)] };

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => SettingsLoader.ValidateHistory(history));

        // Assert
        Assert.Contains("history.rejected", error.Message, StringComparison.Ordinal);
    }

    /// <summary>上限を超える件数は弾く。</summary>
    [Fact]
    public void ValidateHistory_TooManyGlobs_Throws()
    {
        // Arrange
        var history = new HistorySettings
        {
            Archived = Enumerable.Range(0, SettingsLoader.MaxHistoryPatterns + 1).Select(i => $"a{i}/**").ToArray()
        };

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => SettingsLoader.ValidateHistory(history));

        // Assert
        Assert.Contains("件まで", error.Message, StringComparison.Ordinal);
    }

    /// <summary>スコープの許可値は大文字小文字を問わず解釈し、未指定は current。</summary>
    [Theory]
    [InlineData(null, SearchScope.Current)]
    [InlineData("", SearchScope.Current)]
    [InlineData("current", SearchScope.Current)]
    [InlineData("History", SearchScope.History)]
    [InlineData(" ALL ", SearchScope.All)]
    public void TryParseScope_AllowedValue_Parses(string? text, SearchScope expected)
    {
        // Act
        var ok = LifecycleText.TryParseScope(text, out var scope);

        // Assert
        Assert.True(ok);
        Assert.Equal(expected, scope);
    }

    /// <summary>許可値以外のスコープは解釈しない。</summary>
    [Theory]
    [InlineData("archive")]
    [InlineData("current;DROP TABLE files")]
    public void TryParseScope_UnknownValue_Fails(string text)
    {
        // Act
        var ok = LifecycleText.TryParseScope(text, out _);

        // Assert
        Assert.False(ok);
    }

    /// <summary>各スコープが、対応する区分の集合になる。</summary>
    [Theory]
    [InlineData(SearchScope.Current, new[] { DocumentLifecycle.Current })]
    [InlineData(SearchScope.History, new[] { DocumentLifecycle.Archived, DocumentLifecycle.Rejected })]
    [InlineData(SearchScope.All, new[] { DocumentLifecycle.Current, DocumentLifecycle.Archived, DocumentLifecycle.Rejected })]
    public void Resolve_Scope_ReturnsLifecycles(SearchScope scope, DocumentLifecycle[] expected)
    {
        // Act
        var lifecycles = SearchScopeResolver.Resolve(scope);

        // Assert
        Assert.Equal(expected.Order(), lifecycles.Order());
    }

    /// <summary>既定のスコープでは、同じ語を含む履歴文書を全文検索の候補に入れない。</summary>
    [Fact]
    public void SearchFullText_CurrentScope_ExcludesArchived()
    {
        // Arrange
        using var store = NewStoreWithCurrentAndArchived();

        // Act
        var hits = store.SearchFullText("IdScheme", 8, SearchScopeResolver.Resolve(SearchScope.Current));

        // Assert
        var hit = Assert.Single(hits);
        Assert.Equal("docs/ids.md", hit.Source.Path);
        Assert.Equal(DocumentLifecycle.Current, hit.Lifecycle);
    }

    /// <summary>history スコープでは、履歴文書だけを返し、区分を付ける。</summary>
    [Fact]
    public void SearchFullText_HistoryScope_ReturnsOnlyArchived()
    {
        // Arrange
        using var store = NewStoreWithCurrentAndArchived();

        // Act
        var hits = store.SearchFullText("IdScheme", 8, SearchScopeResolver.Resolve(SearchScope.History));

        // Assert
        var hit = Assert.Single(hits);
        Assert.Equal(".spec/archive/specs/ids/requirements.md", hit.Source.Path);
        Assert.Equal(DocumentLifecycle.Archived, hit.Lifecycle);
    }

    /// <summary>all スコープでは、両方を返す。</summary>
    [Fact]
    public void SearchFullText_AllScope_ReturnsBoth()
    {
        // Arrange
        using var store = NewStoreWithCurrentAndArchived();

        // Act
        var hits = store.SearchFullText("IdScheme", 8, SearchScopeResolver.Resolve(SearchScope.All));

        // Assert
        Assert.Equal(2, hits.Count);
    }

    /// <summary>ベクトル検索も、埋め込みを読み出す段階で区分を絞る。</summary>
    [Fact]
    public void SearchVectors_CurrentScope_ExcludesArchivedEmbedding()
    {
        // Arrange
        using var store = NewStoreWithCurrentAndArchived();
        foreach (var chunk in store.ListChunksWithoutEmbeddings())
        {
            store.SaveEmbedding(chunk.Id, chunk.Kind, [1f, 0f], "test-model", 2);
        }

        // Act
        var hits = store.SearchVectors([1f, 0f], 8, SearchScopeResolver.Resolve(SearchScope.Current));

        // Assert
        var hit = Assert.Single(hits);
        Assert.Equal(DocumentLifecycle.Current, hit.Lifecycle);
    }

    /// <summary>シンボル検索も区分で絞る。</summary>
    [Fact]
    public void SearchSymbols_HistoryScope_ExcludesCurrentSymbol()
    {
        // Arrange
        using var store = NewStore();
        var file = new RepositoryFile("src/Widget.cs", "C:\\tmp\\Widget.cs", "sha", "csharp");
        store.UpsertSymbols(
            store.EnsureFile(file),
            file,
            new RoslynSyntaxAnalyzer().AnalyzeFile(file.RelativePath, "namespace Demo; public sealed class Widget {}"),
            "rev");

        // Act
        var hits = store.SearchSymbols("Widget", 8, SearchScopeResolver.Resolve(SearchScope.History));

        // Assert
        Assert.Empty(hits);
    }

    /// <summary>区分の付け直しは、値が変わる行だけを数える。</summary>
    [Fact]
    public void UpdateLifecycles_ChangedPath_CountsOnlyChangedRows()
    {
        // Arrange
        using var store = NewStoreWithCurrentAndArchived();
        var map = new Dictionary<string, DocumentLifecycle>
        {
            ["docs/ids.md"] = DocumentLifecycle.Rejected,
            [".spec/archive/specs/ids/requirements.md"] = DocumentLifecycle.Archived,
            ["docs/missing.md"] = DocumentLifecycle.Archived
        };

        // Act
        var changed = store.UpdateLifecycles(map);

        // Assert
        Assert.Equal(1, changed);
        Assert.Empty(store.SearchFullText("IdScheme", 8, SearchScopeResolver.Resolve(SearchScope.Current)));
    }

    /// <summary>スキーマ 2 の DB を開くと lifecycle 列が足され、既存の行は current になる。</summary>
    [Fact]
    public void Initialize_SchemaVersion2_AddsLifecycleColumn()
    {
        // Arrange
        var db = NewDatabasePath();
        using (var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture"))
        {
            store.Initialize();
            store.EnsureFile(new RepositoryFile("docs/a.md", "C:\\tmp\\a.md", "sha", "markdown"));
        }

        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection("Data Source=" + db))
        {
            connection.Open();
            using var downgrade = connection.CreateCommand();
            downgrade.CommandText = """
                DROP INDEX IF EXISTS idx_files_repo_lifecycle;
                ALTER TABLE files DROP COLUMN lifecycle;
                PRAGMA user_version = 2;
                """;
            downgrade.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        // Act
        using var reopened = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        reopened.Initialize();
        var changed = reopened.UpdateLifecycles(new Dictionary<string, DocumentLifecycle>
        {
            ["docs/a.md"] = DocumentLifecycle.Current
        });

        // Assert
        Assert.Equal(0, changed);
        Assert.Equal(3, ReadUserVersion(db));
    }

    /// <summary>新規の DB は、現在のスキーマ版（3）で作られる。版の定数と、版を書く SQL の食い違いを検出する。</summary>
    [Fact]
    public void Initialize_NewDatabase_WritesCurrentSchemaVersion()
    {
        // Arrange
        var db = NewDatabasePath();

        // Act
        using (var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture"))
        {
            store.Initialize();
        }

        // Assert
        Assert.Equal(3, ReadUserVersion(db));
    }

    private static int ReadUserVersion(string db)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection("Data Source=" + db);
        connection.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(read.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>history の glob だけを変えて索引し直すと、再解析・再埋め込みをせずに区分だけが変わる。</summary>
    [Fact]
    public async Task Index_HistoryGlobChanged_RelabelsWithoutReparse()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        var full = Path.Combine(root, "docs", "legacy.md");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, "# 旧設計\n\nIdScheme を時刻順にした理由。\n");
        using var store = new SqliteMeloreStore(Path.Combine(root, "melore.db"), "fixture", root);
        var embeddings = new CountingEmbeddings();
        var git = new StubGit { Files = [new RepositoryFile("docs/legacy.md", full, "blob-1", "markdown")] };
        var indexer = new IndexService(
            git,
            new MarkdownDocumentParser(),
            new RoslynSyntaxAnalyzer(),
            store,
            embeddings,
            NullLogger<IndexService>.Instance);
        var settings = new MeloreSettings { RepositoryName = "fixture", RepositoryPath = root, Include = ["docs/**/*.md"], Exclude = [] };
        await indexer.IndexAsync(settings, CancellationToken.None);
        var callsAfterFirstIndex = embeddings.Calls;
        git.Files = [new RepositoryFile("docs/legacy.md", full, "blob-1", "markdown", DocumentLifecycle.Archived)];

        // Act
        await indexer.IndexAsync(settings, CancellationToken.None);

        // Assert
        Assert.Equal(callsAfterFirstIndex, embeddings.Calls);
        Assert.Empty(store.SearchFullText("IdScheme", 8, SearchScopeResolver.Resolve(SearchScope.Current)));
        var hit = Assert.Single(store.SearchFullText("IdScheme", 8, SearchScopeResolver.Resolve(SearchScope.History)));
        Assert.Equal(DocumentLifecycle.Archived, hit.Lifecycle);
    }

    /// <summary>CLI: history を設定したリポジトリで、--scope history は履歴文書だけを区分付きで出す。</summary>
    [Fact]
    public async Task Cli_SearchHistoryScope_PrintsArchivedWithLifecycle()
    {
        // Arrange
        var config = await CreateRepositoryWithArchiveAsync();
        Assert.Equal(0, (await RunCliAsync(["index", "--config", config])).Exit);

        // Act
        var (exit, output) = await RunCliAsync(["search", "LegacyEngine", "--scope", "history", "--config", config]);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains(".spec/archive/specs/engine-old/requirements.md:", output, StringComparison.Ordinal);
        Assert.Contains("\tarchived", output, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/guide.md", output, StringComparison.Ordinal);
    }

    /// <summary>CLI: 既定のスコープでは履歴文書を出さない。</summary>
    [Fact]
    public async Task Cli_SearchDefaultScope_OmitsArchived()
    {
        // Arrange
        var config = await CreateRepositoryWithArchiveAsync();
        Assert.Equal(0, (await RunCliAsync(["index", "--config", config])).Exit);

        // Act
        var (exit, output) = await RunCliAsync(["search", "LegacyEngine", "--config", config]);

        // Assert
        Assert.Equal(0, exit);
        Assert.DoesNotContain(".spec/archive/", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\tarchived", output, StringComparison.Ordinal);
    }

    /// <summary>CLI: 許可値以外の --scope は実行せずに失敗する。</summary>
    [Fact]
    public async Task Cli_SearchUnknownScope_Fails()
    {
        // Arrange
        var config = await CreateRepositoryWithArchiveAsync();

        // Act
        var (exit, _) = await RunCliAsync(["search", "Engine", "--scope", "archive", "--config", config]);

        // Assert
        Assert.NotEqual(0, exit);
    }

    private static string NewDatabasePath()
        => Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");

    private static SqliteMeloreStore NewStore()
    {
        var store = new SqliteMeloreStore(NewDatabasePath(), "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        return store;
    }

    /// <summary>同じ語「IdScheme」を含む、現行の文書と archive の文書を 1 つずつ入れた store。</summary>
    private static SqliteMeloreStore NewStoreWithCurrentAndArchived()
    {
        var store = NewStore();
        var parser = new MarkdownDocumentParser();
        AddDocument(store, parser, new RepositoryFile("docs/ids.md", "C:\\tmp\\ids.md", "sha1", "markdown"), "# ID\n\nIdScheme は UUID v7。\n");
        AddDocument(
            store,
            parser,
            new RepositoryFile(".spec/archive/specs/ids/requirements.md", "C:\\tmp\\req.md", "sha2", "markdown", DocumentLifecycle.Archived),
            "# 要件\n\nIdScheme を時刻順にする理由。\n");
        return store;
    }

    private static void AddDocument(SqliteMeloreStore store, MarkdownDocumentParser parser, RepositoryFile file, string text)
        => store.UpsertDocument(store.EnsureFile(file), file, parser.Parse(file.RelativePath, text), "rev");

    /// <summary>TestRepository に archive の Spec を足し、history を設定した melore.json を書く。</summary>
    private static async Task<string> CreateRepositoryWithArchiveAsync()
    {
        var repository = await TestRepository.CreateAsync();
        await repository.WriteFileAsync(
            ".spec/archive/specs/engine-old/requirements.md",
            "# LegacyEngine の要件\n\nLegacyEngine は同期実行だった。非同期へ移した理由を残す。\n");
        await repository.CommitAllAsync("archive");
        var baseline = repository.Settings(new Uri("http://127.0.0.1:9"));
        var settings = new MeloreSettings
        {
            RepositoryName = baseline.RepositoryName,
            RepositoryPath = baseline.RepositoryPath,
            Include = [.. baseline.Include, ".spec/**/*.md"],
            Exclude = baseline.Exclude,
            DatabasePath = baseline.DatabasePath,
            Ollama = baseline.Ollama,
            History = StateviaLikeHistory
        };
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath!)!);
        var config = Path.Combine(repository.Root, "melore.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(settings));
        return config;
    }

    private static async Task<(int Exit, string Output)> RunCliAsync(string[] args)
    {
        using var output = new StringWriter();
        var exit = await new MeloreCli(output).BuildRoot().Parse(args).InvokeAsync();
        return (exit, output.ToString());
    }

    private sealed class StubGit : IGitWorkspace
    {
        public List<RepositoryFile> Files { get; set; } = [];

        public Task<string?> GetHeadRevisionAsync(CancellationToken cancellationToken)
            => Task.FromResult<string?>("head");

        public Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RepositoryFile>>(Files);

        public Task<IReadOnlyList<GitRename>> ListRenamesAsync(string? fromRevision, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GitRename>>([]);

        public Task<IReadOnlyList<WorktreeChange>> ListWorktreeChangesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorktreeChange>>([]);

        public Task<RepositoryFile?> TryReadFileAsync(string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(Files.Find(f => f.RelativePath == relativePath));
    }

    private sealed class CountingEmbeddings : IEmbeddingClient
    {
        public int Calls { get; private set; }

        public bool IsAvailable => true;

        public string ModelName => "counting";

        public string AnalyzerVersion => "counting-1";

        public Task<float[]?> EmbedAsync(string text, bool isQuery, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<float[]?>([1f, 0f]);
        }
    }
}
