using Melore.Contracts;
using Melore.Core.Models;

namespace Melore.Core.Ports;

/// <summary>
/// SQLite 上のメタデータ・FTS・ベクトル。
/// </summary>
public interface IMeloreStore : IDisposable
{
    /// <summary>スキーマを用意する。</summary>
    void Initialize();

    /// <summary>ベクトル索引（sqlite-vec またはブルートフォース）が使えるか。</summary>
    bool VectorSearchEnabled { get; }

    /// <summary>前回インデックスした blob SHA。キーは相対パス。</summary>
    IReadOnlyDictionary<string, string> GetIndexedBlobShas();

    /// <summary>前回の analyzer_versions。無ければ null。</summary>
    string? GetAnalyzerVersions();

    /// <summary>ファイルとその子孫知識を削除する。</summary>
    void DeleteFile(string relativePath);

    /// <summary>ドキュメントを upsert する。</summary>
    void UpsertDocument(string fileId, RepositoryFile file, ParsedDocument document, string? revision);

    /// <summary>C# ファイルのシンボルを upsert する。</summary>
    void UpsertSymbols(string fileId, RepositoryFile file, IReadOnlyList<ParsedSymbol> symbols, string? revision);

    /// <summary>ファイル行を確保して ID を返す。区分も <see cref="RepositoryFile.Lifecycle"/> で書く。</summary>
    string EnsureFile(RepositoryFile file);

    /// <summary>
    /// 既存のファイル行の区分を付け直す。値が変わる行だけを書く。
    /// </summary>
    /// <param name="lifecycleByPath">リポジトリ相対パスと区分。索引に無いパスは無視する。</param>
    /// <returns>区分が変わった行数。</returns>
    /// <remarks>1 つのトランザクションで書く。途中で失敗したら何も変わらない。</remarks>
    int UpdateLifecycles(IReadOnlyDictionary<string, DocumentLifecycle> lifecycleByPath);

    /// <summary>リポジトリ行と世代を更新する。</summary>
    void CompleteIndex(string repositoryName, string rootPath, string? revision, string analyzerVersions);

    /// <summary>ステータス。</summary>
    IndexStatus GetStatus(string repositoryName);

    /// <summary>全文検索。</summary>
    /// <param name="query">利用者のクエリ。FTS 式への変換は実装が行う。</param>
    /// <param name="limit">返す最大件数。</param>
    /// <param name="lifecycles">候補に入れる区分。融合の前に絞るため、ここで渡す。1 つ以上必要。</param>
    IReadOnlyList<SearchHit> SearchFullText(string query, int limit, IReadOnlySet<DocumentLifecycle> lifecycles);

    /// <summary>シンボル名検索。</summary>
    /// <param name="query">名前または FQN の一部。</param>
    /// <param name="limit">返す最大件数。</param>
    /// <param name="lifecycles">候補に入れる区分。1 つ以上必要。</param>
    IReadOnlyList<SearchHit> SearchSymbols(string query, int limit, IReadOnlySet<DocumentLifecycle> lifecycles);

    /// <summary>チャンク本文を Embedding 用に列挙する。</summary>
    IReadOnlyList<EmbeddableChunk> ListChunksWithoutEmbeddings();

    /// <summary>ベクトルを保存する。</summary>
    void SaveEmbedding(string chunkId, HitKind kind, float[] vector, string model, int dim);

    /// <summary>ベクトル近傍。無効時は空。</summary>
    /// <param name="query">クエリの埋め込み。</param>
    /// <param name="limit">返す最大件数。</param>
    /// <param name="lifecycles">候補に入れる区分。埋め込みを読み出す段階で絞る。1 つ以上必要。</param>
    IReadOnlyList<SearchHit> SearchVectors(float[] query, int limit, IReadOnlySet<DocumentLifecycle> lifecycles);

    /// <summary>ID でヒットを取る。無ければ null。</summary>
    SearchHit? GetHitById(string id);

    /// <summary>完全修飾名または ID または単純名でシンボルを探す。</summary>
    IReadOnlyList<SearchHit> FindSymbols(string idOrFqnOrName, int limit);

    /// <summary>チャンクまたはシンボルの本文。無ければ null。</summary>
    string? GetBody(string id);

    /// <summary>型に属するメンバー。</summary>
    IReadOnlyList<RelatedHit> GetDeclaredMembers(string typeFqn);

    /// <summary>同一ファイルのテストメソッド。</summary>
    IReadOnlyList<RelatedHit> GetTestsInSameFile(string path);

    /// <summary>同一ドキュメント内の他チャンク。</summary>
    IReadOnlyList<RelatedHit> GetChunksInSameDocument(string chunkId);

    /// <summary>保存済み relationships を展開する。</summary>
    IReadOnlyList<RelatedHit> GetStoredRelationships(string fromId);

    /// <summary>全 embeddings を消す（analyzer / モデル版の変更）。</summary>
    void ClearEmbeddings();

    /// <summary>同一 blob のリネーム。成功したら true。</summary>
    bool RelocateFile(string fromRelativePath, string toRelativePath, string blobSha);

    /// <summary>指定 rel_type の関係を消す。</summary>
    void DeleteRelationshipsByType(string relType);

    /// <summary>関係を 1 件追加する。</summary>
    void AddRelationship(RelationshipEntry relationship);

    /// <summary>型シンボル（class / interface / record / struct）。</summary>
    IReadOnlyList<IndexedTypeSymbol> ListTypeSymbols();

    /// <summary>全ドキュメントチャンク。</summary>
    IReadOnlyList<IndexedChunk> ListChunks();

    /// <summary>テストメソッド。</summary>
    IReadOnlyList<IndexedTestMethod> ListTestMethods();

    /// <summary>関係件数。basis は explicit / inferred。null なら種別全体。</summary>
    int CountRelationships(string relType, string? basis);

    /// <summary>FQN 一致のシンボル ID。無ければ null。</summary>
    string? FindSymbolIdByFqn(string fqn);

    /// <summary>MCP ツール呼び出しを 1 件残す。応答が長ければ切る。</summary>
    void RecordMcpInvocation(
        string tool,
        string argumentsJson,
        string responseJson,
        int durationMs,
        bool success,
        string? error);
}

/// <summary>
/// Embedding 未計算の本文。
/// </summary>
public sealed record EmbeddableChunk(string Id, HitKind Kind, string Text);
