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
/// <param name="Dirty">差分が多すぎて再索引していない。</param>
/// <param name="DirtyFileCount">対象になった差分ファイル数。</param>
/// <param name="RefreshedFileCount">今回反映したファイル数。</param>
public sealed record ExplainResult(
    string Answer,
    IReadOnlyList<HitDetail> Evidence,
    IReadOnlyList<string> EvidenceIds,
    string ChatModel,
    bool ChatUsed,
    string? Limitation,
    bool Dirty = false,
    int DirtyFileCount = 0,
    int RefreshedFileCount = 0);
