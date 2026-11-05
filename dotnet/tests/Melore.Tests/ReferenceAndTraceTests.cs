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
}
