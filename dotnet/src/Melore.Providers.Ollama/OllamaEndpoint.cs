namespace Melore.Providers.Ollama;

/// <summary>
/// Ollama の接続先を整える。
/// </summary>
internal static class OllamaEndpoint
{
    /// <summary>
    /// 相対パスの解決で末尾のセグメントが落ちないよう、末尾にスラッシュを付けて返す。
    /// </summary>
    public static Uri WithTrailingSlash(Uri endpoint)
    {
        var text = endpoint.ToString();
        return text.EndsWith('/') ? endpoint : new Uri(text + '/');
    }
}
