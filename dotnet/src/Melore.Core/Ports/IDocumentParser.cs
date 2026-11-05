using Melore.Core.Models;

namespace Melore.Core.Ports;

/// <summary>
/// Markdown を見出し単位に分割する。
/// </summary>
public interface IDocumentParser
{
    /// <summary>チャンクの切り方の版。出力の形を変えたら上げる。</summary>
    string AnalyzerVersion { get; }

    /// <summary>本文と行番号付きチャンクを返す。</summary>
    ParsedDocument Parse(string relativePath, string content);
}
