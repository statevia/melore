using Melore.Contracts;
using Melore.Core.Ports;
using Melore.Host;
using Melore.Providers.Ollama;
using Microsoft.Extensions.Logging.Abstractions;

namespace Melore.Tests;

/// <summary>
/// 設定の読み込みと、Ollama の有無によるランタイムの組み立て。
/// </summary>
public sealed class HostTests
{
    /// <summary>コメントと末尾カンマを許し、キーの大文字小文字を区別しない。</summary>
    [Fact]
    public async Task Load_AcceptsCommentsAndTrailingCommas()
    {
        // Arrange
        var root = NewDirectory();
        var path = Path.Combine(root, "melore.json");
        var escaped = root.Replace("\\", "\\\\", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, $$"""
            {
                // 対象
                "REPOSITORYNAME": "demo",
                "repositoryPath": "{{escaped}}",
            }
            """);

        // Act
        var settings = SettingsLoader.Load(new FileInfo(path));

        // Assert
        Assert.Equal("demo", settings.RepositoryName);
        Assert.Equal(root, settings.RepositoryPath);
    }

    /// <summary>存在しない repositoryPath は読み込み時に拒否する。</summary>
    [Fact]
    public async Task Load_MissingRepository_Throws()
    {
        // Arrange
        var path = Path.Combine(NewDirectory(), "melore.json");
        await File.WriteAllTextAsync(path, """{ "repositoryPath": "Z:\\melore-not-found" }""");

        // Act
        var exception = Record.Exception(() => SettingsLoader.Load(new FileInfo(path)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>JSON の null は読み込み時に拒否する。</summary>
    [Fact]
    public async Task Load_NullDocument_Throws()
    {
        // Arrange
        var path = Path.Combine(NewDirectory(), "melore.json");
        await File.WriteAllTextAsync(path, "null");

        // Act
        var exception = Record.Exception(() => SettingsLoader.Load(new FileInfo(path)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>--config のパスから読む。</summary>
    [Fact]
    public async Task LoadFromArgs_UsesConfigOption()
    {
        // Arrange
        var root = NewDirectory();
        var path = Path.Combine(root, "melore.json");
        var escaped = root.Replace("\\", "\\\\", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, $$"""{ "repositoryPath": "{{escaped}}" }""");

        // Act
        var settings = SettingsLoader.LoadFromArgs(["--other", "x", "--CONFIG", path]);

        // Assert
        Assert.Equal(root, settings.RepositoryPath);
    }

    /// <summary>databasePath を指定したら絶対パスにして使う。</summary>
    [Fact]
    public void ResolveDatabasePath_UsesConfiguredPath()
    {
        // Arrange
        var settings = new MeloreSettings { DatabasePath = Path.Combine("data", "melore.db") };

        // Act
        var path = SettingsLoader.ResolveDatabasePath(settings);

        // Assert
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.EndsWith(Path.Combine("data", "melore.db"), path, StringComparison.Ordinal);
    }

    /// <summary>Ollama が応答するときは埋め込みとチャットのクライアントを使う。</summary>
    [Fact]
    public async Task Create_OllamaAvailable_UsesOllamaClients()
    {
        // Arrange
        using var ollama = new OllamaStub();
        var repository = await NewRepositoryAsync();

        // Act
        await using var runtime = MeloreRuntime.Create(repository.Settings(ollama.Endpoint), NullLoggerFactory.Instance);

        // Assert
        Assert.IsType<OllamaEmbeddingClient>(runtime.Embeddings);
        Assert.IsType<OllamaChatClient>(runtime.Chat);
    }

    /// <summary>Ollama が止まっていても Null クライアントで動く。</summary>
    [Fact]
    public async Task Create_OllamaUnavailable_FallsBackToNullClients()
    {
        // Arrange
        using var ollama = new OllamaStub(available: false);
        var repository = await NewRepositoryAsync();

        // Act
        using var runtime = MeloreRuntime.Create(repository.Settings(ollama.Endpoint), NullLoggerFactory.Instance);

        // Assert
        Assert.IsType<NullEmbeddingClient>(runtime.Embeddings);
        Assert.IsType<NullChatClient>(runtime.Chat);
    }

    private static async Task<TestRepository> NewRepositoryAsync()
    {
        var repository = await TestRepository.CreateAsync();
        Directory.CreateDirectory(Path.Combine(repository.Root, ".melore"));
        return repository;
    }

    private static string NewDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
