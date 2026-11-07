using System.Text.RegularExpressions;
using Melore.Contracts;
using Melore.Core.Models;
using Melore.Core.Ports;

namespace Melore.Core.Indexing;

/// <summary>
/// ファイル横断の specifies / tested_by / contradicts を規則で作る。
/// </summary>
public static class RelationshipIndexer
{
    /// <summary>関係規則の版。規則の出力を変えたら上げる。既存チャンクだけで足りる変更は上げなくてよい。</summary>
    public const string AnalyzerVersion = "rel:1";

    private const string SymbolKind = "symbol";
    private const string ChunkKind = "document_chunk";
    private const string TestedBy = "tested_by";
    private const string Specifies = "specifies";
    private const string Contradicts = "contradicts";

    /// <summary>specifies の追加を止める目安。巨大な文書群で関係が増えすぎないようにする。</summary>
    private const int MaxSpecifies = 2000;

    /// <summary>1 つの名前に複数の型があるとき、関係を張る型の上限。</summary>
    private const int MaxTypesPerName = 2;

    private static readonly Regex BacktickName = new("`([A-Z][A-Za-z0-9_]{3,})`", RegexOptions.NonBacktracking);
    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "MUST", "SHOULD", "GET", "POST", "PUT", "HTTP", "JSON", "YAML", "NULL", "TRUE", "FALSE"
    };

    /// <summary>
    /// 派生関係を作り直す。
    /// </summary>
    public static int Rebuild(IMeloreStore store)
    {
        store.DeleteRelationshipsByType(Specifies);
        store.DeleteRelationshipsByType(Contradicts);
        store.DeleteRelationshipsByType(TestedBy);

        var types = store.ListTypeSymbols();
        var chunks = store.ListChunks();
        var tests = store.ListTestMethods();
        var byName = types
            .Where(t => t.Name.Length >= 4)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var byCompactName = types
            .Where(t => t.Name.Length >= 6)
            .GroupBy(t => Compact(t.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var added = 0;
        added += AddTestedBy(store, tests, byName);
        added += AddSpecifies(store, chunks, byName, byCompactName);
        return added;
    }

    private static int AddTestedBy(
        IMeloreStore store,
        IReadOnlyList<IndexedTestMethod> tests,
        Dictionary<string, List<IndexedTypeSymbol>> byName)
        => tests
            .Where(test => test.ContainingTypeName is not null)
            .Sum(test => LinkTest(store, test, byName));

    /// <summary>
    /// クラス名の規約で対象型を探し、無ければ同じファイルのテストクラスへ推定の関係を張る。
    /// </summary>
    private static int LinkTest(
        IMeloreStore store,
        IndexedTestMethod test,
        Dictionary<string, List<IndexedTypeSymbol>> byName)
    {
        var className = test.ContainingTypeName!;
        var shortName = className[(className.LastIndexOf('.') + 1)..];
        if (TestTarget(shortName) is { } target && byName.TryGetValue(target, out var matches))
        {
            var targets = matches.Take(MaxTypesPerName).ToList();
            targets.ForEach(type => store.AddRelationship(new RelationshipEntry(
                SymbolKind,
                type.Id,
                SymbolKind,
                test.Id,
                TestedBy,
                RelationshipBasis.Explicit,
                1.0,
                "class name " + shortName)));
            return targets.Count;
        }

        if (test.ContainingTypeId is null)
        {
            return 0;
        }

        store.AddRelationship(new RelationshipEntry(
            SymbolKind,
            test.ContainingTypeId,
            SymbolKind,
            test.Id,
            TestedBy,
            RelationshipBasis.Inferred,
            0.4,
            "same file test class"));
        return 1;
    }

    private static string? TestTarget(string shortName)
        => shortName.EndsWith("Tests", StringComparison.Ordinal) && shortName.Length > 5
            ? shortName[..^5]
            : null;

    private static int AddSpecifies(
        IMeloreStore store,
        IReadOnlyList<IndexedChunk> chunks,
        Dictionary<string, List<IndexedTypeSymbol>> byName,
        Dictionary<string, IndexedTypeSymbol> byCompactName)
    {
        var added = 0;
        foreach (var chunk in chunks)
        {
            if (added >= MaxSpecifies)
            {
                break;
            }

            added += LinkChunk(store, chunk, byName, byCompactName);
        }

        return added;
    }

    /// <summary>
    /// 見出しの末尾が型名なら確定、ファイル名が型名と一致すれば推定の関係を張る。
    /// </summary>
    private static int LinkChunk(
        IMeloreStore store,
        IndexedChunk chunk,
        Dictionary<string, List<IndexedTypeSymbol>> byName,
        Dictionary<string, IndexedTypeSymbol> byCompactName)
    {
        var headingTail = HeadingTail(chunk.HeadingPath);
        if (headingTail.Length >= 4 && byName.TryGetValue(headingTail, out var exact))
        {
            var targets = exact.Take(MaxTypesPerName).ToList();
            targets.ForEach(type => AddSpecified(
                store,
                chunk,
                type,
                RelationshipBasis.Explicit,
                1.0,
                "heading equals " + type.Name));
            return targets.Count;
        }

        var pathCompact = Compact(Path.GetFileNameWithoutExtension(chunk.Path));
        if (pathCompact.Length < 6 || !byCompactName.TryGetValue(pathCompact, out var byPath))
        {
            return 0;
        }

        AddSpecified(store, chunk, byPath, RelationshipBasis.Inferred, 0.55, "path stem " + byPath.Name);
        return 1;
    }

    private static void AddSpecified(
        IMeloreStore store,
        IndexedChunk chunk,
        IndexedTypeSymbol type,
        RelationshipBasis basis,
        double confidence,
        string evidence)
    {
        store.AddRelationship(new RelationshipEntry(
            ChunkKind,
            chunk.Id,
            SymbolKind,
            type.Id,
            Specifies,
            basis,
            confidence,
            evidence));
        MaybeContradict(store, chunk, type);
    }

    private static void MaybeContradict(IMeloreStore store, IndexedChunk chunk, IndexedTypeSymbol type)
    {
        var members = store.GetDeclaredMembers(type.Fqn);
        var names = members
            .Select(m =>
            {
                var title = m.Hit.Title;
                var dot = title.LastIndexOf('.');
                return dot < 0 ? title : title[(dot + 1)..];
            })
            .ToHashSet(StringComparer.Ordinal);
        names.Add(type.Name);
        var unknown = BacktickName.Matches(chunk.Text)
            .Select(match => match.Groups[1].Value)
            .FirstOrDefault(name => !CommonWords.Contains(name) && !names.Contains(name));
        if (unknown is null)
        {
            return;
        }

        store.AddRelationship(new RelationshipEntry(
            ChunkKind,
            chunk.Id,
            SymbolKind,
            type.Id,
            Contradicts,
            RelationshipBasis.Inferred,
            0.35,
            "backtick " + unknown + " not in members of " + type.Name));
    }

    private static string HeadingTail(string headingPath)
    {
        var parts = headingPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "";
        }

        var tail = parts[^1];
        return tail.Trim('`', '*', '_', ' ', '\t');
    }

    private static string Compact(string value)
        => string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();
}
