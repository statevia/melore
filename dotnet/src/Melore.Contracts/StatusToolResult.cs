namespace Melore.Contracts;

/// <summary>
/// MCP の <c>status</c> の応答。索引のリビジョンと件数に、作業ツリーとの差を添える。
/// </summary>
/// <remarks>
/// MCP の利用回数とリポジトリの絶対パスは返さない。前者はエージェントの判断に使わず、後者はローカル環境の情報になるため。
/// </remarks>
/// <param name="RepositoryName">対象名。</param>
/// <param name="IndexedRevision">解析済み HEAD SHA。未索引なら null。</param>
/// <param name="DocumentChunkCount">ドキュメントチャンク数。</param>
/// <param name="SymbolCount">シンボル数。</param>
/// <param name="TestCount">テスト数。</param>
/// <param name="VectorSearchEnabled">ベクトル索引が利用可能か。</param>
/// <param name="EmbeddingEnabled">Embedding 生成が利用可能か。</param>
/// <param name="UpdatedAtUtc">最終更新（UTC）。</param>
/// <param name="Dirty">差分が多すぎて再索引していない。</param>
/// <param name="DirtyFileCount">対象になった差分ファイル数。</param>
/// <param name="RefreshedFileCount">今回反映したファイル数。</param>
public sealed record StatusToolResult(
    string RepositoryName,
    string? IndexedRevision,
    int DocumentChunkCount,
    int SymbolCount,
    int TestCount,
    bool VectorSearchEnabled,
    bool EmbeddingEnabled,
    DateTimeOffset? UpdatedAtUtc,
    bool Dirty,
    int DirtyFileCount,
    int RefreshedFileCount)
{
    /// <summary>
    /// 索引の状態と鮮度から作る。
    /// </summary>
    /// <param name="status">索引の件数とリビジョン。</param>
    /// <param name="freshness">直前に見た作業ツリーとの差。</param>
    public static StatusToolResult From(IndexStatus status, IndexFreshness freshness)
        => new(
            status.RepositoryName,
            status.IndexedRevision,
            status.DocumentChunkCount,
            status.SymbolCount,
            status.TestCount,
            status.VectorSearchEnabled,
            status.EmbeddingEnabled,
            status.UpdatedAtUtc,
            freshness.Dirty,
            freshness.DirtyFileCount,
            freshness.RefreshedFileCount);
}
