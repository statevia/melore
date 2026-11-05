namespace Melore.Contracts;

/// <summary>
/// 依存・所属の追跡結果。
/// </summary>
/// <param name="Root">起点。</param>
/// <param name="Nodes">出現したシンボル。</param>
/// <param name="Edges">関係。</param>
/// <param name="Limitation">未抽出の関係があれば理由。無ければ null。</param>
public sealed record TraceResult(
    SearchHit? Root,
    IReadOnlyList<SearchHit> Nodes,
    IReadOnlyList<TraceEdge> Edges,
    string? Limitation);

/// <summary>
/// 追跡グラフの辺。
/// </summary>
public sealed record TraceEdge(
    string FromId,
    string ToId,
    string RelType,
    RelationshipBasis Basis);
