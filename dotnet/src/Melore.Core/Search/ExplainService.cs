using Melore.Contracts;
using Melore.Core.Ports;
using Microsoft.Extensions.Logging;

namespace Melore.Core.Search;

/// <summary>
/// 統合検索の上位だけを LLM に渡して説明する。
/// </summary>
/// <param name="search">統合検索。</param>
/// <param name="store">本文。</param>
/// <param name="chat">生成。不可なら固定文。</param>
/// <param name="log">プロンプトに載せた ID を残す。</param>
public sealed class ExplainService(
    SearchService search,
    IMeloreStore store,
    IMeloreChatClient chat,
    ILogger<ExplainService> log)
{
    /// <summary>
    /// 質問に対し、検索上位のみを根拠に説明する。
    /// </summary>
    public async Task<ExplainResult> ExplainAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var cap = Math.Clamp(limit, 1, ExplainPromptBuilder.DefaultEvidenceLimit);
        var hits = await search.SearchAsync(query, cap, cancellationToken).ConfigureAwait(false);
        var evidenceHits = hits
            .Select(h => HitFormatter.ToDetail(store, h, includeBody: true))
            .ToList();

        if (evidenceHits.Count == 0)
        {
            log.LogInformation("explain evidence_ids= (none) query_len={Length}", query.Length);
            return new ExplainResult(
                ExplainPromptBuilder.NoEvidenceAnswer,
                [],
                [],
                chat.ModelName,
                ChatUsed: false,
                Limitation: "検索ヒットが無い");
        }

        var chunks = evidenceHits.Select(h => new ExplainEvidence(
            h.Id,
            h.Kind,
            h.Title,
            h.Path,
            h.StartLine,
            h.EndLine,
            h.Body ?? h.Snippet,
            h.Basis)).ToList();
        var (userPrompt, evidenceIds) = ExplainPromptBuilder.BuildUserPrompt(query, chunks);
        log.LogInformation(
            "explain evidence_ids={Ids} chat_available={Available}",
            string.Join(",", evidenceIds),
            chat.IsAvailable);

        if (!chat.IsAvailable)
        {
            return new ExplainResult(
                ExplainPromptBuilder.ChatUnavailableAnswer,
                evidenceHits,
                evidenceIds,
                chat.ModelName,
                ChatUsed: false,
                Limitation: "Ollama chat が利用できない");
        }

        var answer = await chat.CompleteAsync(
                ExplainPromptBuilder.SystemPrompt,
                userPrompt,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return new ExplainResult(
                ExplainPromptBuilder.ChatUnavailableAnswer,
                evidenceHits,
                evidenceIds,
                chat.ModelName,
                ChatUsed: false,
                Limitation: "chat 応答が空");
        }

        return new ExplainResult(
            answer.Trim(),
            evidenceHits,
            evidenceIds,
            chat.ModelName,
            ChatUsed: true,
            Limitation: null);
    }
}
