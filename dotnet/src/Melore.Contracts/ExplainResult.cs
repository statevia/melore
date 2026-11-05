namespace Melore.Contracts;

/// <summary>
/// 検索根拠だけを使った説明。
/// </summary>
/// <param name="Answer">説明文。根拠が無いときはその旨。</param>
/// <param name="Evidence">プロンプトに入れたヒット。</param>
/// <param name="EvidenceIds">デバッグ用。プロンプトに渡した ID。</param>
/// <param name="ChatModel">使った chat モデル。未使用なら空。</param>
/// <param name="ChatUsed">LLM を呼んだか。</param>
/// <param name="Limitation">不足・未使用の理由。</param>
public sealed record ExplainResult(
    string Answer,
    IReadOnlyList<HitDetail> Evidence,
    IReadOnlyList<string> EvidenceIds,
    string ChatModel,
    bool ChatUsed,
    string? Limitation);
