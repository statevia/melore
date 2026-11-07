using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;

namespace Melore.Mcp;

/// <summary>
/// MCP ツール結果の JSON 設定。
/// </summary>
public static class McpJson
{
    /// <summary>
    /// ツール結果の本文で日本語を \u エスケープしない設定。SDK の既定に、緩いエンコーダだけを足す。
    /// </summary>
    /// <remarks>
    /// エージェントが読むのはツール結果の本文で、ここのエスケープはトークン量に効く。
    /// プロトコルの封筒は SDK の既定のままなので、stdout 上の表現は \u になることがある。
    /// 受信側が JSON を解くと本文の文字列に戻る。
    /// </remarks>
    public static JsonSerializerOptions ToolOptions { get; } = new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
