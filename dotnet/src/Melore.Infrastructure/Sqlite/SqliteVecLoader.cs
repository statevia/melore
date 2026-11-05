using Microsoft.Data.Sqlite;

namespace Melore.Infrastructure.Sqlite;

/// <summary>
/// Windows 上で sqlite-vec の LoadExtension を試す。
/// </summary>
public static class SqliteVecLoader
{
    /// <summary>
    /// 成功時は true。失敗しても例外は外へ出さない。
    /// </summary>
    public static bool TryLoad(SqliteConnection connection)
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                connection.LoadExtension(path);
                return true;
            }
            catch (Exception)
            {
                // プレリリース拡張は環境によって読めない。ブルートフォースへ倒す。
            }
        }

        try
        {
            connection.LoadExtension("vec0");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "vec0.dll");
        yield return Path.Combine(baseDir, "runtimes", "win-x64", "native", "vec0.dll");
        yield return Path.Combine(baseDir, "runtimes", "win-arm64", "native", "vec0.dll");
    }
}
