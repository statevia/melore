using Melore.Contracts;
using Melore.Core;
using Melore.Core.Models;

namespace Melore.Infrastructure.Sqlite;

public sealed partial class SqliteMeloreStore
{
    /// <inheritdoc />
    public SearchHit? GetHitById(string id)
    {
        return LoadHit(id, nameof(HitKind.DocumentChunk), 0)
               ?? LoadHit(id, nameof(HitKind.Symbol), 0);
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchHit> FindSymbols(string idOrFqnOrName, int limit)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.fqn, COALESCE(s.documentation_comment, s.signature, s.fqn),
                   s.start_line, s.end_line, f.path, r.indexed_revision
            FROM symbols s
            JOIN files f ON f.id = s.file_id
            JOIN repositories r ON r.id = f.repository_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0
              AND (s.id = $q OR s.fqn = $q OR s.name = $q)
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        cmd.Parameters.AddWithValue("$q", idOrFqnOrName);
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
    public string? GetBody(string id)
    {
        using var chunk = _connection.CreateCommand();
        chunk.CommandText = "SELECT text FROM document_chunks WHERE id = $id;";
        chunk.Parameters.AddWithValue("$id", id);
        var text = chunk.ExecuteScalar() as string;
        if (text is not null)
        {
            return text;
        }

        using var symbol = _connection.CreateCommand();
        symbol.CommandText = """
            SELECT COALESCE(documentation_comment, signature, fqn)
            FROM symbols WHERE id = $id;
            """;
        symbol.Parameters.AddWithValue("$id", id);
        return symbol.ExecuteScalar() as string;
    }

    /// <inheritdoc />
    public IReadOnlyList<RelatedHit> GetDeclaredMembers(string typeFqn)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.fqn, COALESCE(s.documentation_comment, s.signature, s.fqn),
                   s.start_line, s.end_line, f.path, r.indexed_revision
            FROM symbols s
            JOIN files f ON f.id = s.file_id
            JOIN repositories r ON r.id = f.repository_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0
              AND s.containing_type_fqn = $fqn
            LIMIT 64;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        cmd.Parameters.AddWithValue("$fqn", typeFqn);
        return ReadRelated(cmd, HitKind.Symbol, "declares", RelationshipBasis.Explicit);
    }

