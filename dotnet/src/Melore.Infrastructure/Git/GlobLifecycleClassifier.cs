using Melore.Contracts;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Melore.Infrastructure.Git;

/// <summary>
/// リポジトリ相対パスから区分を決める。
/// </summary>
/// <remarks>
/// 照合規則は <see cref="GitWorkspace"/> の include / exclude と同じ（大文字小文字を区別しない、<c>/</c> 区切り）。
/// ファイルの中身は読まない。archived と rejected の両方に当たるパスは、判定順に頼らず例外にする。
/// </remarks>
/// <param name="history">履歴文書の glob。</param>
public sealed class GlobLifecycleClassifier(HistorySettings history)
{
    private readonly Matcher? _archived = CreateMatcher(history.Archived);
    private readonly Matcher? _rejected = CreateMatcher(history.Rejected);

    /// <summary>
    /// パスの区分を返す。
    /// </summary>
    /// <param name="relativePath">リポジトリ相対パス。<c>\</c> 区切りでもよい。</param>
    /// <returns>どの glob にも当たらなければ <see cref="DocumentLifecycle.Current"/>。</returns>
    /// <exception cref="InvalidOperationException">archived と rejected の両方に当たるとき。</exception>
    public DocumentLifecycle Classify(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var archived = _archived?.Match(normalized).HasMatches ?? false;
        var rejected = _rejected?.Match(normalized).HasMatches ?? false;
        return (archived, rejected) switch
        {
            (true, true) => throw new InvalidOperationException(
                "history.archived と history.rejected の両方に当たるパスがあります: " + normalized),
            (true, false) => DocumentLifecycle.Archived,
            (false, true) => DocumentLifecycle.Rejected,
            (false, false) => DocumentLifecycle.Current
        };
    }

    private static Matcher? CreateMatcher(string[] patterns)
    {
        if (patterns.Length == 0)
        {
            return null;
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(patterns.Select(pattern => pattern.Replace('\\', '/')));
        return matcher;
    }
}
