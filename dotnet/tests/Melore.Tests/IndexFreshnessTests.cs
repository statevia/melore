using Melore.Contracts;
using Melore.Core.Indexing;
using Melore.Core.Models;
using Melore.Core.Ports;
using Melore.Core.Search;
using Melore.Infrastructure.Git;
using Melore.Infrastructure.Sqlite;
using Melore.Parsing.Markdown;
using Melore.Parsing.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;

namespace Melore.Tests;

public sealed class IndexFreshnessTests
{
    /// <summary>analyzer 版が変わると embeddings を消し、ファイルを再解析する。</summary>
    [Fact]
    public async Task Index_AnalyzerBump_ClearsEmbeddingsAndReparses()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        var relative = "docs/a.md";
        var full = Path.Combine(root, "docs", "a.md");
        await File.WriteAllTextAsync(full, "# Title\n\nhello\n");
        var db = Path.Combine(root, "melore.db");
        using var store = new SqliteMeloreStore(db, "fixture", root);
        var embeddings = new CountingEmbeddings();
        var git = new StubGit
        {
            Head = "aaa",
            Files =
            {
                new RepositoryFile(relative, full, "blob-1", "markdown")
            }
        };
        var indexer = new IndexService(
            git,
            new MarkdownDocumentParser(),
            new RoslynSyntaxAnalyzer(),
            store,
            embeddings,
            NullLogger<IndexService>.Instance);

        // Act
        await indexer.IndexAsync(Settings(root), CancellationToken.None);
        var firstCalls = embeddings.Calls;
        Assert.True(firstCalls > 0);
        Assert.Empty(store.ListChunksWithoutEmbeddings());
        store.CompleteIndex("fixture", root, "aaa", "old-analyzer");
        await indexer.IndexAsync(Settings(root), CancellationToken.None);

