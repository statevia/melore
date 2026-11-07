using Melore.Contracts;
using Melore.Core.Models;
using Melore.Infrastructure.Git;

namespace Melore.Tests;

/// <summary>
/// 実際の git を呼ぶ処理と、git の出力の解釈。
/// </summary>
public sealed class GitWorkspaceTests
{
    /// <summary>絶対パスの PATH エントリを前から順に探す。</summary>
    [Fact]
    public void Locator_FindsFirstAbsoluteMatch()
    {
        // Arrange
        var empty = NewDirectory();
        var first = NewDirectory();
        var second = NewDirectory();
        File.WriteAllText(Path.Combine(first, "tool"), "");
        File.WriteAllText(Path.Combine(second, "tool"), "");
        var path = string.Join(Path.PathSeparator, empty, first, second);

        // Act
        var found = GitExecutableLocator.Find(path, "tool");

        // Assert
        Assert.Equal(Path.Combine(first, "tool"), found);
    }

    /// <summary>カレントディレクトリに当たる相対エントリは探さない。</summary>
    [Fact]
    public void Locator_IgnoresRelativeEntries()
    {
        // Arrange
        var fileName = "melore-" + Guid.NewGuid().ToString("N");
        var planted = Path.Combine(Environment.CurrentDirectory, fileName);
        File.WriteAllText(planted, "");
        var path = "." + Path.PathSeparator + "relative";

        // Act
        var found = GitExecutableLocator.Find(path, fileName);
        File.Delete(planted);

        // Assert
        Assert.Null(found);
    }

    /// <summary>引用符付きの PATH エントリも読む。</summary>
    [Fact]
    public void Locator_AcceptsQuotedEntries()
    {
        // Arrange
        var directory = NewDirectory();
        File.WriteAllText(Path.Combine(directory, "tool"), "");

        // Act
        var found = GitExecutableLocator.Find("\"" + directory + "\"", "tool");

        // Assert
        Assert.Equal(Path.Combine(directory, "tool"), found);
    }

    /// <summary>PATH が無ければ null を返す。</summary>
    [Fact]
    public void Locator_NoPath_ReturnsNull()
    {
        // Arrange
        string? path = null;

        // Act
        var found = GitExecutableLocator.Find(path, "tool");

        // Assert
        Assert.Null(found);
    }

    /// <summary>実行環境の git を絶対パスで見つける。</summary>
    [Fact]
    public void Locator_FindsInstalledGit()
    {
        // Arrange と Act
        var found = GitExecutableLocator.Find();

        // Assert
        Assert.NotNull(found);
        Assert.True(Path.IsPathFullyQualified(found));
    }

    /// <summary>コミット間のリネームを返す。</summary>
    [Fact]
    public async Task ListRenames_ReturnsRenamedFile()
    {
        // Arrange
        var repository = await TestRepository.CreateAsync();
        var workspace = Workspace(repository);
        var revision = await workspace.GetHeadRevisionAsync(CancellationToken.None);
        await repository.GitAsync("mv", "docs/guide.md", "docs/moved.md");
        await repository.CommitAllAsync("move");

        // Act
        var renames = await workspace.ListRenamesAsync(revision, CancellationToken.None);

        // Assert
        Assert.Equal(new GitRename("docs/guide.md", "docs/moved.md"), Assert.Single(renames));
    }

    /// <summary>比較元のリビジョンが無ければ git を呼ばず空を返す。</summary>
    [Fact]
    public async Task ListRenames_NoRevision_ReturnsEmpty()
    {
        // Arrange
        var repository = await TestRepository.CreateAsync();
        var workspace = Workspace(repository);

        // Act
        var renames = await workspace.ListRenamesAsync(null, CancellationToken.None);

        // Assert
        Assert.Empty(renames);
    }

    /// <summary>不正なリビジョンでは例外にせず空を返す。</summary>
    [Fact]
    public async Task ListRenames_UnknownRevision_ReturnsEmpty()
    {
        // Arrange
        var repository = await TestRepository.CreateAsync();
        var workspace = Workspace(repository);

        // Act
        var renames = await workspace.ListRenamesAsync("0000000", CancellationToken.None);

        // Assert
        Assert.Empty(renames);
    }

