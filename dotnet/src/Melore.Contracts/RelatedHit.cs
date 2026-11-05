namespace Melore.Contracts;

/// <summary>
/// 関連検索の 1 件。関係種別と根拠を持つ。
/// </summary>
/// <param name="Hit">対象ヒット。</param>
/// <param name="RelType">関係（declares / inherits / tested_by / doc_link 等）。</param>
/// <param name="Basis">確定か推定か。</param>
public sealed record RelatedHit(
    SearchHit Hit,
    string RelType,
    RelationshipBasis Basis);
