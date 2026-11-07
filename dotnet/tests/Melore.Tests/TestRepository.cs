using System.Diagnostics;
using System.Text.Json;
using Melore.Contracts;

namespace Melore.Tests;

/// <summary>
/// 文書・実装・テストを 1 つずつ持つ、一時的な Git リポジトリ。
/// </summary>
internal sealed class TestRepository
{
    private TestRepository(string root) => Root = root;

    /// <summary>リポジトリのルート。</summary>
    public string Root { get; }

    /// <summary>
    /// リポジトリを作り、初期コミットまで済ませる。
    /// </summary>
    public static async Task<TestRepository> CreateAsync()
    {
        var repository = new TestRepository(
            Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N")));
        await repository.WriteFileAsync(
            "docs/guide.md",
            """
            # Engine

            Engine は実行の中心です。`Engine` と `MissingMember` に触れます。

            ## 手順

            [実装](../src/Engine.cs) を見る。
            """);
        await repository.WriteFileAsync(
            "src/Engine.cs",
            """
            namespace Demo;

            public interface IEngine
            {
                void Run();
            }

            /// <summary>実行の中心。</summary>
            public sealed class Engine : IEngine
            {
                public void Run() => Step();

                private void Step() {}
            }
            """);
        await repository.WriteFileAsync(
            "tests/EngineTests.cs",
            """
            namespace Demo.Tests;

            public sealed class EngineTests
            {
                [Fact]
                public void Run_Works() {}
            }
            """);
        await repository.GitAsync("init");
        await repository.CommitAllAsync("init");
        return repository;
    }

    /// <summary>リポジトリ相対パスへ書く。親フォルダが無ければ作る。</summary>
    public async Task WriteFileAsync(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
    }

    /// <summary>すべての変更をコミットする。</summary>
    public async Task CommitAllAsync(string message)
    {
        await GitAsync("add", "-A");
        await GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", message);
    }

    /// <summary>git を実行する。失敗したらテストを落とす。</summary>
    public async Task GitAsync(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    /// <summary>このリポジトリを指す設定。</summary>
    public MeloreSettings Settings(Uri ollamaEndpoint, string? solutionPath = null)
        => new()
        {
            RepositoryName = "fixture",
            RepositoryPath = Root,
            Include = ["docs/**/*.md", "src/**/*.cs", "tests/**/*.cs"],
            Exclude = [],
            SolutionPath = solutionPath,
            DatabasePath = Path.Combine(Root, ".melore", "melore.db"),
            Ollama = new OllamaSettings
            {
                Endpoint = ollamaEndpoint.ToString(),
                EmbeddingModel = "embed-model",
                ChatModel = "chat-model"
            }
        };

    /// <summary>設定を melore.json に書き、そのパスを返す。</summary>
    public async Task<string> WriteConfigAsync(Uri ollamaEndpoint)
    {
        var path = Path.Combine(Root, "melore.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Settings(ollamaEndpoint)));
        Directory.CreateDirectory(Path.Combine(Root, ".melore"));
        return path;
    }
}
