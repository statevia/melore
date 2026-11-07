using Melore.Contracts;

namespace Melore.Core.Models;

/// <summary>
/// 保存する関係 1 件。
/// </summary>
/// <param name="FromKind">起点の種別。<c>symbol</c> や <c>document_chunk</c>。</param>
/// <param name="FromId">起点の ID。</param>
/// <param name="ToKind">終点の種別。<c>symbol</c> や <c>type_name</c>。</param>
/// <param name="ToId">終点の ID。<c>type_name</c> のときは型名。</param>
/// <param name="RelType">関係の種類。</param>
/// <param name="Basis">根拠の確からしさ。</param>
/// <param name="Confidence">0 から 1 の確度。</param>
/// <param name="Evidence">根拠の短い説明。</param>
public sealed record RelationshipEntry(
    string FromKind,
    string FromId,
    string ToKind,
    string ToId,
    string RelType,
    RelationshipBasis Basis,
    double Confidence,
    string Evidence);