    /// <inheritdoc />
    public IReadOnlyList<RelatedHit> GetTestsInSameFile(string path)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.fqn, COALESCE(s.documentation_comment, s.signature, s.fqn),
                   s.start_line, s.end_line, f.path, r.indexed_revision
            FROM tests t
            JOIN symbols s ON s.id = t.symbol_id
            JOIN files f ON f.id = s.file_id
            JOIN repositories r ON r.id = f.repository_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0 AND f.path = $path
            LIMIT 64;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        cmd.Parameters.AddWithValue("$path", path);
        return ReadRelated(cmd, HitKind.Test, "tested_by", RelationshipBasis.Inferred);
    }

    /// <inheritdoc />
    public IReadOnlyList<RelatedHit> GetChunksInSameDocument(string chunkId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.heading_path, c.text, c.start_line, c.end_line, f.path, r.indexed_revision
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id
            JOIN files f ON f.id = d.file_id
            JOIN repositories r ON r.id = f.repository_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0
              AND c.document_id = (SELECT document_id FROM document_chunks WHERE id = $id)
              AND c.id <> $id
            LIMIT 32;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        cmd.Parameters.AddWithValue("$id", chunkId);
        return ReadRelated(cmd, HitKind.DocumentChunk, "same_document", RelationshipBasis.Explicit);
    }

    /// <inheritdoc />
    public IReadOnlyList<RelatedHit> GetStoredRelationships(string fromId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT rel_type, basis, from_id, to_id, from_kind, to_kind
            FROM relationships
            WHERE from_id = $id OR to_id = $id
            LIMIT 64;
            """;
        cmd.Parameters.AddWithValue("$id", fromId);
        using var reader = cmd.ExecuteReader();
        var results = new List<RelatedHit>();
        while (reader.Read())
        {
            var relType = reader.GetString(0);
            var basis = Enum.TryParse<RelationshipBasis>(reader.GetString(1), ignoreCase: true, out var parsed)
                ? parsed
                : RelationshipBasis.Inferred;
            var from = reader.GetString(2);
            var to = reader.GetString(3);
            var toKind = reader.GetString(5);
            var otherId = string.Equals(from, fromId, StringComparison.Ordinal) ? to : from;
            if (string.Equals(toKind, "type_name", StringComparison.Ordinal) &&
                string.Equals(from, fromId, StringComparison.Ordinal))
            {
                foreach (var named in FindSymbols(otherId, 3))
                {
                    results.Add(new RelatedHit(named, relType, basis));
                }

                continue;
            }

            if (string.Equals(toKind, "file", StringComparison.Ordinal))
            {
                foreach (var chunk in ChunksInFile(otherId, 3))
                {
                    results.Add(new RelatedHit(chunk, relType, basis));
                }

                continue;
            }

            var hit = GetHitById(otherId);
            if (hit is not null)
            {
                results.Add(new RelatedHit(hit, relType, basis));
            }
        }

        return results;
    }

    private IReadOnlyList<SearchHit> ChunksInFile(string relativePath, int limit)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.heading_path, c.text, c.start_line, c.end_line, f.path, r.indexed_revision
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id
            JOIN files f ON f.id = d.file_id
            JOIN repositories r ON r.id = f.repository_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0 AND f.path = $path
            ORDER BY c.ordinal
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        cmd.Parameters.AddWithValue("$path", relativePath);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        var hits = new List<SearchHit>();
        var rank = 0;
        while (reader.Read())
        {
            hits.Add(ReadDocumentHit(reader, HitKind.DocumentChunk, rank++));
        }

        return hits;
    }

    private List<RelatedHit> ReadRelated(
        Microsoft.Data.Sqlite.SqliteCommand cmd,
        HitKind kind,
        string relType,
        RelationshipBasis basis)
    {
        using var reader = cmd.ExecuteReader();
        var hits = new List<RelatedHit>();
        var rank = 0;
        while (reader.Read())
        {
            hits.Add(new RelatedHit(ReadDocumentHit(reader, kind, rank++), relType, basis));
        }

        return hits;
    }

    private void InsertRelationship(
        string fromId,
        string fromKind,
        string toId,
        string toKind,
        string relType,
        string basis,
        double confidence,
        string evidence)
    {
        using var rel = _connection.CreateCommand();
        rel.CommandText = """
            INSERT INTO relationships
                (id, from_kind, from_id, to_kind, to_id, rel_type, basis, confidence, evidence)
            VALUES
                ($id, $fk, $from, $tk, $to, $type, $basis, $conf, $ev);
            """;
        rel.Parameters.AddWithValue("$id", Ids.New());
        rel.Parameters.AddWithValue("$fk", fromKind);
        rel.Parameters.AddWithValue("$from", fromId);
        rel.Parameters.AddWithValue("$tk", toKind);
        rel.Parameters.AddWithValue("$to", toId);
        rel.Parameters.AddWithValue("$type", relType);
        rel.Parameters.AddWithValue("$basis", basis);
        rel.Parameters.AddWithValue("$conf", confidence);
        rel.Parameters.AddWithValue("$ev", evidence);
        rel.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void ClearEmbeddings()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM embeddings;";
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public bool RelocateFile(string fromRelativePath, string toRelativePath, string blobSha)
    {
        if (FindFileId(toRelativePath) is not null)
        {
            return false;
        }

        var fromId = FindFileId(fromRelativePath);
        if (fromId is null)
        {
            return false;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE files
            SET path = $to, git_blob_sha = $sha, last_write_utc = $ts, is_deleted = 0
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$to", toRelativePath);
        cmd.Parameters.AddWithValue("$sha", blobSha);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", fromId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <inheritdoc />
    public void DeleteRelationshipsByType(string relType)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM relationships WHERE rel_type = $type;";
        cmd.Parameters.AddWithValue("$type", relType);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void AddRelationship(
        string fromKind,
        string fromId,
        string toKind,
        string toId,
        string relType,
        RelationshipBasis basis,
        double confidence,
        string evidence)
        => InsertRelationship(
            fromId,
            fromKind,
            toId,
            toKind,
            relType,
            basis.ToString().ToLowerInvariant(),
            confidence,
            evidence);

    /// <inheritdoc />
    public IReadOnlyList<IndexedTypeSymbol> ListTypeSymbols()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.name, s.fqn, s.kind, s.signature, f.path
            FROM symbols s
            JOIN files f ON f.id = s.file_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0
              AND s.kind IN ('class', 'interface', 'record', 'struct')
            LIMIT 20000;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        using var reader = cmd.ExecuteReader();
        var list = new List<IndexedTypeSymbol>();
        while (reader.Read())
        {
            list.Add(new IndexedTypeSymbol(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5)));
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<IndexedChunk> ListChunks()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.heading_path, c.text, f.path
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id
            JOIN files f ON f.id = d.file_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0
            LIMIT 20000;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        using var reader = cmd.ExecuteReader();
        var list = new List<IndexedChunk>();
        while (reader.Read())
        {
            list.Add(new IndexedChunk(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<IndexedTestMethod> ListTestMethods()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.name, parent.id, parent.fqn, f.path
            FROM tests t
            JOIN symbols s ON s.id = t.symbol_id
            JOIN files f ON f.id = s.file_id
            LEFT JOIN symbols parent ON parent.fqn = s.containing_type_fqn AND parent.file_id = s.file_id
            WHERE f.repository_id = $repo AND f.is_deleted = 0
            LIMIT 20000;
            """;
        cmd.Parameters.AddWithValue("$repo", RepositoryId);
        using var reader = cmd.ExecuteReader();
        var list = new List<IndexedTestMethod>();
        while (reader.Read())
        {
            list.Add(new IndexedTestMethod(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        }

        return list;
    }

    /// <inheritdoc />
    public int CountRelationships(string relType, string? basis)
    {
        using var cmd = _connection.CreateCommand();
        if (basis is null)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM relationships WHERE rel_type = $type;";
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM relationships WHERE rel_type = $type AND basis = $basis;";
            cmd.Parameters.AddWithValue("$basis", basis);
        }

        cmd.Parameters.AddWithValue("$type", relType);
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
