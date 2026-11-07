namespace Melore.Contracts;

/// <summary>
/// 依存・所属の追跡結果。ノードは名前と行範囲だけ。
/// </summary>
/// <param name="Root">起点。</param>
/// <param name="Nodes">出現したシンボル。</param>
/// <param name="Edges">関係。</param>
/// <param name="Limitation">未抽出や打ち切りがあれば理由。無ければ null。</param>
public sealed record TraceResult(
    TraceNode? Root,
    IReadOnlyList<TraceNode> Nodes,
    IReadOnlyList<TraceEdge> Edges,
    string? Limitation);

/// <summary>
/// 追跡グラフの節。snippet は含めない。
/// </summary>
/// <param name="Id">シンボル ID。</param>
/// <param name="Kind">知識種別。</param>
/// <param name="Name">表示名（シンボルは FQN）。</param>
/// <param name="Path">リポジトリ相対パス。</param>
/// <param name="StartLine">開始行。</param>
/// <param name="EndLine">終了行。</param>
public sealed record TraceNode(
    string Id,
    string Kind,
    string Name,
    string Path,
    int StartLine,
    int EndLine);

/// <summary>
/// 追跡グラフの辺。
/// </summary>
public sealed record TraceEdge(
    string FromId,
    string ToId,
    string RelType,
    RelationshipBasis Basis);
