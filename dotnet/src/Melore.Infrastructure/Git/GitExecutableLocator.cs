namespace Melore.Infrastructure.Git;

/// <summary>
/// PATH から git の絶対パスを解決する。
/// </summary>
/// <remarks>
/// Windows のプロセス起動は、ファイル名だけを渡すと PATH より先にカレントディレクトリを探す。
/// 対象リポジトリの中で起動したときに、そこに置かれた git.exe を実行しないよう、
/// 絶対パスの PATH エントリだけを見て解決する。PATH 自体は利用者の環境として信頼する。
/// <c>.cmd</c> などのシムは対象にしない。
/// </remarks>
public static class GitExecutableLocator
{
    /// <summary>
    /// 現在のプロセスの PATH から解決する。
    /// </summary>
    /// <returns>git の絶対パス。見つからなければ null。</returns>
    public static string? Find()
        => Find(Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows() ? "git.exe" : "git");

    /// <summary>
    /// 指定の PATH の先頭から順に探す。
    /// </summary>
    /// <param name="pathVariable">PATH 環境変数の値。</param>
    /// <param name="fileName">探す実行ファイル名。</param>
    /// <returns>見つかった絶対パス。見つからなければ null。</returns>
    public static string? Find(string? pathVariable, string fileName)
        => (pathVariable ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim('"'))
            .Where(Path.IsPathFullyQualified)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
}
