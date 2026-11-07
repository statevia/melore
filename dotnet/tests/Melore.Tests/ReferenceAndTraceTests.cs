using Melore.Core.Models;
using Melore.Core.Search;
using Melore.Infrastructure.Sqlite;
using Melore.Parsing.Roslyn;

namespace Melore.Tests;

public sealed class ReferenceAndTraceTests
{
    /// <summary>クラスのメンバーと基底型名が related に出る。</summary>
    [Fact]
    public void References_ReturnsMembersAndBaseType()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var analyzer = new RoslynSyntaxAnalyzer();
        const string source = """
            namespace Demo;
            public interface IEngine {}
            public sealed class Engine : IEngine
            {
                public void Run() {}
            }
            """;
        var file = new RepositoryFile("Engine.cs", "C:\\tmp\\Engine.cs", "sha", "csharp");
        store.UpsertSymbols(store.EnsureFile(file), file, analyzer.AnalyzeFile("Engine.cs", source), "rev");
        var references = new ReferenceService(store);

        // Act
        var related = references.GetReferences("Demo.Engine", 32);

        // Assert
        Assert.Contains(related, r => r.RelType == "declares" && r.Hit.Title.Contains("Run", StringComparison.Ordinal));
        Assert.Contains(related, r => r.RelType == "inherits" && r.Basis == Melore.Contracts.RelationshipBasis.Inferred);
    }

    /// <summary>trace は depth 内の辺と、calls 未抽出の注記を返す。</summary>
    [Fact]
    public void Trace_IncludesLimitationWhenCallsMissing()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var analyzer = new RoslynSyntaxAnalyzer();
        const string source = """
            namespace Demo;
            public sealed class Engine
            {
                public void Run() {}
            }
            """;
        var file = new RepositoryFile("Engine.cs", "C:\\tmp\\Engine.cs", "sha", "csharp");
        store.UpsertSymbols(store.EnsureFile(file), file, analyzer.AnalyzeFile("Engine.cs", source), "rev");

        // Act
        var result = new TraceService(store).Trace("Demo.Engine", 2);

        // Assert
        Assert.NotNull(result.Root);
        Assert.Contains(result.Edges, e => e.RelType == "declares");
        Assert.Contains("呼び出し", result.Limitation, StringComparison.Ordinal);
    }


    /// <summary>関係の取得元が複数あっても limit を超えない。</summary>
    [Fact]
    public void References_NeverExceedsLimit()
    {
        // Arrange
        using var store = NewStore("""
            namespace Demo;
            public sealed class Engine
            {
                public void A() {}
                public void B() {}
                public void C() {}
                public void D() {}
            }
            public sealed class EngineTests
            {
                [Fact]
                public void Runs() {}
            }
            """);

        // Act
        var related = new ReferenceService(store).GetReferences("Demo.Engine", 2);

        // Assert
        Assert.Equal(2, related.Count);
    }

    /// <summary>同じヒットは 1 件にまとめる。</summary>
    [Fact]
    public void References_DeduplicatesHits()
    {
        // Arrange
        using var store = NewStore("""
            namespace Demo;
            public sealed class Engine
            {
                public void Run() {}
            }
            """);

        // Act
        var related = new ReferenceService(store).GetReferences("Demo.Engine", 32);

        // Assert
        Assert.Equal(related.Count, related.Select(r => r.Hit.Id).Distinct().Count());
    }

    /// <summary>空白だけの指定と、見つからない指定は空を返す。</summary>
    [Theory]
    [InlineData("   ")]
    [InlineData("Demo.Nothing")]
    public void References_UnknownTarget_ReturnsEmpty(string target)
    {
        // Arrange
        using var store = NewStore("namespace Demo; public sealed class Engine {}");

        // Act
        var related = new ReferenceService(store).GetReferences(target, 8);

        // Assert
        Assert.Empty(related);
    }

    /// <summary>relTypes で絞ると、指定外の関係は辺にならない。</summary>
    [Fact]
    public void Trace_RelTypes_KeepsOnlyAllowedEdges()
    {
        // Arrange
        using var store = NewStore("""
            namespace Demo;
            public interface IEngine {}
            public sealed class Engine : IEngine
            {
                public void Run() {}
            }
            """);

        // Act
        var result = new TraceService(store).Trace("Demo.Engine", 2, relTypes: ["DECLARES"]);

        // Assert
        Assert.NotEmpty(result.Edges);
        Assert.All(result.Edges, e => Assert.Equal("declares", e.RelType));
    }

    /// <summary>見つからないシンボルは起点なしで理由を返す。</summary>
    [Fact]
    public void Trace_UnknownSymbol_ReturnsLimitation()
    {
        // Arrange
        using var store = NewStore("namespace Demo; public sealed class Engine {}");

        // Act
        var result = new TraceService(store).Trace("Demo.Nothing", 2);

        // Assert
        Assert.Null(result.Root);
        Assert.Contains("見つかりません", result.Limitation, StringComparison.Ordinal);
    }

    /// <summary>ノード数の上限で止めたら、打ち切りを注記に足す。</summary>
    [Fact]
    public void Trace_NodeCap_ReportsTruncation()
    {
        // Arrange
        using var store = NewStore("""
            namespace Demo;
            public sealed class Engine
            {
                public void A() {}
                public void B() {}
                public void C() {}
            }
            """);

        // Act
        var result = new TraceService(store).Trace("Demo.Engine", 2, limit: 2);

        // Assert
        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains("打ち切り", result.Limitation, StringComparison.Ordinal);
    }

    private static SqliteMeloreStore NewStore(string source)
    {
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var file = new RepositoryFile("Engine.cs", "C:\\tmp\\Engine.cs", "sha", "csharp");
        store.UpsertSymbols(store.EnsureFile(file), file, new RoslynSyntaxAnalyzer().AnalyzeFile("Engine.cs", source), "rev");
        return store;
    }
}
