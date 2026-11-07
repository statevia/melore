using Melore.Contracts;
using Melore.Host;
using Melore.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Melore.Tests;

/// <summary>
/// MCP ツールの応答と利用ログを、索引済みのリポジトリで確かめる。
/// </summary>
public sealed class MeloreToolsTests(MeloreToolsTests.Fixture fixture) : IClassFixture<MeloreToolsTests.Fixture>
{
    /// <summary>search は既定で本文を返さない。</summary>
    [Fact]
    public async Task Search_OmitsBodyByDefault()
    {
        // Arrange
        var tools = fixture.Tools;

        // Act
        var result = await tools.Search("Engine", limit: 100);

        // Assert
        Assert.NotEmpty(result.Hits);
        Assert.All(result.Hits, hit => Assert.Null(hit.Body));
        Assert.False(result.Dirty);
    }

    /// <summary>includeBody を指定すると本文を返す。</summary>
    [Fact]
    public async Task Search_IncludeBody_ReturnsBody()
    {
        // Arrange
        var tools = fixture.Tools;

        // Act
        var result = await tools.Search("Engine", limit: 4, includeBody: true);

        // Assert
        Assert.Contains(result.Hits, hit => !string.IsNullOrEmpty(hit.Body));
    }

    /// <summary>references は関係の種類を付けて返す。</summary>
    [Fact]
    public async Task References_ReturnsRelationshipType()
    {
        // Arrange
        var tools = fixture.Tools;

        // Act
        var result = await tools.References("Demo.Engine");

        // Assert
        Assert.Contains(result.Hits, hit => hit.RelType == "declares");
    }

    /// <summary>trace は起点と辺を返す。</summary>
    [Fact]
    public async Task Trace_ReturnsRootAndEdges()
    {
        // Arrange
        var tools = fixture.Tools;

        // Act
        var result = await tools.Trace("Demo.Engine", depth: 1, relTypes: ["declares"]);

        // Assert
        Assert.Equal("Demo.Engine", result.Root?.Name);
        Assert.All(result.Edges, edge => Assert.Equal("declares", edge.RelType));
    }

    /// <summary>explain は索引の鮮度を結果へ載せる。</summary>
    [Fact]
    public async Task Explain_CarriesFreshness()
    {
        // Arrange
        var tools = fixture.Tools;

        // Act
        var result = await tools.Explain("Engine", limit: 2);

        // Assert
        Assert.False(result.Dirty);
        Assert.NotEmpty(result.Evidence);
    }

    /// <summary>失敗したツールも利用ログへ残し、例外はそのまま返す。</summary>
    [Fact]
    public async Task Trace_Failure_IsRecordedAndRethrown()
    {
        // Arrange
        var tools = fixture.Tools;
        var before = fixture.Runtime.Search.Status("fixture").McpInvocationCount;

        // Act
        var exception = await Record.ExceptionAsync(() => tools.Trace(null!));

        // Assert
        Assert.NotNull(exception);
        Assert.True(fixture.Runtime.Search.Status("fixture").McpInvocationCount > before);
    }

    /// <summary>
    /// 索引済みのランタイムを共有する。
    /// </summary>
    public sealed class Fixture : IAsyncLifetime
    {
        private readonly OllamaStub _ollama = new();

        /// <summary>索引済みのランタイム。</summary>
        public MeloreRuntime Runtime { get; private set; } = null!;

        /// <summary>テスト対象のツール。</summary>
        public MeloreTools Tools { get; private set; } = null!;

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            var repository = await TestRepository.CreateAsync();
            Directory.CreateDirectory(Path.Combine(repository.Root, ".melore"));
            var settings = repository.Settings(_ollama.Endpoint);
            Runtime = MeloreRuntime.Create(settings, NullLoggerFactory.Instance);
            await Runtime.Indexer.IndexAsync(settings, CancellationToken.None);
            Tools = new MeloreTools(
                Runtime.Search,
                Runtime.References,
                Runtime.Trace,
                Runtime.Explain,
                Runtime.Store,
                Runtime.Indexer,
                settings);
        }

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            await Runtime.DisposeAsync();
            _ollama.Dispose();
        }
    }
}
