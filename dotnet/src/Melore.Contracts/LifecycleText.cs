namespace Melore.Contracts;

/// <summary>
/// 区分とスコープの文字列表現（小文字）との相互変換。
/// </summary>
/// <remarks>
/// DB の <c>files.lifecycle</c>、CLI の <c>--scope</c>、MCP の <c>scope</c> と <c>lifecycle</c> で同じ表記を使う。
/// 入力の解釈は大文字小文字を区別せず、許可値以外は受け付けない。
/// </remarks>
public static class LifecycleText
{
    /// <summary>スコープの許可値。エラー表示に使う。</summary>
    public const string AllowedScopes = "current, history, all";

    /// <summary>区分を小文字の文字列にする。</summary>
    public static string ToText(DocumentLifecycle lifecycle) => lifecycle switch
    {
        DocumentLifecycle.Current => "current",
        DocumentLifecycle.Archived => "archived",
        DocumentLifecycle.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null)
    };

    /// <summary>
    /// DB に保存した文字列を区分に戻す。
    /// </summary>
    /// <exception cref="InvalidOperationException">保存値が想定外のとき。DB の破損を黙って現行扱いにしない。</exception>
    public static DocumentLifecycle ParseLifecycle(string text) => text switch
    {
        "current" => DocumentLifecycle.Current,
        "archived" => DocumentLifecycle.Archived,
        "rejected" => DocumentLifecycle.Rejected,
        _ => throw new InvalidOperationException("索引 DB の区分が不正です。index をやり直してください。")
    };

    /// <summary>
    /// 利用者が渡したスコープを解釈する。null と空文字は既定の <see cref="SearchScope.Current"/>。
    /// </summary>
    /// <returns>解釈できたら true。</returns>
    public static bool TryParseScope(string? text, out SearchScope scope)
    {
        scope = SearchScope.Current;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "current":
                scope = SearchScope.Current;
                return true;
            case "history":
                scope = SearchScope.History;
                return true;
            case "all":
                scope = SearchScope.All;
                return true;
            default:
                return false;
        }
    }
}
