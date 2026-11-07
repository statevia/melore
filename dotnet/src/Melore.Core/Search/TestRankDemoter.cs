using Melore.Contracts;

namespace Melore.Core.Search;

/// <summary>
/// テスト語が無いクエリでは、テストのヒットを実装より後ろへ下げる。
/// </summary>
public static class TestRankDemoter
{
    /// <summary>クエリがテストを探しているか。</summary>
    public static bool QueryWantsTests(string query)
        => query.Contains("テスト", StringComparison.Ordinal)
           || query.Contains("検証", StringComparison.Ordinal)
           || query.Contains("test", StringComparison.OrdinalIgnoreCase);

    /// <summary>パスがテスト配下か。</summary>
    public static bool IsTestHit(SearchHit hit)
    {
        var path = hit.Source.Path.Replace('\\', '/');
        return path.Contains("/Tests/", StringComparison.OrdinalIgnoreCase)
               || path.Contains(".Tests/", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>テスト以外を先に、相対順は保つ。</summary>
    public static IReadOnlyList<SearchHit> Apply(string query, IReadOnlyList<SearchHit> hits)
    {
        if (QueryWantsTests(query))
        {
            return hits;
        }

        var main = new List<SearchHit>();
        var tests = new List<SearchHit>();
        foreach (var hit in hits)
        {
            if (IsTestHit(hit))
            {
                tests.Add(hit);
            }
            else
            {
                main.Add(hit);
            }
        }

        if (tests.Count == 0)
        {
            return hits;
        }

        main.AddRange(tests);
        return main;
    }
}