        // Assert
        Assert.True(embeddings.Calls > firstCalls);
        Assert.Empty(store.ListChunksWithoutEmbeddings());
        Assert.Equal(indexer.AnalyzerVersions, store.GetAnalyzerVersions());
    }

    /// <summary>同一 blob のリネームは再解析せずパスだけ移す。</summary>
    [Fact]
    public async Task Index_Rename_RelocatesWithoutReparse()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        var fromFull = Path.Combine(root, "docs", "old.md");
        var toFull = Path.Combine(root, "docs", "new.md");
        await File.WriteAllTextAsync(fromFull, "# Title\n\nhello\n");
        var db = Path.Combine(root, "melore.db");
        using var store = new SqliteMeloreStore(db, "fixture", root);
        var git = new StubGit
        {
            Head = "aaa",
            Files = { new RepositoryFile("docs/old.md", fromFull, "same-sha", "markdown") }
        };
        var indexer = new IndexService(
            git,
            new MarkdownDocumentParser(),
            new RoslynSyntaxAnalyzer(),
            store,
            new NullEmbeddingClient(),
            NullLogger<IndexService>.Instance);
        await indexer.IndexAsync(Settings(root), CancellationToken.None);

        File.Move(fromFull, toFull);
        git.Head = "bbb";
        git.Files.Clear();
        git.Files.Add(new RepositoryFile("docs/new.md", toFull, "same-sha", "markdown"));
        git.Renames.Add(new GitRename("docs/old.md", "docs/new.md"));

        // Act
        var status = await indexer.IndexAsync(Settings(root), CancellationToken.None);

        // Assert
        var shas = store.GetIndexedBlobShas();
        Assert.False(shas.ContainsKey("docs/old.md"));
        Assert.Equal("same-sha", shas["docs/new.md"]);
        Assert.Equal(1, status.DocumentChunkCount);
    }

    /// <summary>作業ツリーを書き換えると blob SHA が変わる。</summary>
    [Fact]
    public async Task GitWorkspace_DirtyFile_ChangesWorkingTreeHash()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await RunGit(root, ["init"]);
        var file = Path.Combine(root, "docs");
        Directory.CreateDirectory(file);
        var md = Path.Combine(file, "a.md");
        await File.WriteAllTextAsync(md, "# A\n\none\n");
        await RunGit(root, ["add", "docs/a.md"]);
        await RunGit(root, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "init"]);
        var settings = new MeloreSettings
        {
            RepositoryName = "fixture",
            RepositoryPath = root,
            Include = ["docs/**/*.md"],
            Exclude = []
        };
        var git = new GitWorkspace(settings);
        var before = await git.ListFilesAsync(CancellationToken.None);
        var sha1 = Assert.Single(before).BlobSha;

        // Act
        await File.WriteAllTextAsync(md, "# A\n\ntwo\n");
        var after = await git.ListFilesAsync(CancellationToken.None);
        var sha2 = Assert.Single(after).BlobSha;

        // Assert
        Assert.NotEqual(sha1, sha2);
    }

    /// <summary>未追跡の型は、その場の再索引のあと検索 1 位になる。</summary>
    [Fact]
    public async Task EnsureFresh_UntrackedType_RanksFirst()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "dotnet"));
        await RunGit(root, ["init"]);
        var tracked = Path.Combine(root, "dotnet", "Tracked.cs");
        await File.WriteAllTextAsync(tracked, "namespace Demo; public sealed class Tracked {}");
        await RunGit(root, ["add", "dotnet/Tracked.cs"]);
        await RunGit(root, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "init"]);
        var settings = new MeloreSettings
        {
            RepositoryName = "fixture",
            RepositoryPath = root,
            Include = ["dotnet/**/*.cs"],
            Exclude = []
        };
        using var store = new SqliteMeloreStore(Path.Combine(root, "melore.db"), "fixture", root);
        var indexer = new IndexService(
            new GitWorkspace(settings),
            new MarkdownDocumentParser(),
            new RoslynSyntaxAnalyzer(),
            store,
            new NullEmbeddingClient(),
            NullLogger<IndexService>.Instance);
        await indexer.IndexAsync(settings, CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(root, "dotnet", "InProcessBranchScope.cs"),
            "namespace Demo; public sealed class InProcessBranchScope {}");

        // Act
        var freshness = await indexer.EnsureFreshAsync(settings, CancellationToken.None);
        var hits = await new SearchService(store, new NullEmbeddingClient(), new NoOpReranker())
            .SearchAsync("InProcessBranchScope", 8, SearchScope.Current, CancellationToken.None);

        // Assert
        Assert.False(freshness.Dirty);
        Assert.True(freshness.RefreshedFileCount >= 1);
        var first = Assert.Single(hits, h => h.Source.Path.Contains("InProcessBranchScope.cs", StringComparison.Ordinal));
        Assert.Equal(hits[0].Id, first.Id);
    }

    /// <summary>RelocateFile は宛先が既にあると何もしない。</summary>
    [Fact]
    public void RelocateFile_NoOpWhenDestinationExists()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        store.EnsureFile(new RepositoryFile("a.md", "C:\\tmp\\a.md", "1", "markdown"));
        store.EnsureFile(new RepositoryFile("b.md", "C:\\tmp\\b.md", "2", "markdown"));

        // Act
        var moved = store.RelocateFile("a.md", "b.md", "1");

        // Assert
        Assert.False(moved);
        Assert.True(store.GetIndexedBlobShas().ContainsKey("a.md"));
    }

    /// <summary>対象から消えたファイルは索引からも消す。</summary>
    [Fact]
    public async Task Index_RemovedFile_IsDeleted()
    {
        // Arrange
        var context = await NewContextAsync();
        await context.Indexer.IndexAsync(Settings(context.Root), CancellationToken.None);
        context.Git.Files.Clear();

        // Act
        var status = await context.Indexer.IndexAsync(Settings(context.Root), CancellationToken.None);

        // Assert
        Assert.Equal(0, status.DocumentChunkCount);
        Assert.Empty(context.Store.GetIndexedBlobShas());
    }

    /// <summary>解決できた意味関係だけを保存し、相手が無い関係は捨てる。</summary>
    [Fact]
    public async Task Index_SemanticGraph_StoresResolvedEdges()
    {
        // Arrange
        var graph = new StubSemanticGraph(() =>
        [
            new SemanticRelation("Demo.Engine.Run()", "Demo.Engine.Step()", "calls"),
            new SemanticRelation("Demo.Engine.Run()", "Demo.Engine.Step()", "calls"),
            new SemanticRelation("Demo.Engine.Run()", "Demo.Missing()", "calls")
        ]);
        var context = await NewContextAsync(graph);
        var settings = SolutionSettings(context.Root, "demo.sln");
        await File.WriteAllTextAsync(Path.Combine(context.Root, "demo.sln"), "");

        // Act
        await context.Indexer.IndexAsync(settings, CancellationToken.None);

        // Assert
        Assert.Equal(1, context.Store.CountRelationships("calls", null));
    }

    /// <summary>意味解析の失敗では索引全体を止めない。</summary>
    [Fact]
    public async Task Index_SemanticFailure_StillCompletes()
    {
        // Arrange
        var graph = new StubSemanticGraph(() => throw new InvalidOperationException("broken solution"));
        var context = await NewContextAsync(graph);
        var settings = SolutionSettings(context.Root, "demo.sln");
        await File.WriteAllTextAsync(Path.Combine(context.Root, "demo.sln"), "");

        // Act
        var status = await context.Indexer.IndexAsync(settings, CancellationToken.None);

        // Assert
        Assert.Equal(1, graph.Calls);
        Assert.Equal("aaa", status.IndexedRevision);
    }

    /// <summary>ソリューションが無ければ意味解析を呼ばない。</summary>
    [Fact]
    public async Task Index_MissingSolution_SkipsSemanticGraph()
    {
        // Arrange
        var graph = new StubSemanticGraph(() => []);
        var context = await NewContextAsync(graph);

        // Act
        await context.Indexer.IndexAsync(SolutionSettings(context.Root, "missing.sln"), CancellationToken.None);

        // Assert
        Assert.Equal(0, graph.Calls);
    }

    /// <summary>差分が無ければ何もしない。</summary>
    [Fact]
    public async Task EnsureFresh_NoChanges_ReturnsClean()
    {
        // Arrange
        var context = await NewContextAsync();

        // Act
        var freshness = await context.Indexer.EnsureFreshAsync(Settings(context.Root), CancellationToken.None);

        // Assert
        Assert.Equal(new IndexFreshness(false, 0, 0), freshness);
    }

    /// <summary>差分が上限を超えたら索引せず dirty を返す。</summary>
    [Fact]
    public async Task EnsureFresh_TooManyChanges_ReturnsDirty()
    {
        // Arrange
        var context = await NewContextAsync();
        var count = IndexService.MaxAutoRefresh + 1;
        context.Git.Changes.AddRange(
            Enumerable.Range(0, count).Select(i => new WorktreeChange($"docs/{i}.md", Remove: false)));

        // Act
        var freshness = await context.Indexer.EnsureFreshAsync(Settings(context.Root), CancellationToken.None);

        // Assert
        Assert.Equal(new IndexFreshness(true, count, 0), freshness);
    }

    /// <summary>削除の差分と、読めなくなったファイルは索引から消す。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureFresh_RemovedOrUnreadable_DeletesFile(bool remove)
    {
        // Arrange
        var context = await NewContextAsync();
        await context.Indexer.IndexAsync(Settings(context.Root), CancellationToken.None);
        context.Git.Files.Clear();
        context.Git.Changes.Add(new WorktreeChange("docs/a.md", remove));

        // Act
        var freshness = await context.Indexer.EnsureFreshAsync(Settings(context.Root), CancellationToken.None);

        // Assert
        Assert.Equal(1, freshness.RefreshedFileCount);
        Assert.DoesNotContain("docs/a.md", context.Store.GetIndexedBlobShas().Keys);
    }

    /// <summary>blob が同じファイルは差分に出ても索引し直さない。</summary>
    [Fact]
    public async Task EnsureFresh_SameBlob_DoesNotRefresh()
    {
        // Arrange
        var context = await NewContextAsync();
        await context.Indexer.IndexAsync(Settings(context.Root), CancellationToken.None);
        context.Git.Changes.Add(new WorktreeChange("docs/a.md", Remove: false));

        // Act
        var freshness = await context.Indexer.EnsureFreshAsync(Settings(context.Root), CancellationToken.None);

        // Assert
        Assert.Equal(new IndexFreshness(false, 1, 0), freshness);
    }

    private static async Task<IndexContext> NewContextAsync(ISemanticGraphExtractor? semantic = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        var markdown = Path.Combine(root, "docs", "a.md");
        await File.WriteAllTextAsync(markdown, "# Title\n\nhello\n");
        var code = Path.Combine(root, "Engine.cs");
        await File.WriteAllTextAsync(
            code,
            "namespace Demo; public sealed class Engine { public void Run() {} public void Step() {} }");
        var git = new StubGit
        {
            Head = "aaa",
            Files =
            {
                new RepositoryFile("docs/a.md", markdown, "blob-1", "markdown"),
                new RepositoryFile("Engine.cs", code, "blob-2", "csharp")
            }
        };
        var store = new SqliteMeloreStore(Path.Combine(root, "melore.db"), "fixture", root);
        var indexer = new IndexService(
            git,
            new MarkdownDocumentParser(),
            new RoslynSyntaxAnalyzer(),
            store,
            new NullEmbeddingClient(),
            NullLogger<IndexService>.Instance,
            semantic);
        return new IndexContext(root, git, store, indexer);
    }

    private static MeloreSettings SolutionSettings(string root, string solutionPath) => new()
    {
        RepositoryName = "fixture",
        RepositoryPath = root,
        SolutionPath = solutionPath
    };

    private sealed record IndexContext(string Root, StubGit Git, SqliteMeloreStore Store, IndexService Indexer);

    private static MeloreSettings Settings(string root) => new()
    {
        RepositoryName = "fixture",
        RepositoryPath = root,
        Include = ["docs/**/*.md"],
        Exclude = []
    };

    private static async Task RunGit(string root, string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(start)
                            ?? throw new InvalidOperationException("git");
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class StubGit : IGitWorkspace
    {
        public string? Head { get; set; }

        public List<RepositoryFile> Files { get; } = [];

        public List<GitRename> Renames { get; } = [];

        public List<WorktreeChange> Changes { get; } = [];

        public Task<string?> GetHeadRevisionAsync(CancellationToken cancellationToken)
            => Task.FromResult(Head);

        public Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RepositoryFile>>(Files);

        public Task<IReadOnlyList<GitRename>> ListRenamesAsync(string? fromRevision, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GitRename>>(fromRevision is null ? [] : Renames);

        public Task<IReadOnlyList<WorktreeChange>> ListWorktreeChangesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorktreeChange>>(Changes);

        public Task<RepositoryFile?> TryReadFileAsync(string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(Files.Find(f => f.RelativePath == relativePath));
    }

    private sealed class StubSemanticGraph(Func<IReadOnlyList<SemanticRelation>> extract) : ISemanticGraphExtractor
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SemanticRelation>> ExtractAsync(string solutionPath, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(extract());
        }
    }

    private sealed class CountingEmbeddings : IEmbeddingClient
    {
        public int Calls { get; private set; }

        public bool IsAvailable => true;

        public string ModelName => "test-embed";

        public string AnalyzerVersion => "embed:test-embed";

        public Task<float[]?> EmbedAsync(string text, bool isQuery, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<float[]?>([1f, 0f, 0f]);
        }
    }
}
