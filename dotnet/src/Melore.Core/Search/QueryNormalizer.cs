namespace Melore.Core.Search;

/// <summary>
/// FTS 用のトークン正規化。助詞を落とし、短いシノニムを足す。
/// </summary>
public static class QueryNormalizer
{
    private static readonly HashSet<string> JapaneseParticles = new(StringComparer.Ordinal)
    {
        "の", "は", "を", "が", "と", "で", "に", "へ", "も", "か", "や", "よ",
        "より", "から", "まで", "です", "する", "した", "して", "ない", "無い",
        "どう", "なる", "何", "について", "とは", "など", "また", "および"
    };

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "and", "or", "to", "in", "for", "is", "are"
    };

    private static readonly (string Needle, string Extra)[] Synonyms =
    [
        ("シナリオテスト", "Scenario"),
        ("テナントヘッダ", "tenant"),
        ("zip", "module-zip"),
        ("レイアウト", "layout"),
        ("feature-first", "feature"),
        ("ログマスキング", "masking"),
        ("禁止", "NewGuid")
    ];

    /// <summary>MATCH に載せるトークン。助詞と 1 文字（`$` 以外）は除く。</summary>
    public static IReadOnlyList<string> Tokens(string query)
    {
        var tokens = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(query, @"\$+|[\p{L}\p{N}_-]+"))
        {
            var value = StripParticles(match.Value.Trim('-'));
            if (value.Length == 0 || Stopwords.Contains(value) || JapaneseParticles.Contains(value))
            {
                continue;
            }

            if (value.Length == 1 && value != "$")
            {
                continue;
            }

            tokens.Add(value);
        }

        return tokens;
    }

    /// <summary>元クエリとシノニムを足した検索文字列。最大 3 件。</summary>
    public static IReadOnlyList<string> Expand(string query)
    {
        var trimmed = query.Trim();
        var list = new List<string> { trimmed };
        foreach (var (needle, extra) in Synonyms)
        {
            if (trimmed.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(extra);
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
    }

    private static string StripParticles(string value)
    {
        var changed = true;
        while (changed && value.Length > 0)
        {
            changed = false;
            foreach (var stop in JapaneseParticles.OrderByDescending(s => s.Length))
            {
                if (value.Length > stop.Length && value.StartsWith(stop, StringComparison.Ordinal))
                {
                    value = value[stop.Length..];
                    changed = true;
                    break;
                }

                if (value.Length > stop.Length && value.EndsWith(stop, StringComparison.Ordinal))
                {
                    value = value[..^stop.Length];
                    changed = true;
                    break;
                }
            }
        }

        return value;
    }
}
