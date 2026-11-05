namespace Melore.Contracts;

/// <summary>
/// インデックスの鮮度と件数。
/// </summary>
/// <param name="RepositoryName">対象名。</param>
/// <param name="IndexedRevision">解析済み HEAD SHA。</param>
/// <param name="DocumentChunkCount">ドキュメントチャンク数。</param>
/// <param name="SymbolCount">シンボル数。</param>
/// <param name="TestCount">テスト数。</param>
/// <param name="VectorSearchEnabled">ベクトル索引が利用可能か。</param>
/// <param name="EmbeddingEnabled">Embedding 生成が利用可能か。</param>
/// <param name="UpdatedAtUtc">最終更新（UTC）。</param>
public sealed record IndexStatus(
    string RepositoryName,
    string? IndexedRevision,
    int DocumentChunkCount,
    int SymbolCount,
    int TestCount,
    bool VectorSearchEnabled,
    bool EmbeddingEnabled,
    DateTimeOffset? UpdatedAtUtc);
