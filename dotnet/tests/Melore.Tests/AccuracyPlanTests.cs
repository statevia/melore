using System.Text.Json;
using Melore.Contracts;
using Melore.Core.Models;
using Melore.Core.Ports;
using Melore.Core.Search;
using Melore.Infrastructure.Git;
using Melore.Infrastructure.Sqlite;
using Melore.Mcp;
using Melore.Parsing.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ModelContextProtocol;

namespace Melore.Tests;

public sealed class AccuracyPlanTests
{
    /// <summary>porcelain の未追跡・削除・リネームをパスにする。</summary>
    [Fact]
    public void GitStatusParser_ReadsUntrackedDeleteAndRename()
    {
        // Arrange
        const string stdout = """
            ?? dotnet/New.cs
            D  dotnet/Gone.cs
            R  dotnet/Old.cs	dotnet/Next.cs

            """;

        // Act
        var changes = GitStatusParser.Parse(stdout);

        // Assert
        Assert.Contains(changes, c => c.RelativePath == "dotnet/New.cs" && !c.Remove);
        Assert.Contains(changes, c => c.RelativePath == "dotnet/Gone.cs" && c.Remove);
        Assert.Contains(changes, c => c.RelativePath == "dotnet/Old.cs" && c.Remove);
        Assert.Contains(changes, c => c.RelativePath == "dotnet/Next.cs" && !c.Remove);
    }

    /// <summary>テスト語が無いとき、テストファイルは実装より後ろ。</summary>
    [Fact]
    public async Task Search_WithoutTestWords_DemotesTestPaths()
    {
        // Arrange
        using var store = NewStore();
        var analyzer = new RoslynSyntaxAnalyzer();
        Index(store, analyzer, "dotnet/App/Widget.cs", "namespace Demo; public sealed class Widget {}");
        Index(store, analyzer, "dotnet/App.Tests/WidgetTests.cs", """
            namespace Demo;
            public sealed class WidgetTests
            {
                [Fact]
                public void FindsWidget() {}
            }
            """);
        var search = new SearchService(store, new NullEmbeddingClient(), new NoOpReranker());

        // Act
        var hits = await search.SearchAsync("Widget", 8, SearchScope.Current, CancellationToken.None);

        // Assert
        Assert.Contains("Widget.cs", hits[0].Source.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("Tests", hits[0].Source.Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>MCP の入出力は status の件数に出る。</summary>
    [Fact]
    public void RecordMcpInvocation_ShowsOnStatus()
    {
        // Arrange
        using var store = NewStore();

        // Act
        store.RecordMcpInvocation("search", "{\"query\":\"日本語\"}", "{\"hits\":[]}", 4, true, null);
        var status = store.GetStatus("fixture");

        // Assert
        Assert.Equal(1, status.McpInvocationCount);
        Assert.NotNull(status.LastMcpInvocationUtc);
    }

    /// <summary>解決できた implements と calls を FQN で返す。</summary>
    [Fact]
    public void SemanticGraph_ReadsImplementsAndCalls()
    {
        // Arrange
        const string source = """
            namespace Demo;
            public interface IEngine { void Run(); }
            public sealed class Engine : IEngine
            {
                public void Run() { Helper(); }
                private void Helper() {}
            }
            """;
        var compilation = CSharpCompilation.Create(
            "demo",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Act
        var edges = RoslynSemanticGraph.FromCompilation(compilation);

        // Assert
        Assert.Contains(edges, e => e is { RelType: "implements", FromFqn: "Demo.Engine", ToFqn: "Demo.IEngine" });
        Assert.Contains(edges, e => e.RelType == "calls" && e.FromFqn == "Demo.Engine.Run()" && e.ToFqn == "Demo.Engine.Helper()");
    }

    /// <summary>自己呼び出しと、ソースに宣言が無い相手への呼び出しは辺にしない。</summary>
    [Fact]
    public void SemanticGraph_DropsSelfAndExternalCalls()
    {
        // Arrange
        const string source = """
            namespace Demo;
            public sealed class Engine
            {
                public void Run()
                {
                    Run();
                    System.GC.Collect();
                    Unknown();
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "demo",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Act
        var edges = RoslynSemanticGraph.FromCompilation(compilation);

        // Assert
        Assert.Empty(edges);
    }

    /// <summary>calls があれば未抽出とは言わず、ノード数は応答上限で止まる。</summary>
    [Fact]
    public void Trace_StopsAtBudget_AndReportsCalls()
    {
        // Arrange
        using var store = NewStore();
        var analyzer = new RoslynSyntaxAnalyzer();
        var methods = string.Join('\n', Enumerable.Range(0, 80).Select(i => $"public void M{i}() {{}}"));
        Index(store, analyzer, "dotnet/Engine.cs", "namespace Demo; public sealed class Engine { " + methods + " }");
        var engine = store.FindSymbols("Demo.Engine", 1).Single(s => s.Title == "Demo.Engine");
        var member = store.FindSymbols("Demo.Engine.M0()", 1)[0];
        store.AddRelationship(new RelationshipEntry("symbol", engine.Id, "symbol", member.Id, "calls", RelationshipBasis.Explicit, 1, "M0"));

        // Act
        var wide = new TraceService(store).Trace("Demo.Engine", 1, limit: 256);
        var calls = new TraceService(store).Trace("Demo.Engine", 1, limit: 8, relTypes: ["calls"]);

        // Assert
        Assert.Contains("打ち切り", wide.Limitation, StringComparison.Ordinal);
        Assert.True(wide.Nodes.Count < 81);
        Assert.Contains(calls.Edges, e => e.RelType == "calls");
        Assert.DoesNotContain("未抽出", calls.Limitation ?? "", StringComparison.Ordinal);
    }

    /// <summary>MCP ツール結果の JSON は日本語をエスケープしない。</summary>
    [Fact]
    public void McpJson_WritesJapaneseLiterally()
    {
        // Arrange
        var value = new { text = "日本語" };

        // Act
        var json = JsonSerializer.Serialize(value, McpJson.ToolOptions);

        // Assert
        Assert.Contains("日本語", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
    }

    private static SqliteMeloreStore NewStore()
    {
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        return store;
    }

    private static void Index(SqliteMeloreStore store, RoslynSyntaxAnalyzer analyzer, string path, string source)
    {
        var file = new RepositoryFile(path, "C:\\tmp\\" + path.Replace('/', '_'), "sha-" + path, "csharp");
        store.UpsertSymbols(store.EnsureFile(file), file, analyzer.AnalyzeFile(path, source), "rev");
    }
}
