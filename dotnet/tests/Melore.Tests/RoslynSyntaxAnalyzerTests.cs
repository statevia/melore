using Melore.Parsing.Roslyn;

namespace Melore.Tests;

public sealed class RoslynSyntaxAnalyzerTests
{
    /// <summary>クラスと Fact メソッドの行番号を取る。</summary>
    [Fact]
    public void AnalyzeFile_ReadsTypesAndTests()
    {
        // Arrange
        var analyzer = new RoslynSyntaxAnalyzer();
        const string source = """
            namespace Demo;

            public sealed class Sample
            {
                [Fact]
                public void Works()
                {
                }
            }
            """;

        // Act
        var symbols = analyzer.AnalyzeFile("Demo.cs", source);

        // Assert
        Assert.Contains(symbols, s => s is { Kind: "class", Fqn: "Demo.Sample", StartLine: 3 });
        Assert.Empty(symbols.Single(s => s.Kind == "class").BaseTypeNames);
        Assert.Contains(symbols, s => s is { Kind: "method", Name: "Works", IsTest: true, TestFramework: "xunit" });
    }

    /// <summary>埋め込み用の文書は summary と remarks だけ。</summary>
    [Fact]
    public void AnalyzeFile_KeepsSummaryAndRemarksOnly()
    {
        // Arrange
        var analyzer = new RoslynSyntaxAnalyzer();
        const string source = """
            namespace Demo;
            /// <summary>冪等性キーを扱う。</summary>
            /// <remarks>重複を防ぐ。</remarks>
            /// <param name="key">ignored</param>
            public sealed class ExecutionIdempotencyService
            {
                public void Start(string key) {}
            }
            """;

        // Act
        var type = analyzer.AnalyzeFile("Demo.cs", source).Single(s => s.Kind == "class");

        // Assert
        Assert.Contains("冪等性キー", type.DocumentationComment, StringComparison.Ordinal);
        Assert.Contains("重複を防ぐ", type.DocumentationComment, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", type.DocumentationComment, StringComparison.Ordinal);
    }
}
