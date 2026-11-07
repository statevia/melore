using System.CommandLine;
using Melore.Cli;

namespace Melore.Tests;

/// <summary>
/// CLI の全サブコマンドを、一時リポジトリとスタブの Ollama で実行する。
/// </summary>
public sealed class MeloreCliTests(MeloreCliTests.Fixture fixture) : IClassFixture<MeloreCliTests.Fixture>
{
    /// <summary>index は件数とリビジョンを表示する。</summary>
    [Fact]
    public void Index_PrintsStatus()
    {
        // Arrange
        var result = fixture.IndexResult;

        // Act
        var output = result.Output;

        // Assert
        Assert.Equal(0, result.Exit);
        Assert.Contains("repository\tfixture", output, StringComparison.Ordinal);
        Assert.Contains("document_chunks\t2", output, StringComparison.Ordinal);
    }

    /// <summary>status は索引の状態を返す。</summary>
    [Fact]
    public async Task Status_PrintsCounts()
    {
        // Arrange
        string[] args = ["status", "--config", fixture.Config];

        // Act
        var (exit, output) = await RunAsync(args);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("tests\t1", output, StringComparison.Ordinal);
    }

    /// <summary>search はパスと行範囲付きのヒットを返す。</summary>
    [Fact]
    public async Task Search_PrintsHitWithLineRange()
    {
        // Arrange
        string[] args = ["search", "Engine", "--limit", "4", "--config", fixture.Config];

        // Act
        var (exit, output) = await RunAsync(args);

        // Assert
        Assert.Equal(0, exit);
        Assert.StartsWith("dirty\tFalse", output, StringComparison.Ordinal);
        Assert.Contains("src/Engine.cs:", output, StringComparison.Ordinal);
    }

    /// <summary>references は型のメンバーを列挙する。</summary>
    [Fact]
    public async Task References_PrintsDeclaredMembers()
    {
        // Arrange
        string[] args = ["references", "Demo.Engine", "--config", fixture.Config];

        // Act
        var (exit, output) = await RunAsync(args);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("declares", output, StringComparison.Ordinal);
    }

    /// <summary>trace は --rel で関係の種類を絞る。</summary>
    [Fact]
    public async Task Trace_FiltersByRelationshipType()
    {
        // Arrange
        string[] args = ["trace", "Demo.Engine", "--rel", "declares", "--depth", "1", "--config", fixture.Config];

        // Act
        var (exit, output) = await RunAsync(args);

        // Assert
        Assert.Equal(0, exit);
        Assert.StartsWith("root\tDemo.Engine", output, StringComparison.Ordinal);
        Assert.Contains("declares\tDemo.Engine -> ", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\tinherits\t", output, StringComparison.Ordinal);
    }

    /// <summary>trace は見つからないシンボルで 1 を返す。</summary>
    [Fact]
    public async Task Trace_UnknownSymbol_ReturnsOne()
    {
        // Arrange
        string[] args = ["trace", "Demo.Nothing", "--config", fixture.Config];

        // Act
        var (exit, output) = await RunAsync(args);

        // Assert
        Assert.Equal(1, exit);
        Assert.Contains("見つかりません", output, StringComparison.Ordinal);
    }

    /// <summary>explain はチャットの利用と根拠の ID を表示する。</summary>
    [Fact]
    public async Task Explain_PrintsEvidence()
    {
        // Arrange
        string[] args = ["explain", "Engine", "--limit", "2", "--config", fixture.Config];

        // Act
        var (exit, output) = await RunAsync(args);

        // Assert
        Assert.Equal(0, exit);
        Assert.Contains("evidence_ids\t", output, StringComparison.Ordinal);
        Assert.Contains("chat\t", output, StringComparison.Ordinal);
    }

    /// <summary>eval は Statevia 向けの期待パスが無いと MISS を出し、1 を返す。</summary>
    [Fact]
    public async Task Eval_ReportsMissesAndFails()
    {
        // Arrange
        string[] args = ["eval", "--config", fixture.Config];

        // Act
        var (exit, output) = await RunAsync(args);

        // Assert
        Assert.Equal(1, exit);
        Assert.Contains("hit_at_8", output, StringComparison.Ordinal);
        Assert.Contains("MISS", output, StringComparison.Ordinal);
    }

    /// <summary>--config が無いと実行せずに失敗する。</summary>
    [Fact]
    public async Task Status_WithoutConfig_Fails()
    {
        // Arrange
        string[] args = ["status"];

        // Act
        var (exit, _) = await RunAsync(args);

        // Assert
        Assert.NotEqual(0, exit);
    }

    private static async Task<(int Exit, string Output)> RunAsync(string[] args)
    {
        using var output = new StringWriter();
        var exit = await new MeloreCli(output).BuildRoot().Parse(args).InvokeAsync();
        return (exit, output.ToString());
    }

    /// <summary>
    /// 索引済みのリポジトリを共有する。
    /// </summary>
    public sealed class Fixture : IAsyncLifetime
    {
        private readonly OllamaStub _ollama = new();

        /// <summary>melore.json のパス。</summary>
        public string Config { get; private set; } = "";

        /// <summary>最初の index の結果。</summary>
        public (int Exit, string Output) IndexResult { get; private set; }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            var repository = await TestRepository.CreateAsync();
            Config = await repository.WriteConfigAsync(_ollama.Endpoint);
            IndexResult = await RunAsync(["index", "--config", Config]);
        }

        /// <inheritdoc />
        public Task DisposeAsync()
        {
            _ollama.Dispose();
            return Task.CompletedTask;
        }
    }
}
