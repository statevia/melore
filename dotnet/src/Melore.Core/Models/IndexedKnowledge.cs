namespace Melore.Core.Models;

/// <summary>
/// 関係再構築用の型シンボル。
/// </summary>
public sealed record IndexedTypeSymbol(string Id, string Name, string Fqn, string Kind, string? Signature, string Path);

/// <summary>
/// 関係再構築用のチャンク。
/// </summary>
public sealed record IndexedChunk(string Id, string HeadingPath, string Text, string Path);

/// <summary>
/// 関係再構築用のテストメソッド。
/// </summary>
public sealed record IndexedTestMethod(
    string Id,
    string Name,
    string? ContainingTypeId,
    string? ContainingTypeName,
    string Path);
