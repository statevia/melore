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

    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new(@"\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled);
    private const int MaxChunkChars = 8000;

    /// <inheritdoc />
    public ParsedDocument Parse(string relativePath, string content)
    {
        var lines = SplitLines(content);
        var headings = new string?[7];
        var chunks = new List<DocumentChunk>();
        var links = new List<DocumentLink>();
        var buffer = new List<(int Line, string Text)>();
        var sectionStart = 1;
        var ordinal = 0;

        void Flush()
        {
            if (buffer.Count == 0)
            {
                return;
            }

            var headingPath = BuildHeadingPath(headings, relativePath);
            var text = string.Join('\n', buffer.Select(x => x.Text)).Trim();
            var endLine = buffer[^1].Line;
            if (text.Length == 0)
            {
                buffer.Clear();
                return;
            }

            foreach (var part in SplitOversized(text, sectionStart, endLine))
            {
                chunks.Add(new DocumentChunk(
                    headingPath,
                    SpecId: null,
                    part.Text,
                    part.StartLine,
                    part.EndLine,
                    ordinal++));
            }

            buffer.Clear();
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            CollectLinks(relativePath, line, lineNumber, links);

            var match = HeadingPattern.Match(line);
            if (match.Success)
            {
                Flush();
                var level = match.Groups[1].Value.Length;
                var headingTitle = match.Groups[2].Value.Trim();
                for (var l = level; l <= 6; l++)
                {
                    headings[l] = null;
                }

                headings[level] = headingTitle;
                sectionStart = lineNumber;
                buffer.Add((lineNumber, line));
                continue;
            }

            if (i == 0 && line.StartsWith("---", StringComparison.Ordinal))
            {
                // YAML front matter は検索対象にしない。
                var end = lines.FindIndex(1, x => x.StartsWith("---", StringComparison.Ordinal));
                if (end > 0)
                {
                    i = end;
                    continue;
                }
            }

            buffer.Add((lineNumber, line));
        }

        Flush();

        var title = headings.Skip(1).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? Path.GetFileNameWithoutExtension(relativePath);
        return new ParsedDocument(title, Classify(relativePath), chunks, links);
    }

    private static List<string> SplitLines(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    private static string BuildHeadingPath(string?[] headings, string relativePath)
    {
        var parts = headings.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
        return parts.Count == 0 ? Path.GetFileNameWithoutExtension(relativePath) : string.Join(" / ", parts);
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
}
