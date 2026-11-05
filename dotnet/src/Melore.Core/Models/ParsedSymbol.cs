namespace Melore.Core.Models;

/// <summary>
/// C# シンボル定義。
/// </summary>
public sealed record ParsedSymbol(
    string Kind,
    string Fqn,
    string Name,
    string? ContainingTypeFqn,
    string? Signature,
    string Accessibility,
    int StartLine,
    int EndLine,
    string? DocumentationComment,
    bool IsTest,
    string? TestFramework,
    IReadOnlyList<string> BaseTypeNames);