    /// <summary>include に合わない変更は差分に含めない。</summary>
    [Fact]
    public async Task ListWorktreeChanges_FiltersByInclude()
    {
        // Arrange
        var repository = await TestRepository.CreateAsync();
        await repository.WriteFileAsync("docs/new.md", "# New");
        await repository.WriteFileAsync("notes.txt", "out of scope");
        var workspace = Workspace(repository);

        // Act
        var changes = await workspace.ListWorktreeChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new WorktreeChange("docs/new.md", Remove: false), Assert.Single(changes));
    }

    /// <summary>差分が無ければ空を返す。</summary>
    [Fact]
    public async Task ListWorktreeChanges_CleanTree_ReturnsEmpty()
    {
        // Arrange
        var repository = await TestRepository.CreateAsync();
        var workspace = Workspace(repository);

        // Act
        var changes = await workspace.ListWorktreeChangesAsync(CancellationToken.None);

        // Assert
        Assert.Empty(changes);
    }

    /// <summary>対象のファイルは言語と blob のハッシュ付きで読む。</summary>
    [Fact]
    public async Task TryReadFile_IncludedFile_ReturnsMetadata()
    {
        // Arrange
        var repository = await TestRepository.CreateAsync();
        var workspace = Workspace(repository);

        // Act
        var file = await workspace.TryReadFileAsync("src\\Engine.cs", CancellationToken.None);

        // Assert
        Assert.Equal("src/Engine.cs", file?.RelativePath);
        Assert.Equal("csharp", file?.Language);
        Assert.False(string.IsNullOrEmpty(file?.BlobSha));
    }

    /// <summary>対象外と存在しないファイルは null を返す。</summary>
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("docs/missing.md")]
    public async Task TryReadFile_ExcludedOrMissing_ReturnsNull(string relativePath)
    {
        // Arrange
        var repository = await TestRepository.CreateAsync();
        await repository.WriteFileAsync("notes.txt", "out of scope");
        var workspace = Workspace(repository);

        // Act
        var file = await workspace.TryReadFileAsync(relativePath, CancellationToken.None);

        // Assert
        Assert.Null(file);
    }

    /// <summary>Git 管理外のフォルダでも例外にせず、ファイルを列挙する。</summary>
    [Fact]
    public async Task NonRepository_FallsBackWithoutThrowing()
    {
        // Arrange
        var root = NewDirectory();
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        await File.WriteAllTextAsync(Path.Combine(root, "docs", "a.md"), "# A");
        var workspace = new GitWorkspace(new MeloreSettings
        {
            RepositoryPath = root,
            Include = ["docs/**/*.md"],
            Exclude = []
        });

        // Act
        var head = await workspace.GetHeadRevisionAsync(CancellationToken.None);
        var changes = await workspace.ListWorktreeChangesAsync(CancellationToken.None);
        var files = await workspace.ListFilesAsync(CancellationToken.None);

        // Assert
        Assert.Null(head);
        Assert.Empty(changes);
        Assert.False(string.IsNullOrEmpty(Assert.Single(files).BlobSha));
    }

    /// <summary>リネームは旧パスの削除と新パスの追加に分ける。</summary>
    [Fact]
    public void StatusParser_Rename_SplitsIntoRemoveAndAdd()
    {
        // Arrange
        const string stdout = "R  old.md\tnew.md\n";

        // Act
        var changes = GitStatusParser.Parse(stdout);

        // Assert
        Assert.Equal(
            [new WorktreeChange("old.md", Remove: true), new WorktreeChange("new.md", Remove: false)],
            changes);
    }

    /// <summary>削除の印と、引用符付きのパスを読む。短すぎる行は捨てる。</summary>
    [Fact]
    public void StatusParser_ReadsDeletedAndQuotedPaths()
    {
        // Arrange
        const string stdout = " D gone.md\n?? \"with space.md\"\nxx\n";

        // Act
        var changes = GitStatusParser.Parse(stdout);

        // Assert
        Assert.Equal(
            [new WorktreeChange("gone.md", Remove: true), new WorktreeChange("with space.md", Remove: false)],
            changes);
    }

    private static GitWorkspace Workspace(TestRepository repository)
        => new(new MeloreSettings
        {
            RepositoryPath = repository.Root,
            Include = ["docs/**/*.md", "src/**/*.cs"],
            Exclude = []
        });

    private static string NewDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
