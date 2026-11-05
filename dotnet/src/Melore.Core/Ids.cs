namespace Melore.Core;

/// <summary>
/// 時刻順に近い主キーを生成する。
/// </summary>
public static class Ids
{
    /// <summary>
    /// UUID v7 をハイフンなし 32 文字で返す。
    /// </summary>
    public static string New() => Guid.CreateVersion7().ToString("N");
}
