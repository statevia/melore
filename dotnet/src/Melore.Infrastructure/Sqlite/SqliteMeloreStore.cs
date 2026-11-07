using System.Globalization;
using Melore.Contracts;
using Melore.Core;
using Melore.Core.Models;
using Melore.Core.Ports;
using Microsoft.Data.Sqlite;

namespace Melore.Infrastructure.Sqlite;

/// <summary>
/// SQLite + FTS5 + ベクトル BLOB（必要なら sqlite-vec）の実装。
/// </summary>
public sealed partial class SqliteMeloreStore : IMeloreStore
{
    private readonly string _repositoryName;
    private readonly string _rootPath;
    private readonly SqliteConnection _connection;
    private string? _repositoryId;
    private bool _initialized;

    /// <summary>
    /// DB ファイルを開く。親ディレクトリが無ければ作る。
    /// </summary>
    public SqliteMeloreStore(string databasePath, string repositoryName, string rootPath)
    {
        _repositoryName = repositoryName;
        _rootPath = rootPath;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString());
        _connection.Open();
    }

    /// <inheritdoc />
    public bool VectorSearchEnabled { get; private set; } = true;

    /// <summary>sqlite-vec のロードに成功したか。検索は BLOB 走査でも動く。</summary>
    public bool SqliteVecLoaded { get; private set; }

    /// <inheritdoc />
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();

        using var schema = _connection.CreateCommand();
        schema.CommandText = SchemaSql;
        schema.ExecuteNonQuery();

        MigrateFtsIfNeeded();
        SqliteVecLoaded = SqliteVecLoader.TryLoad(_connection);
        EnsureRepository();
        _initialized = true;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetIndexedBlobShas()
    {
        EnsureRepository();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT path, git_blob_sha
            FROM files
            WHERE repository_id = $repo AND is_deleted = 0;
            """;
        cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.GetString(1);
        }

        return map;
    }

    /// <inheritdoc />
    public string? GetAnalyzerVersions()
    {
        EnsureRepository();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT analyzer_versions FROM repositories WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", RepositoryId);
        return cmd.ExecuteScalar() as string;
    }

    /// <inheritdoc />
    public void DeleteFile(string relativePath)
    {
        EnsureRepository();
        using var tx = _connection.BeginTransaction();
        var fileId = FindFileId(relativePath);
        if (fileId is not null)
        {
            DeleteFileById(fileId);
        }

        tx.Commit();
    }

    /// <inheritdoc />
    public string EnsureFile(RepositoryFile file)
    {
        EnsureRepository();
        var existing = FindFileId(file.RelativePath);
        if (existing is not null)
        {
            using var update = _connection.CreateCommand();
            update.CommandText = """
                UPDATE files
                SET git_blob_sha = $sha, language = $lang, last_write_utc = $ts, is_deleted = 0
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$sha", file.BlobSha);
            update.Parameters.AddWithValue("$lang", file.Language);
            update.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$id", existing);
            update.ExecuteNonQuery();
            return existing;
        }

        var id = Ids.New();
        using var insert = _connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO files (id, repository_id, path, language, git_blob_sha, last_write_utc, is_deleted)
            VALUES ($id, $repo, $path, $lang, $sha, $ts, 0);
            """;
        insert.Parameters.AddWithValue("$id", id);
        insert.Parameters.AddWithValue(RepoParameter, RepositoryId);
        insert.Parameters.AddWithValue("$path", file.RelativePath);
        insert.Parameters.AddWithValue("$lang", file.Language);
        insert.Parameters.AddWithValue("$sha", file.BlobSha);
        insert.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        insert.ExecuteNonQuery();
        return id;
    }

    /// <inheritdoc />
    public void UpsertDocument(string fileId, RepositoryFile file, ParsedDocument document, string? revision)
    {
        using var tx = _connection.BeginTransaction();
        ClearDocumentKnowledge(fileId);

        var documentId = Ids.New();
        using (var insertDoc = _connection.CreateCommand())
        {
            insertDoc.CommandText = """
                INSERT INTO documents (id, file_id, title, doc_kind)
                VALUES ($id, $file, $title, $kind);
                """;
            insertDoc.Parameters.AddWithValue("$id", documentId);
            insertDoc.Parameters.AddWithValue(FileParameter, fileId);
            insertDoc.Parameters.AddWithValue("$title", document.Title);
            insertDoc.Parameters.AddWithValue("$kind", document.DocKind);
            insertDoc.ExecuteNonQuery();
        }

        foreach (var chunk in document.Chunks)
        {
            var chunkId = Ids.New();
            using var insertChunk = _connection.CreateCommand();
            insertChunk.CommandText = """
                INSERT INTO document_chunks
                    (id, document_id, heading_path, spec_id, text, start_line, end_line, ordinal, token_estimate)
                VALUES
                    ($id, $doc, $heading, $spec, $text, $start, $end, $ord, $tokens);
                """;
            insertChunk.Parameters.AddWithValue("$id", chunkId);
            insertChunk.Parameters.AddWithValue("$doc", documentId);
            insertChunk.Parameters.AddWithValue("$heading", chunk.HeadingPath);
            insertChunk.Parameters.AddWithValue("$spec", (object?)chunk.SpecId ?? DBNull.Value);
            insertChunk.Parameters.AddWithValue("$text", chunk.Text);
            insertChunk.Parameters.AddWithValue("$start", chunk.StartLine);
            insertChunk.Parameters.AddWithValue("$end", chunk.EndLine);
            insertChunk.Parameters.AddWithValue("$ord", chunk.Ordinal);
            insertChunk.Parameters.AddWithValue("$tokens", Math.Max(1, chunk.Text.Length / 4));
            insertChunk.ExecuteNonQuery();

            using var fts = _connection.CreateCommand();
            fts.CommandText = """
                INSERT INTO document_fts (id, heading_path, spec_id, text)
                VALUES ($id, $heading, $spec, $text);
                """;
            fts.Parameters.AddWithValue("$id", chunkId);
            fts.Parameters.AddWithValue("$heading", chunk.HeadingPath);
            fts.Parameters.AddWithValue("$spec", chunk.SpecId ?? "");
            fts.Parameters.AddWithValue("$text", chunk.Text);
            fts.ExecuteNonQuery();
        }

        foreach (var link in document.Links)
        {
            using var rel = _connection.CreateCommand();
            rel.CommandText = """
                INSERT INTO relationships
                    (id, from_kind, from_id, to_kind, to_id, rel_type, basis, confidence, evidence)
                VALUES
                    ($id, 'document_chunk', $from, 'file', $to, 'doc_link', 'explicit', 1.0, $ev);
                """;
            rel.Parameters.AddWithValue("$id", Ids.New());
            rel.Parameters.AddWithValue("$from", documentId);
            rel.Parameters.AddWithValue("$to", link.TargetRelativePath);
            rel.Parameters.AddWithValue("$ev", $"{file.RelativePath}:{link.Line}");
            rel.ExecuteNonQuery();
        }

        tx.Commit();
        _ = revision;
    }

    /// <inheritdoc />
    public void UpsertSymbols(string fileId, RepositoryFile file, IReadOnlyList<ParsedSymbol> symbols, string? revision)
    {
        using var tx = _connection.BeginTransaction();
        ClearSymbolKnowledge(fileId);

        var idsByFqn = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            var symbolId = Ids.New();
            idsByFqn[symbol.Fqn] = symbolId;
            using var insert = _connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO symbols
                    (id, file_id, kind, fqn, name, containing_type_fqn, signature, accessibility,
                     start_line, end_line, is_generated, documentation_comment)
                VALUES
                    ($id, $file, $kind, $fqn, $name, $containing, $sig, $access,
                     $start, $end, 0, $doc);
                """;
            insert.Parameters.AddWithValue("$id", symbolId);
            insert.Parameters.AddWithValue(FileParameter, fileId);
            insert.Parameters.AddWithValue("$kind", symbol.Kind);
            insert.Parameters.AddWithValue("$fqn", symbol.Fqn);
            insert.Parameters.AddWithValue(NameParameter, symbol.Name);
            insert.Parameters.AddWithValue("$containing", (object?)symbol.ContainingTypeFqn ?? DBNull.Value);
            insert.Parameters.AddWithValue("$sig", (object?)symbol.Signature ?? DBNull.Value);
            insert.Parameters.AddWithValue("$access", symbol.Accessibility);
            insert.Parameters.AddWithValue("$start", symbol.StartLine);
            insert.Parameters.AddWithValue("$end", symbol.EndLine);
            insert.Parameters.AddWithValue("$doc", (object?)symbol.DocumentationComment ?? DBNull.Value);
            insert.ExecuteNonQuery();

            using var fts = _connection.CreateCommand();
            fts.CommandText = """
                INSERT INTO symbol_fts (id, name, fqn, signature, documentation_comment)
                VALUES ($id, $name, $fqn, $sig, $doc);
                """;
            fts.Parameters.AddWithValue("$id", symbolId);
            fts.Parameters.AddWithValue(NameParameter, symbol.Name);
            fts.Parameters.AddWithValue("$fqn", symbol.Fqn);
            fts.Parameters.AddWithValue("$sig", symbol.Signature ?? "");
            fts.Parameters.AddWithValue("$doc", symbol.DocumentationComment ?? "");
            fts.ExecuteNonQuery();

            if (symbol.IsTest)
            {
                using var test = _connection.CreateCommand();
                test.CommandText = """
                    INSERT INTO tests (id, symbol_id, framework, fact_or_theory, display_name, inferred_target_fqn)
                    VALUES ($id, $symbol, $fw, NULL, $name, NULL);
                    """;
                test.Parameters.AddWithValue("$id", Ids.New());
                test.Parameters.AddWithValue("$symbol", symbolId);
                test.Parameters.AddWithValue("$fw", symbol.TestFramework ?? "unknown");
                test.Parameters.AddWithValue(NameParameter, symbol.Name);
                test.ExecuteNonQuery();
            }
        }

        foreach (var symbol in symbols)
        {
            if (!idsByFqn.TryGetValue(symbol.Fqn, out var fromId))
            {
                continue;
            }

            if (symbol.ContainingTypeFqn is not null &&
                idsByFqn.TryGetValue(symbol.ContainingTypeFqn, out var parentId))
            {
                InsertRelationship(new RelationshipEntry(
                    "symbol",
                    parentId,
                    "symbol",
                    fromId,
                    "declares",
                    RelationshipBasis.Explicit,
                    1.0,
                    symbol.Name));
            }

            foreach (var baseName in symbol.BaseTypeNames)
            {
                InsertRelationship(new RelationshipEntry(
                    "symbol",
                    fromId,
                    "type_name",
                    baseName,
                    "inherits",
                    RelationshipBasis.Inferred,
                    0.6,
                    baseName));
            }
        }

        tx.Commit();
        _ = file;
        _ = revision;
    }

    /// <inheritdoc />
    public void CompleteIndex(string repositoryName, string rootPath, string? revision, string analyzerVersions)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE repositories
            SET name = $name, root_path = $root, indexed_revision = $rev,
                analyzer_versions = $ver, updated_at = $ts
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue(NameParameter, repositoryName);
        cmd.Parameters.AddWithValue("$root", rootPath);
        cmd.Parameters.AddWithValue("$rev", (object?)revision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ver", analyzerVersions);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", RepositoryId);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IndexStatus GetStatus(string repositoryName)
    {
        EnsureRepository();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT r.indexed_revision, r.updated_at,
                   (SELECT COUNT(*) FROM document_chunks c
                    JOIN documents d ON d.id = c.document_id
                    JOIN files f ON f.id = d.file_id
                    WHERE f.repository_id = r.id AND f.is_deleted = 0),
                   (SELECT COUNT(*) FROM symbols s
                    JOIN files f ON f.id = s.file_id
                    WHERE f.repository_id = r.id AND f.is_deleted = 0),
                   (SELECT COUNT(*) FROM tests t
                    JOIN symbols s ON s.id = t.symbol_id
                    JOIN files f ON f.id = s.file_id
                    WHERE f.repository_id = r.id AND f.is_deleted = 0)
            FROM repositories r
            WHERE r.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", RepositoryId);
        string? revision = null;
        DateTimeOffset? updated = null;
        int chunks = 0;
        int symbols = 0;
        int tests = 0;
        var found = false;
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                found = true;
                revision = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (!reader.IsDBNull(1) &&
                    DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    updated = parsed;
                }

                chunks = reader.GetInt32(2);
                symbols = reader.GetInt32(3);
                tests = reader.GetInt32(4);
            }
        }

        if (!found)
        {
            return new IndexStatus(repositoryName, null, 0, 0, 0, VectorSearchEnabled, false, null);
        }

        var (invocations, lastInvocation) = ReadInvocationStats();
        return new IndexStatus(
            repositoryName,
            revision,
            chunks,
            symbols,
            tests,
            VectorSearchEnabled,
            HasAnyEmbedding(),
            updated,
            invocations,
            lastInvocation);
    }

    /// <inheritdoc />
    public string? FindSymbolIdByFqn(string fqn)
    {
        EnsureRepository();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id
            FROM symbols s
            JOIN files f ON f.id = s.file_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0 AND s.fqn = $fqn
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
        cmd.Parameters.AddWithValue("$fqn", fqn);
        return cmd.ExecuteScalar() as string;
    }

    /// <inheritdoc />
    public void RecordMcpInvocation(
        string tool,
        string argumentsJson,
        string responseJson,
        int durationMs,
        bool success,
        string? error)
    {
        Initialize();
        const int maxChars = 256 * 1024;
        var responseChars = responseJson.Length;
        if (argumentsJson.Length > maxChars)
        {
            argumentsJson = argumentsJson[..maxChars];
        }

        if (responseJson.Length > maxChars)
        {
            responseJson = responseJson[..maxChars];
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mcp_invocations
                (id, created_at, tool, arguments_json, response_json, response_chars, duration_ms, success, error)
            VALUES
                ($id, $at, $tool, $args, $response, $chars, $ms, $ok, $error);
            """;
        cmd.Parameters.AddWithValue("$id", Ids.New());
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$tool", tool);
        cmd.Parameters.AddWithValue("$args", argumentsJson);
        cmd.Parameters.AddWithValue("$response", responseJson);
        cmd.Parameters.AddWithValue("$chars", responseChars);
        cmd.Parameters.AddWithValue("$ms", durationMs);
        cmd.Parameters.AddWithValue("$ok", success ? 1 : 0);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private (int Count, DateTimeOffset? Last) ReadInvocationStats()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), MAX(created_at) FROM mcp_invocations;";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return (0, null);
        }

        DateTimeOffset? last = null;
        if (!reader.IsDBNull(1) &&
            DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            last = parsed;
        }

        return (reader.GetInt32(0), last);
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchHit> SearchFullText(string query, int limit)
    {
        var ftsQuery = ToFtsQuery(query);
        if (ftsQuery is null)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT c.id, c.heading_path, c.text, c.start_line, c.end_line, f.path, r.indexed_revision
                FROM document_fts
                JOIN document_chunks c ON c.id = document_fts.id
                JOIN documents d ON d.id = c.document_id
                JOIN files f ON f.id = d.file_id
                JOIN repositories r ON r.id = f.repository_id
                WHERE document_fts MATCH $q AND f.repository_id = $repo AND f.is_deleted = 0
                ORDER BY bm25(document_fts)
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$q", ftsQuery);
            cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                hits.Add(ReadDocumentHit(reader, HitKind.DocumentChunk, 0));
            }
        }

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.id, s.name, COALESCE(s.documentation_comment, s.signature, s.fqn),
                       s.start_line, s.end_line, f.path, r.indexed_revision
                FROM symbol_fts
                JOIN symbols s ON s.id = symbol_fts.id
                JOIN files f ON f.id = s.file_id
                JOIN repositories r ON r.id = f.repository_id
                WHERE symbol_fts MATCH $q AND f.repository_id = $repo AND f.is_deleted = 0
                ORDER BY bm25(symbol_fts)
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$q", ftsQuery);
            cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                hits.Add(ReadDocumentHit(reader, HitKind.Symbol, hits.Count));
            }
        }

        return hits;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchHit> SearchSymbols(string query, int limit)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.fqn, COALESCE(s.documentation_comment, s.signature, s.fqn),
                   s.start_line, s.end_line, f.path, r.indexed_revision
            FROM symbols s
            JOIN files f ON f.id = s.file_id
            JOIN repositories r ON r.id = f.repository_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0
              AND (instr(lower(s.name), lower($q)) > 0 OR instr(lower(s.fqn), lower($q)) > 0)
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
        cmd.Parameters.AddWithValue("$q", query);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        var hits = new List<SearchHit>();
        var rank = 0;
        while (reader.Read())
        {
            hits.Add(ReadDocumentHit(reader, HitKind.Symbol, rank++));
        }

        return hits;
    }

    /// <inheritdoc />
    public IReadOnlyList<EmbeddableChunk> ListChunksWithoutEmbeddings()
    {
        var list = new List<EmbeddableChunk>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT c.id, c.heading_path || char(10) || c.text
                FROM document_chunks c
                JOIN documents d ON d.id = c.document_id
                JOIN files f ON f.id = d.file_id
                WHERE f.repository_id = $repo AND f.is_deleted = 0
                  AND NOT EXISTS (SELECT 1 FROM embeddings e WHERE e.chunk_id = c.id);
                """;
            cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new EmbeddableChunk(reader.GetString(0), HitKind.DocumentChunk, Truncate(reader.GetString(1), 4000)));
            }
        }

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.id, s.name || char(10) || COALESCE(s.documentation_comment, '')
                FROM symbols s
                JOIN files f ON f.id = s.file_id
                WHERE f.repository_id = $repo AND f.is_deleted = 0
                  AND s.kind IN ('class', 'interface', 'record', 'struct', 'method')
                  AND NOT EXISTS (SELECT 1 FROM tests t WHERE t.symbol_id = s.id)
                  AND NOT EXISTS (SELECT 1 FROM embeddings e WHERE e.chunk_id = s.id);
                """;
            cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new EmbeddableChunk(reader.GetString(0), HitKind.Symbol, Truncate(reader.GetString(1), 2000)));
            }
        }

        return list;
    }

    /// <inheritdoc />
    public void SaveEmbedding(string chunkId, HitKind kind, float[] vector, string model, int dim)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO embeddings (id, chunk_id, kind, model, dim, vector)
            VALUES ($id, $chunk, $kind, $model, $dim, $vec)
            ON CONFLICT(chunk_id) DO UPDATE SET model = $model, dim = $dim, vector = $vec;
            """;
        cmd.Parameters.AddWithValue("$id", Ids.New());
        cmd.Parameters.AddWithValue("$chunk", chunkId);
        cmd.Parameters.AddWithValue("$kind", kind.ToString());
        cmd.Parameters.AddWithValue("$model", model);
        cmd.Parameters.AddWithValue("$dim", dim);
        cmd.Parameters.AddWithValue("$vec", FloatsToBytes(vector));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchHit> SearchVectors(float[] query, int limit)
    {
        if (query.Length == 0)
        {
            return [];
        }

        var scored = new List<(string ChunkId, string Kind, double Score)>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT e.chunk_id, e.kind, e.vector
            FROM embeddings e
            JOIN (
                SELECT id FROM document_chunks
                UNION ALL
                SELECT id FROM symbols
            ) k ON k.id = e.chunk_id;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var blob = (byte[])reader[2];
            var vector = BytesToFloats(blob);
            if (vector.Length != query.Length)
            {
                continue;
            }

            scored.Add((reader.GetString(0), reader.GetString(1), Cosine(query, vector)));
        }

        var hits = new List<SearchHit>();
        foreach (var row in scored.OrderByDescending(x => x.Score).Take(limit))
        {
            var hit = LoadHit(row.ChunkId, row.Kind, row.Score);
            if (hit is not null)
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _connection.Dispose();
    }

    private string RepositoryId => _repositoryId ?? throw new InvalidOperationException("Store is not initialized.");

    private void EnsureRepository()
    {
        if (_repositoryId is not null)
        {
            return;
        }

        using var find = _connection.CreateCommand();
        find.CommandText = "SELECT id FROM repositories WHERE name = $name LIMIT 1;";
        find.Parameters.AddWithValue(NameParameter, _repositoryName);
        var existing = find.ExecuteScalar() as string;
        if (existing is not null)
        {
            _repositoryId = existing;
            return;
        }

        _repositoryId = Ids.New();
        using var insert = _connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO repositories (id, name, root_path, indexed_revision, analyzer_versions, updated_at)
            VALUES ($id, $name, $root, NULL, NULL, $ts);
            """;
        insert.Parameters.AddWithValue("$id", _repositoryId);
        insert.Parameters.AddWithValue(NameParameter, _repositoryName);
        insert.Parameters.AddWithValue("$root", _rootPath);
        insert.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        insert.ExecuteNonQuery();
    }

    private string? FindFileId(string relativePath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM files WHERE repository_id = $repo AND path = $path;";
        cmd.Parameters.AddWithValue(RepoParameter, RepositoryId);
        cmd.Parameters.AddWithValue("$path", relativePath);
        return cmd.ExecuteScalar() as string;
    }

    private void DeleteFileById(string fileId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM embeddings WHERE chunk_id IN (
                SELECT c.id FROM document_chunks c
                JOIN documents d ON d.id = c.document_id WHERE d.file_id = $file
                UNION
                SELECT s.id FROM symbols s WHERE s.file_id = $file);
            DELETE FROM relationships WHERE from_id IN (SELECT id FROM symbols WHERE file_id = $file)
                OR from_id IN (SELECT id FROM documents WHERE file_id = $file)
                OR to_id IN (SELECT id FROM symbols WHERE file_id = $file);
            DELETE FROM tests WHERE symbol_id IN (SELECT id FROM symbols WHERE file_id = $file);
            DELETE FROM symbol_fts WHERE id IN (SELECT id FROM symbols WHERE file_id = $file);
            DELETE FROM document_fts WHERE id IN (
                SELECT c.id FROM document_chunks c
                JOIN documents d ON d.id = c.document_id WHERE d.file_id = $file);
            DELETE FROM document_chunks WHERE document_id IN (SELECT id FROM documents WHERE file_id = $file);
            DELETE FROM documents WHERE file_id = $file;
            DELETE FROM symbols WHERE file_id = $file;
            DELETE FROM files WHERE id = $file;
            """;
        cmd.Parameters.AddWithValue(FileParameter, fileId);
        cmd.ExecuteNonQuery();
    }

    private void ClearDocumentKnowledge(string fileId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM embeddings WHERE chunk_id IN (
                SELECT c.id FROM document_chunks c
                JOIN documents d ON d.id = c.document_id WHERE d.file_id = $file);
            DELETE FROM relationships WHERE from_id IN (SELECT id FROM documents WHERE file_id = $file);
            DELETE FROM document_fts WHERE id IN (
                SELECT c.id FROM document_chunks c
                JOIN documents d ON d.id = c.document_id WHERE d.file_id = $file);
            DELETE FROM document_chunks WHERE document_id IN (SELECT id FROM documents WHERE file_id = $file);
            DELETE FROM documents WHERE file_id = $file;
            """;
        cmd.Parameters.AddWithValue(FileParameter, fileId);
        cmd.ExecuteNonQuery();
    }

    private void ClearSymbolKnowledge(string fileId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM embeddings WHERE chunk_id IN (SELECT id FROM symbols WHERE file_id = $file);
            DELETE FROM relationships WHERE from_id IN (SELECT id FROM symbols WHERE file_id = $file)
                OR to_id IN (SELECT id FROM symbols WHERE file_id = $file);
            DELETE FROM tests WHERE symbol_id IN (SELECT id FROM symbols WHERE file_id = $file);
            DELETE FROM symbol_fts WHERE id IN (SELECT id FROM symbols WHERE file_id = $file);
            DELETE FROM symbols WHERE file_id = $file;
            """;
        cmd.Parameters.AddWithValue(FileParameter, fileId);
        cmd.ExecuteNonQuery();
    }

    private bool HasAnyEmbedding()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM embeddings LIMIT 1;";
        return cmd.ExecuteScalar() is not null;
    }

    private static SearchHit ReadDocumentHit(SqliteDataReader reader, HitKind kind, int rank)
    {
        var id = reader.GetString(0);
        var title = reader.GetString(1);
        var text = reader.IsDBNull(2) ? title : reader.GetString(2);
        var start = reader.GetInt32(3);
        var end = reader.GetInt32(4);
        var path = reader.GetString(5);
        var revision = reader.IsDBNull(6) ? null : reader.GetString(6);
        return new SearchHit(
            id,
            kind,
            title,
            Snippet(text),
            1.0 / (rank + 1),
            new SourceSpan(path, start, end, revision));
    }

    private SearchHit? LoadHit(string id, string kind, double score)
    {
        var isSymbol = string.Equals(kind, nameof(HitKind.Symbol), StringComparison.OrdinalIgnoreCase)
                       || string.Equals(kind, "Symbol", StringComparison.Ordinal);
        using var cmd = _connection.CreateCommand();
        if (isSymbol)
        {
            cmd.CommandText = """
                SELECT s.id, s.fqn, COALESCE(s.documentation_comment, s.signature, s.fqn),
                       s.start_line, s.end_line, f.path, r.indexed_revision
                FROM symbols s
                JOIN files f ON f.id = s.file_id
                JOIN repositories r ON r.id = f.repository_id
                WHERE s.id = $id;
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT c.id, c.heading_path, c.text, c.start_line, c.end_line, f.path, r.indexed_revision
                FROM document_chunks c
                JOIN documents d ON d.id = c.document_id
                JOIN files f ON f.id = d.file_id
                JOIN repositories r ON r.id = f.repository_id
                WHERE c.id = $id;
                """;
        }

        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var hit = ReadDocumentHit(reader, isSymbol ? HitKind.Symbol : HitKind.DocumentChunk, 0);
        return hit with { Score = score };
    }

    /// <summary>
    /// FTS5 の MATCH 式。`$` は独立トークン、`.` は区切りのままにする。
    /// </summary>
    internal static string? ToFtsQuery(string query)
    {
        var tokens = Melore.Core.Search.QueryNormalizer.Tokens(query)
            .Select(t => "\"" + t.Replace("\"", "", StringComparison.Ordinal) + "\"")
            .ToList();
        return tokens.Count == 0 ? null : string.Join(" AND ", tokens);
    }

    private const int CurrentSchemaVersion = 2;
    private const string RepoParameter = "$repo";
    private const string FileParameter = "$file";
    private const string NameParameter = "$name";

    private void MigrateFtsIfNeeded()
    {
        using var read = _connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(read.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version >= CurrentSchemaVersion)
        {
            return;
        }

        using var recreate = _connection.CreateCommand();
        recreate.CommandText = """
            DROP TABLE IF EXISTS document_fts;
            DROP TABLE IF EXISTS symbol_fts;
            """;
        recreate.ExecuteNonQuery();

        using var create = _connection.CreateCommand();
        create.CommandText = FtsCreateSql;
        create.ExecuteNonQuery();

        using var fill = _connection.CreateCommand();
        fill.CommandText = """
            INSERT INTO document_fts (id, heading_path, spec_id, text)
            SELECT id, heading_path, COALESCE(spec_id, ''), text FROM document_chunks;
            INSERT INTO symbol_fts (id, name, fqn, signature, documentation_comment)
            SELECT id, name, fqn, COALESCE(signature, ''), COALESCE(documentation_comment, '') FROM symbols;
            """;
        fill.ExecuteNonQuery();

        using var set = _connection.CreateCommand();
        set.CommandText = "PRAGMA user_version = " + CurrentSchemaVersion + ";";
        set.ExecuteNonQuery();
    }

    private static string Snippet(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 280 ? flat : flat[..280] + "…";
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max];

    private static byte[] FloatsToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloats(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        double na = 0;
        double nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom <= 0 ? 0 : dot / denom;
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS repositories (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            root_path TEXT NOT NULL,
            indexed_revision TEXT,
            analyzer_versions TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS files (
            id TEXT PRIMARY KEY,
            repository_id TEXT NOT NULL,
            path TEXT NOT NULL,
            language TEXT NOT NULL,
            git_blob_sha TEXT NOT NULL,
            last_write_utc TEXT,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            UNIQUE (repository_id, path),
            FOREIGN KEY (repository_id) REFERENCES repositories(id)
        );
        CREATE TABLE IF NOT EXISTS documents (
            id TEXT PRIMARY KEY,
            file_id TEXT NOT NULL,
            title TEXT NOT NULL,
            doc_kind TEXT NOT NULL,
            FOREIGN KEY (file_id) REFERENCES files(id)
        );
        CREATE TABLE IF NOT EXISTS document_chunks (
            id TEXT PRIMARY KEY,
            document_id TEXT NOT NULL,
            heading_path TEXT NOT NULL,
            spec_id TEXT,
            text TEXT NOT NULL,
            start_line INTEGER NOT NULL,
            end_line INTEGER NOT NULL,
            ordinal INTEGER NOT NULL,
            token_estimate INTEGER NOT NULL,
            FOREIGN KEY (document_id) REFERENCES documents(id)
        );
        CREATE TABLE IF NOT EXISTS symbols (
            id TEXT PRIMARY KEY,
            file_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            fqn TEXT NOT NULL,
            name TEXT NOT NULL,
            containing_type_fqn TEXT,
            signature TEXT,
            accessibility TEXT NOT NULL,
            start_line INTEGER NOT NULL,
            end_line INTEGER NOT NULL,
            is_generated INTEGER NOT NULL DEFAULT 0,
            documentation_comment TEXT,
            FOREIGN KEY (file_id) REFERENCES files(id)
        );
        CREATE TABLE IF NOT EXISTS tests (
            id TEXT PRIMARY KEY,
            symbol_id TEXT NOT NULL,
            framework TEXT NOT NULL,
            fact_or_theory TEXT,
            display_name TEXT,
            inferred_target_fqn TEXT,
            FOREIGN KEY (symbol_id) REFERENCES symbols(id)
        );
        CREATE TABLE IF NOT EXISTS relationships (
            id TEXT PRIMARY KEY,
            from_kind TEXT NOT NULL,
            from_id TEXT NOT NULL,
            to_kind TEXT NOT NULL,
            to_id TEXT NOT NULL,
            rel_type TEXT NOT NULL,
            basis TEXT NOT NULL,
            confidence REAL NOT NULL,
            evidence TEXT
        );
        CREATE TABLE IF NOT EXISTS embeddings (
            id TEXT PRIMARY KEY,
            chunk_id TEXT NOT NULL UNIQUE,
            kind TEXT NOT NULL,
            model TEXT NOT NULL,
            dim INTEGER NOT NULL,
            vector BLOB NOT NULL
        );
        CREATE TABLE IF NOT EXISTS mcp_invocations (
            id TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            tool TEXT NOT NULL,
            arguments_json TEXT NOT NULL,
            response_json TEXT NOT NULL,
            response_chars INTEGER NOT NULL,
            duration_ms INTEGER NOT NULL,
            success INTEGER NOT NULL,
            error TEXT
        );
        """;

    private const string FtsCreateSql = """
        CREATE VIRTUAL TABLE IF NOT EXISTS document_fts USING fts5(
            id UNINDEXED,
            heading_path,
            spec_id,
            text,
            tokenize = "unicode61 tokenchars '$'"
        );
        CREATE VIRTUAL TABLE IF NOT EXISTS symbol_fts USING fts5(
            id UNINDEXED,
            name,
            fqn,
            signature,
            documentation_comment,
            tokenize = "unicode61 tokenchars '$'"
        );
        """;
}
