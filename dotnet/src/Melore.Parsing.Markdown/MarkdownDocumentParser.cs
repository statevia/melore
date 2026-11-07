using System.Text.RegularExpressions;
using Melore.Core.Models;
using Melore.Core.Ports;

namespace Melore.Parsing.Markdown;

/// <summary>
/// ATX 見出しでセクション分割し、1-origin の行範囲を付ける。
/// </summary>
public sealed class MarkdownDocumentParser : IDocumentParser
{
    /// <inheritdoc />
    public string AnalyzerVersion => "markdown:1";

    // 対象リポジトリの本文を読むので、行の長さに線形な NonBacktracking を使う。
    // タイムアウトだと発動時に例外で索引全体が止まる。
    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)$", RegexOptions.NonBacktracking);
    private static readonly Regex LinkPattern = new(@"\[[^\]]*\]\(([^)]+)\)", RegexOptions.NonBacktracking);
    private const int MaxChunkChars = 8000;

    /// <inheritdoc />
    public ParsedDocument Parse(string relativePath, string content)
    {
        var lines = SplitLines(content);
        var sections = new SectionBuffer(relativePath);
        var links = new List<DocumentLink>();

        for (var i = BodyStart(lines); i < lines.Count; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            CollectLinks(relativePath, line, lineNumber, links);

            var match = HeadingPattern.Match(line);
            if (match.Success)
            {
                sections.BeginHeading(match.Groups[1].Value.Length, match.Groups[2].Value.Trim(), lineNumber);
            }

            sections.Append(lineNumber, line);
        }

        sections.Flush();
        return new ParsedDocument(sections.Title, Classify(relativePath), sections.Chunks, links);
    }

    /// <summary>
    /// 本文の先頭の添字を返す。YAML front matter は検索対象にしない。閉じていなければ本文として扱う。
    /// </summary>
    private static int BodyStart(List<string> lines)
    {
        if (lines.Count == 0 || !lines[0].StartsWith("---", StringComparison.Ordinal))
        {
            return 0;
        }

        var end = lines.FindIndex(1, x => x.StartsWith("---", StringComparison.Ordinal));
        return end > 0 ? end + 1 : 0;
    }

    private static List<string> SplitLines(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static string Classify(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Contains("/specifications/", StringComparison.OrdinalIgnoreCase))
        {
            return "specification";
        }

        if (normalized.Contains("/concepts/", StringComparison.OrdinalIgnoreCase))
        {
            return "concept";
        }

        if (normalized.Contains("/guides/", StringComparison.OrdinalIgnoreCase))
        {
            return "guide";
        }

        if (normalized.Contains("/architecture/", StringComparison.OrdinalIgnoreCase))
        {
            return "architecture";
        }

        return "other";
    }

    private static void CollectLinks(
        string relativePath,
        string line,
        int lineNumber,
        List<DocumentLink> links)
    {
        foreach (Match match in LinkPattern.Matches(line))
        {
            var raw = match.Groups[1].Value.Trim();
            var hash = raw.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                raw = raw[..hash];
            }

            if (raw.Length == 0 ||
                raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
            var combined = string.IsNullOrEmpty(directory) ? raw : directory + "/" + raw;
            var normalized = NormalizeRelative(combined);
            links.Add(new DocumentLink(normalized, lineNumber));
        }
    }

    private static string NormalizeRelative(string path)
    {
        var parts = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }

    private static IEnumerable<(string Text, int StartLine, int EndLine)> SplitOversized(
        string text,
        int startLine,
        int endLine)
    {
        if (text.Length <= MaxChunkChars)
        {
            yield return (text, startLine, endLine);
            yield break;
        }

        var paragraphs = text.Split("\n\n", StringSplitOptions.None);
        var builder = new System.Text.StringBuilder();
        var currentStart = startLine;
        var consumedLines = 0;
        foreach (var paragraph in paragraphs)
        {
            if (builder.Length > 0 && builder.Length + paragraph.Length > MaxChunkChars)
            {
                yield return (builder.ToString().Trim(), currentStart, startLine + consumedLines);
                builder.Clear();
                currentStart = startLine + consumedLines;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(paragraph);
            consumedLines += paragraph.Split('\n').Length + 1;
        }

        if (builder.Length > 0)
        {
            yield return (builder.ToString().Trim(), currentStart, endLine);
        }
    }

    /// <summary>
    /// 見出しの階層と、見出しごとに溜めた行を持つ。見出しが変わるたびにチャンクへ確定する。
    /// </summary>
    /// <param name="relativePath">見出しが無いときの名前に使うパス。</param>
    private sealed class SectionBuffer(string relativePath)
    {
        private readonly string?[] _headings = new string?[7];
        private readonly List<(int Line, string Text)> _lines = [];
        private int _sectionStart = 1;
        private int _ordinal;

        public List<DocumentChunk> Chunks { get; } = [];

        public string Title
            => _headings.Skip(1).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
               ?? Path.GetFileNameWithoutExtension(relativePath);

        public void BeginHeading(int level, string title, int lineNumber)
        {
            Flush();
            Array.Clear(_headings, level, _headings.Length - level);
            _headings[level] = title;
            _sectionStart = lineNumber;
        }

        public void Append(int lineNumber, string line) => _lines.Add((lineNumber, line));

        public void Flush()
        {
            if (_lines.Count == 0)
            {
                return;
            }

            var headingPath = HeadingPath();
            var text = string.Join('\n', _lines.Select(x => x.Text)).Trim();
            var endLine = _lines[^1].Line;
            _lines.Clear();
            if (text.Length == 0)
            {
                return;
            }

            Chunks.AddRange(SplitOversized(text, _sectionStart, endLine)
                .Select(part => new DocumentChunk(
                    headingPath,
                    SpecId: null,
                    part.Text,
                    part.StartLine,
                    part.EndLine,
                    _ordinal++)));
        }

        private string HeadingPath()
        {
            var parts = _headings.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
            return parts.Count == 0 ? Path.GetFileNameWithoutExtension(relativePath) : string.Join(" / ", parts);
        }
    }
}
