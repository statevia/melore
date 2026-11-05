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
}
