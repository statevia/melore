using System.Text;

namespace Melore.Core.Search;

/// <summary>
/// explain 用プロンプト。検索上位チャンク以外は入れない。
/// </summary>
public static class ExplainPromptBuilder
{
    /// <summary>プロンプトに載せる最大件数。</summary>
    public const int DefaultEvidenceLimit = 8;

    /// <summary>1 チャンクの最大文字数。</summary>
    public const int MaxCharsPerChunk = 1200;

    /// <summary>根拠テキストの合計上限。</summary>
    public const int MaxEvidenceChars = 8000;

    /// <summary>システム指示。根拠外の補完を禁ずる。</summary>
    public const string SystemPrompt =
        """
        あなたは Melore です。次の根拠チャンクだけを使って日本語で短く答えてください。
        - 根拠に無い型名・パス・API・仕様を断定しない。
        - 根拠が足りないときは「インデックスに無い」と述べ、存在しないパスを作らない。
        - 仕様と実装が食い違うときは矛盾を明示する。
        - 関係の basis が inferred なら推定と書く。explicit なら確定参照と書く。
        - 各主張の末尾に出典を付ける。形式: [id] path:startLine-endLine
        - 前置きや思考過程は出さない。
        """;

    /// <summary>
    /// ユーザー側プロンプトを組み立てる。返る ID が実際に載せた根拠。
    /// </summary>
    public static (string UserPrompt, IReadOnlyList<string> EvidenceIds) BuildUserPrompt(
        string query,
        IReadOnlyList<ExplainEvidence> evidence)
    {
        var builder = new StringBuilder();
        builder.Append("質問:\n");
        builder.Append(query.Trim());
        builder.Append("\n\n根拠:\n");

        if (evidence.Count == 0)
        {
            builder.Append("(なし)\n");
            return (builder.ToString(), []);
        }

        var ids = new List<string>();
        var used = 0;
        foreach (var item in evidence)
        {
            var body = Truncate(item.Body, MaxCharsPerChunk);
            if (used + body.Length > MaxEvidenceChars && ids.Count > 0)
            {
                break;
            }

            builder.Append("### [");
            builder.Append(item.Id);
            builder.Append("] ");
            builder.Append(item.Kind);
            builder.Append(' ');
            builder.Append(item.Path);
            builder.Append(':');
            builder.Append(item.StartLine);
            builder.Append('-');
            builder.Append(item.EndLine);
            if (!string.IsNullOrEmpty(item.Basis))
            {
                builder.Append(" basis=");
                builder.Append(item.Basis);
            }

            builder.Append('\n');
            builder.Append(item.Title);
            builder.Append('\n');
            builder.Append(body);
            builder.Append("\n\n");
            ids.Add(item.Id);
            used += body.Length;
        }

        return (builder.ToString(), ids);
    }

    /// <summary>根拠が空のときの固定回答。LLM は呼ばない。</summary>
    public const string NoEvidenceAnswer =
        "インデックスに該当する根拠がありません。存在しないパスや型名は断定しません。";

    /// <summary>Chat が使えないときの固定回答。</summary>
    public const string ChatUnavailableAnswer =
        "チャットモデルが利用できないため説明文は生成していません。根拠ヒットのみ返します。";

    private static string Truncate(string text, int max)
    {
        var flat = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}

/// <summary>
/// プロンプトに載せる 1 チャンク。
/// </summary>
/// <param name="Id">チャンクまたはシンボル ID。</param>
/// <param name="Kind">知識種別。</param>
/// <param name="Title">見出し。</param>
/// <param name="Path">相対パス。</param>
/// <param name="StartLine">開始行。</param>
/// <param name="EndLine">終了行。</param>
/// <param name="Body">本文または抜粋。</param>
/// <param name="Basis">関係の根拠。無いときは null。</param>
public sealed record ExplainEvidence(
    string Id,
    string Kind,
    string Title,
    string Path,
    int StartLine,
    int EndLine,
    string Body,
    string? Basis);
