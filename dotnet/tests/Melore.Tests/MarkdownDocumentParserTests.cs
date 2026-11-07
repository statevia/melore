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

    /// <summary>末尾改行で endLine がファイル行数を超えない。</summary>
    [Fact]
    public void Parse_TrailingNewline_EndLineStaysInFile()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        const string markdown = "# Title\n\nBody line.\n";
        var physical = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Length;

        // Act
        var document = parser.Parse("docs/a.md", markdown);

        // Assert
        var chunk = Assert.Single(document.Chunks);
        Assert.InRange(chunk.EndLine, 1, physical);
    }


    /// <summary>YAML front matter は本文にせず、行番号は元のファイルのまま数える。</summary>
    [Fact]
    public void Parse_FrontMatter_IsSkipped()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        const string markdown = "---\ntitle: secret\n---\n# Title\n\nBody.\n";

        // Act
        var document = parser.Parse("docs/a.md", markdown);

        // Assert
        var chunk = Assert.Single(document.Chunks);
        Assert.Equal(4, chunk.StartLine);
        Assert.DoesNotContain("secret", chunk.Text, StringComparison.Ordinal);
    }

    /// <summary>閉じていない front matter は本文として残す。</summary>
    [Fact]
    public void Parse_UnclosedFrontMatter_StaysInBody()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        const string markdown = "---\nnot closed\n";

        // Act
        var document = parser.Parse("docs/a.md", markdown);

        // Assert
        var chunk = Assert.Single(document.Chunks);
        Assert.Contains("not closed", chunk.Text, StringComparison.Ordinal);
    }

    /// <summary>上位の見出しが変わると、下位の見出しを引き継がない。</summary>
    [Fact]
    public void Parse_NewHeading_ClearsDeeperLevels()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        const string markdown = "# A\n\n## B\n\ntext\n\n# C\n\ntext\n";

        // Act
        var document = parser.Parse("docs/a.md", markdown);

        // Assert
        Assert.Equal(["A", "A / B", "C"], document.Chunks.Select(c => c.HeadingPath));
        Assert.Equal([0, 1, 2], document.Chunks.Select(c => c.Ordinal));
    }

    /// <summary>見出しが無ければファイル名を題名と見出しに使う。</summary>
    [Fact]
    public void Parse_NoHeading_UsesFileName()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();

        // Act
        var document = parser.Parse("docs/guides/setup.md", "plain text\n");

        // Assert
        Assert.Equal("setup", document.Title);
        Assert.Equal("setup", Assert.Single(document.Chunks).HeadingPath);
    }

    /// <summary>上限を超える節は段落の区切りで分ける。</summary>
    [Fact]
    public void Parse_OversizedSection_SplitsAtParagraphs()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        var paragraph = new string('a', 5000);
        var markdown = "# Title\n\n" + paragraph + "\n\n" + paragraph + "\n";

        // Act
        var document = parser.Parse("docs/a.md", markdown);

        // Assert
        Assert.True(document.Chunks.Count >= 2);
        Assert.All(document.Chunks, c => Assert.True(c.Text.Length <= 8000));
    }

    /// <summary>外部 URL・アンカーだけのリンクは捨て、.. は解決する。</summary>
    [Fact]
    public void Parse_Links_KeepOnlyRepositoryPaths()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        const string markdown = "[a](https://example.com) [b](mailto:x@example.com) [c](#top) [d](./x/../../y.md)\n";

        // Act
        var document = parser.Parse("docs/guides/a.md", markdown);

        // Assert
        Assert.Equal("docs/y.md", Assert.Single(document.Links).TargetRelativePath);
    }

    /// <summary>パスの区分で文書の種類を決める。</summary>
    [Theory]
    [InlineData("docs/concepts/a.md", "concept")]
    [InlineData("docs/guides/a.md", "guide")]
    [InlineData("docs/architecture/a.md", "architecture")]
    [InlineData("docs/a.md", "other")]
    public void Parse_ClassifiesByPath(string path, string expected)
    {
        // Arrange
        var parser = new MarkdownDocumentParser();

        // Act
        var document = parser.Parse(path, "# Title\n");

        // Assert
        Assert.Equal(expected, document.DocKind);
    }

    /// <summary>閉じないリンクの長い行でも、バックトラックせず短時間で終わる。</summary>
    [Fact]
    public void Parse_PathologicalLinkLine_FinishesQuickly()
    {
        // Arrange
        var parser = new MarkdownDocumentParser();
        var markdown = new string('[', 200_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var document = parser.Parse("docs/a.md", markdown);

        // Assert
        Assert.Empty(document.Links);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }
}
