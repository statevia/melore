using Melore.Core.Models;

namespace Melore.Infrastructure.Git;

/// <summary>
/// <c>git status --porcelain</c> の 1 行をパスへ分ける。
/// </summary>
public static class GitStatusParser
{
    /// <summary>
    /// 標準出力を変更一覧にする。リネームは旧パスの削除と新パスの追加。
    /// </summary>
    public static IReadOnlyList<WorktreeChange> Parse(string stdout)
    {
        var list = new List<WorktreeChange>();
        foreach (var raw in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4)
            {
                continue;
            }

            var code = raw[..2];
            var rest = raw[3..];
            var tab = rest.IndexOf('\t');
            if ((code[0] is 'R' or 'C') && tab >= 0)
            {
                list.Add(new WorktreeChange(Normalize(rest[..tab]), Remove: true));
                list.Add(new WorktreeChange(Normalize(rest[(tab + 1)..]), Remove: false));
                continue;
            }

            var remove = code[0] == 'D' || code[1] == 'D';
            list.Add(new WorktreeChange(Normalize(rest), remove));
        }

        return list;
    }

    private static string Normalize(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value.Replace('\\', '/');
    }
}
