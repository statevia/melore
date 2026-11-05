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

    private static readonly Regex BacktickName = new("`([A-Z][A-Za-z0-9_]{3,})`", RegexOptions.Compiled);
    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "MUST", "SHOULD", "GET", "POST", "PUT", "HTTP", "JSON", "YAML", "NULL", "TRUE", "FALSE"
    };

    /// <summary>
    /// 派生関係を作り直す。
    /// </summary>
    public static int Rebuild(IMeloreStore store)
    {
        store.DeleteRelationshipsByType("specifies");
        store.DeleteRelationshipsByType("contradicts");
        store.DeleteRelationshipsByType("tested_by");

        var types = store.ListTypeSymbols();
        var chunks = store.ListChunks();
        var tests = store.ListTestMethods();
        var byName = types
            .Where(t => t.Name.Length >= 4)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var added = 0;
        added += AddTestedBy(store, tests, byName);
        added += AddSpecifies(store, chunks, types, byName);
        return added;
    }

    private static int AddTestedBy(
        IMeloreStore store,
        IReadOnlyList<IndexedTestMethod> tests,
        Dictionary<string, List<IndexedTypeSymbol>> byName)
    {
        var added = 0;
        foreach (var test in tests)
        {
            var className = test.ContainingTypeName;
            if (className is null)
            {
                continue;
            }

            var shortName = className.Contains('.', StringComparison.Ordinal)
                ? className[(className.LastIndexOf('.') + 1)..]
                : className;
            if (shortName.EndsWith("Tests", StringComparison.Ordinal) && shortName.Length > 5)
            {
                var target = shortName[..^5];
                if (byName.TryGetValue(target, out var matches))
                {
                    foreach (var type in matches.Take(2))
                    {
                        store.AddRelationship(
                            "symbol",
                            type.Id,
                            "symbol",
                            test.Id,
                            "tested_by",
                            RelationshipBasis.Explicit,
                            1.0,
                            "class name " + shortName);
                        added++;
                    }

                    continue;
                }
            }

            if (test.ContainingTypeId is not null)
            {
                store.AddRelationship(
                    "symbol",
                    test.ContainingTypeId,
                    "symbol",
                    test.Id,
                    "tested_by",
                    RelationshipBasis.Inferred,
                    0.4,
                    "same file test class");
                added++;
            }
        }

        return added;
    }

    private static int AddSpecifies(
        IMeloreStore store,
        IReadOnlyList<IndexedChunk> chunks,
        IReadOnlyList<IndexedTypeSymbol> types,
        Dictionary<string, List<IndexedTypeSymbol>> byName)
    {
        var added = 0;
        foreach (var chunk in chunks)
        {
            if (added >= 2000)
            {
                break;
            }

            var headingTail = HeadingTail(chunk.HeadingPath);
            if (headingTail.Length >= 4 && byName.TryGetValue(headingTail, out var exact))
            {
                foreach (var type in exact.Take(2))
                {
                    store.AddRelationship(
                        "document_chunk",
                        chunk.Id,
                        "symbol",
                        type.Id,
                        "specifies",
                        RelationshipBasis.Explicit,
                        1.0,
                        "heading equals " + type.Name);
                    added++;
                    MaybeContradict(store, chunk, type);
                }

                continue;
            }

            foreach (var type in types)
            {
                if (type.Name.Length < 6)
                {
                    continue;
                }

                var pathCompact = Compact(Path.GetFileNameWithoutExtension(chunk.Path));
                var nameCompact = Compact(type.Name);
                if (pathCompact.Length >= 6 && pathCompact == nameCompact)
                {
                    store.AddRelationship(
                        "document_chunk",
                        chunk.Id,
                        "symbol",
                        type.Id,
                        "specifies",
                        RelationshipBasis.Inferred,
                        0.55,
                        "path stem " + type.Name);
                    added++;
                    MaybeContradict(store, chunk, type);
                    break;
                }
            }
        }

        return added;
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
        foreach (Match match in BacktickName.Matches(chunk.Text))
        {
            var name = match.Groups[1].Value;
            if (CommonWords.Contains(name) || names.Contains(name))
            {
                continue;
            }

            store.AddRelationship(
                "document_chunk",
                chunk.Id,
                "symbol",
                type.Id,
                "contradicts",
                RelationshipBasis.Inferred,
                0.35,
                "backtick " + name + " not in members of " + type.Name);
            break;
        }
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
        => Regex.Replace(value, "[^A-Za-z0-9]", "", RegexOptions.None).ToLowerInvariant();
}
