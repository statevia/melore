using Melore.Parsing.Markdown;

namespace Melore.Tests;

public sealed class MarkdownDocumentParserTests
{
    /// <summary>見出しの行番号が 1-origin で保持される。</summary>
    [Fact]
    public void Parse_AssignsHeadingLineRange()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        const string markdown = """
            # Title

            Intro paragraph.

            ## Fork Join

            Fork は枝を分ける。
            """;

        // Act
        var document = parser.Parse("docs/specifications/execution/fork-join.md", markdown);

        // Assert
        Assert.Equal("specification", document.DocKind);
        var fork = Assert.Single(document.Chunks, c => c.HeadingPath.Contains("Fork Join", StringComparison.Ordinal));
        Assert.Equal(5, fork.StartLine);
        Assert.Contains("枝を分ける", fork.Text, StringComparison.Ordinal);
        Assert.True(fork.EndLine >= fork.StartLine);
    }

    /// <summary>相対 Markdown リンクを保存する。</summary>
    [Fact]
    public void Parse_CollectsRelativeLinks()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        const string markdown = """
            # A

            See [fsm](./fsm.md#state).
            """;

        // Act
        var document = parser.Parse("docs/specifications/execution/fork-join.md", markdown);

        // Assert
        var link = Assert.Single(document.Links);
        Assert.Equal("docs/specifications/execution/fsm.md", link.TargetRelativePath);
        Assert.Equal(3, link.Line);
    }
}
