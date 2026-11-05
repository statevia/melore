using Melore.Contracts;
using Melore.Core.Indexing;
using Melore.Core.Models;
using Melore.Infrastructure.Sqlite;
using Melore.Parsing.Markdown;
using Melore.Parsing.Roslyn;

namespace Melore.Tests;

public sealed class RelationshipIndexerTests
{
    /// <summary>見出し一致は explicit、パス語幹は inferred。テストクラス名は explicit。</summary>
    [Fact]
    public void Rebuild_StoresExplicitAndInferredRelationships()
    {
        // Arrange
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        var markdown = new MarkdownDocumentParser();
        var roslyn = new RoslynSyntaxAnalyzer();

        var specFile = new RepositoryFile(
            "docs/specifications/execution/ExecutionEngine.md",
            "C:\\tmp\\ExecutionEngine.md",
            "sha1",
            "markdown");
        store.UpsertDocument(
            store.EnsureFile(specFile),
            specFile,
            markdown.Parse(specFile.RelativePath, """
                # ExecutionEngine

                `RunAsync` を公開する。存在しない `GhostMethod` にも触れる。
                """),
            "rev");

        var layoutFile = new RepositoryFile(
            "docs/specifications/actions/module-zip-layout.md",
            "C:\\tmp\\layout.md",
            "sha2",
            "markdown");
        store.UpsertDocument(
            store.EnsureFile(layoutFile),
            layoutFile,
            markdown.Parse(layoutFile.RelativePath, "# Zip\n\nModuleZipLayout の配置。"),
            "rev");

        var engineFile = new RepositoryFile("ExecutionEngine.cs", "C:\\tmp\\ExecutionEngine.cs", "sha3", "csharp");
        store.UpsertSymbols(
            store.EnsureFile(engineFile),
            engineFile,
            roslyn.AnalyzeFile("ExecutionEngine.cs", """
                namespace Demo;
                public sealed class ExecutionEngine
                {
                    public void RunAsync() {}
                }
                """),
            "rev");

        var layoutTypeFile = new RepositoryFile("ModuleZipLayout.cs", "C:\\tmp\\ModuleZipLayout.cs", "sha4", "csharp");
        store.UpsertSymbols(
            store.EnsureFile(layoutTypeFile),
            layoutTypeFile,
            roslyn.AnalyzeFile("ModuleZipLayout.cs", """
                namespace Demo;
                public sealed class ModuleZipLayout {}
                """),
            "rev");

        var testFile = new RepositoryFile("ExecutionEngineTests.cs", "C:\\tmp\\ExecutionEngineTests.cs", "sha5", "csharp");
        store.UpsertSymbols(
            store.EnsureFile(testFile),
            testFile,
            roslyn.AnalyzeFile("ExecutionEngineTests.cs", """
                namespace Demo;
                public sealed class ExecutionEngineTests
                {
                    [Fact]
                    public void Runs() {}
                }
                """),
            "rev");

        var sameFile = new RepositoryFile("Widget.cs", "C:\\tmp\\Widget.cs", "sha6", "csharp");
        store.UpsertSymbols(
            store.EnsureFile(sameFile),
            sameFile,
            roslyn.AnalyzeFile("Widget.cs", """
                namespace Demo;
                public sealed class Widget
                {
                    [Fact]
                    public void WidgetFact() {}
                }
                """),
            "rev");

        // Act
        RelationshipIndexer.Rebuild(store);

        // Assert
        Assert.True(store.CountRelationships("specifies", "explicit") > 0);
        Assert.True(store.CountRelationships("specifies", "inferred") > 0);
        Assert.True(store.CountRelationships("tested_by", "explicit") > 0);
        Assert.True(store.CountRelationships("tested_by", "inferred") > 0);
        Assert.True(store.CountRelationships("contradicts", "inferred") > 0);
        Assert.Equal(0, store.CountRelationships("contradicts", "explicit"));
    }
}
