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


    /// <summary>コメント内の改行と連続空白は、1 つの空白にまとめる。</summary>
    [Fact]
    public void AnalyzeFile_FlattensCommentWhitespace()
    {
        // Arrange
        var analyzer = new RoslynSyntaxAnalyzer();
        const string source = """
            namespace Demo;
            /// <summary>
            ///   一行目
            ///   二行目
            /// </summary>
            public sealed class Engine {}
            """;

        // Act
        var type = analyzer.AnalyzeFile("Demo.cs", source).Single(s => s.Kind == "class");

        // Assert
        Assert.DoesNotContain("\n", type.DocumentationComment, StringComparison.Ordinal);
        Assert.Contains("一行目", type.DocumentationComment, StringComparison.Ordinal);
        Assert.Contains("二行目", type.DocumentationComment, StringComparison.Ordinal);
    }

    /// <summary>列挙型・プロパティ・イベントを、所属と種別付きで取り出す。</summary>
    [Fact]
    public void AnalyzeFile_ExtractsMemberKinds()
    {
        // Arrange
        var analyzer = new RoslynSyntaxAnalyzer();
        const string source = """
            namespace Demo
            {
                public enum State { Ready }
                public struct Point { public int X { get; set; } }
                public record Item;
                public interface IEngine {}
                public sealed class Engine : IEngine
                {
                    public event System.EventHandler Done { add {} remove {} }
                }
            }
            """;

        // Act
        var symbols = analyzer.AnalyzeFile("Demo.cs", source);

        // Assert
        Assert.Contains(symbols, s => s is { Kind: "enum", Fqn: "Demo.State" });
        Assert.Contains(symbols, s => s is { Kind: "enum_member", Fqn: "Demo.State.Ready", ContainingTypeFqn: "Demo.State" });
        Assert.Contains(symbols, s => s is { Kind: "struct", Fqn: "Demo.Point" });
        Assert.Contains(symbols, s => s is { Kind: "property", Fqn: "Demo.Point.X" });
        Assert.Contains(symbols, s => s is { Kind: "record", Fqn: "Demo.Item" });
        Assert.Contains(symbols, s => s is { Kind: "event", Fqn: "Demo.Engine.Done" });
        Assert.Equal(["IEngine"], symbols.Single(s => s.Fqn == "Demo.Engine").BaseTypeNames);
    }
}
