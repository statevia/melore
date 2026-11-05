using Melore.Core.Models;

namespace Melore.Core.Ports;

/// <summary>
/// C# ソースからシンボルを抽出する。
/// </summary>
public interface ICodeAnalyzer
{
    /// <summary>シンボル抽出の版。出力の形を変えたら上げる。</summary>
    string AnalyzerVersion { get; }

    /// <summary>構文木から定義を取る。コンパイルは必須にしない。</summary>
    IReadOnlyList<ParsedSymbol> AnalyzeFile(string relativePath, string content);
}
