using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;

namespace Melore.Mcp;

/// <summary>
/// MCP の JSON で日本語を \u エスケープしない。
/// SDK 2.3.0 の既定オプションは読み取り専用なので、エンコーダ欄だけ差し替える。
/// </summary>
public static class McpJson
{
    /// <summary>
    /// プロセスの MCP 既定オプションを緩いエンコーダにする。ツール登録より前に呼ぶ。
    /// </summary>
    public static void UseRelaxedUnicode()
    {
        var field = typeof(JsonSerializerOptions).GetField(
            "_encoder",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(McpJsonUtilities.DefaultOptions, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    }
}
