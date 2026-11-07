using Melore.Contracts;
using Melore.Core.Models;
using Melore.Core.Ports;
using Microsoft.Extensions.Logging;

namespace Melore.Core.Indexing;

/// <summary>
/// 差分インデックスを実行する。
/// </summary>
/// <param name="git">対象ワークスペース。</param>
/// <param name="documents">Markdown パーサ。</param>
/// <param name="code">C# 解析。</param>
/// <param name="store">永続化。</param>
/// <param name="embeddings">任意。利用不可ならベクトルは作らない。</param>
/// <param name="logger">進捗ログ。</param>
/// <param name="semantic">calls / implements。未設定なら構文解析のまま。</param>
public sealed class IndexService(
    IGitWorkspace git,
    IDocumentParser documents,
    ICodeAnalyzer code,
    IMeloreStore store,
    IEmbeddingClient embeddings,
    ILogger<IndexService> logger,
    ISemanticGraphExtractor? semantic = null)
{
    /// <summary>検索の直前に自動で再索引する差分の上限。</summary>
    public const int MaxAutoRefresh = 32;
    /// <summary>各解析器の版を合成した文字列。DB の前回値と比較して再構築する。</summary>
    public string AnalyzerVersions =>
        string.Join(';', documents.AnalyzerVersion, code.AnalyzerVersion, embeddings.AnalyzerVersion, RelationshipIndexer.AnalyzerVersion);

    /// <summary>
    /// 対象リポジトリを差分更新する。
    /// </summary>
    public async Task<IndexStatus> IndexAsync(MeloreSettings settings, CancellationToken cancellationToken)
    {
        store.Initialize();
        var revision = await git.GetHeadRevisionAsync(cancellationToken).ConfigureAwait(false);
        var files = await git.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Listed {Count} files for {Revision}", files.Count, revision);
        var analyzerChanged = !string.Equals(store.GetAnalyzerVersions(), AnalyzerVersions, StringComparison.Ordinal);
        if (analyzerChanged)
        {
            store.ClearEmbeddings();
        }

        var previous = analyzerChanged
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(store.GetIndexedBlobShas(), StringComparer.OrdinalIgnoreCase);
        var currentByPath = files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var currentPaths = currentByPath.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        await ApplyRenamesAsync(settings, previous, currentByPath, cancellationToken).ConfigureAwait(false);
        var deleted = DeleteMissing(previous, currentPaths);
        var (parsed, skipped) = await IndexChangedAsync(files, previous, revision, cancellationToken)
            .ConfigureAwait(false);

        var related = RelationshipIndexer.Rebuild(store);
        await ApplySemanticGraphAsync(settings, cancellationToken).ConfigureAwait(false);
        if (embeddings.IsAvailable)
        {
            await EmbedMissingAsync(cancellationToken).ConfigureAwait(false);
        }

        store.CompleteIndex(settings.RepositoryName, settings.RepositoryPath, revision, AnalyzerVersions);
        logger.LogInformation(
            "Index finished. parsed={Parsed} skipped={Skipped} deleted={Deleted} related={Related} analyzerChanged={AnalyzerChanged} revision={Revision}",
            parsed,
            skipped,
            deleted,
            related,
            analyzerChanged,
            revision);
        return store.GetStatus(settings.RepositoryName);
    }

    /// <summary>
    /// blob が同じリネームは解析し直さず、既存の行を新しいパスへ移す。
    /// </summary>
    private async Task ApplyRenamesAsync(
        MeloreSettings settings,
        Dictionary<string, string> previous,
        Dictionary<string, RepositoryFile> currentByPath,
        CancellationToken cancellationToken)
    {
        foreach (var rename in await git.ListRenamesAsync(store.GetStatus(settings.RepositoryName).IndexedRevision, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (!previous.TryGetValue(rename.FromPath, out var oldSha) ||
                !currentByPath.TryGetValue(rename.ToPath, out var next) ||
                !string.Equals(oldSha, next.BlobSha, StringComparison.Ordinal))
            {
                continue;
            }

            if (store.RelocateFile(rename.FromPath, rename.ToPath, next.BlobSha))
            {
                previous.Remove(rename.FromPath);
                previous[rename.ToPath] = next.BlobSha;
                logger.LogInformation("Renamed {From} -> {To}", rename.FromPath, rename.ToPath);
            }
        }
    }

    private int DeleteMissing(Dictionary<string, string> previous, HashSet<string> currentPaths)
    {
        var missing = previous.Keys.Where(path => !currentPaths.Contains(path)).ToList();
        missing.ForEach(store.DeleteFile);
        return missing.Count;
    }

    private async Task<(int Parsed, int Skipped)> IndexChangedAsync(
        IReadOnlyList<RepositoryFile> files,
        Dictionary<string, string> previous,
        string? revision,
        CancellationToken cancellationToken)
    {
        var parsed = 0;
        var skipped = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (previous.TryGetValue(file.RelativePath, out var sha) &&
                string.Equals(sha, file.BlobSha, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            await IndexFileAsync(file, revision, cancellationToken).ConfigureAwait(false);
            parsed++;
        }

        return (parsed, skipped);
    }

    /// <summary>
    /// 差分が上限以内ならそのファイルだけ索引し直す。
    /// </summary>
    public async Task<IndexFreshness> EnsureFreshAsync(MeloreSettings settings, CancellationToken cancellationToken)
    {
        store.Initialize();
        var changes = await git.ListWorktreeChangesAsync(cancellationToken).ConfigureAwait(false);
        if (changes.Count == 0)
        {
            return new IndexFreshness(false, 0, 0);
        }

        if (changes.Count > MaxAutoRefresh)
        {
            return new IndexFreshness(true, changes.Count, 0);
        }

        var revision = await git.GetHeadRevisionAsync(cancellationToken).ConfigureAwait(false);
        var known = store.GetIndexedBlobShas();
        var refreshed = 0;
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.Remove)
            {
                store.DeleteFile(change.RelativePath);
                refreshed++;
                continue;
            }

            var file = await git.TryReadFileAsync(change.RelativePath, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                store.DeleteFile(change.RelativePath);
                refreshed++;
                continue;
            }

            if (known.TryGetValue(file.RelativePath, out var sha) &&
                string.Equals(sha, file.BlobSha, StringComparison.Ordinal))
            {
                continue;
            }

            await IndexFileAsync(file, revision, cancellationToken).ConfigureAwait(false);
            refreshed++;
        }

        if (refreshed > 0 && embeddings.IsAvailable)
        {
            await EmbedMissingAsync(cancellationToken).ConfigureAwait(false);
        }

        if (refreshed > 0)
        {
            store.CompleteIndex(settings.RepositoryName, settings.RepositoryPath, revision, AnalyzerVersions);
        }

        return new IndexFreshness(false, changes.Count, refreshed);
    }

    private async Task IndexFileAsync(RepositoryFile file, string? revision, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(file.FullPath, cancellationToken).ConfigureAwait(false);
        var fileId = store.EnsureFile(file);
        if (file.Language == "markdown")
        {
            var document = documents.Parse(file.RelativePath, text);
            store.UpsertDocument(fileId, file, document, revision);
        }
        else if (file.Language == "csharp")
        {
            var symbols = code.AnalyzeFile(file.RelativePath, text);
            store.UpsertSymbols(fileId, file, symbols, revision);
        }
    }

    private async Task ApplySemanticGraphAsync(MeloreSettings settings, CancellationToken cancellationToken)
    {
        if (semantic is null || string.IsNullOrWhiteSpace(settings.SolutionPath))
        {
            return;
        }

        var full = Path.IsPathRooted(settings.SolutionPath)
            ? settings.SolutionPath
            : Path.Combine(settings.RepositoryPath, settings.SolutionPath);
        if (!File.Exists(full))
        {
            return;
        }

        IReadOnlyList<SemanticRelation> edges;
        try
        {
            edges = await semantic.ExtractAsync(full, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Semantic graph was skipped for {Solution}", full);
            return;
        }

        store.DeleteRelationshipsByType("calls");
        store.DeleteRelationshipsByType("implements");
        foreach (var edge in edges.DistinctBy(e => e.FromFqn + "\t" + e.ToFqn + "\t" + e.RelType))
        {
            var fromId = store.FindSymbolIdByFqn(edge.FromFqn);
            var toId = store.FindSymbolIdByFqn(edge.ToFqn);
            if (fromId is null || toId is null)
            {
                continue;
            }

            store.AddRelationship(new RelationshipEntry(
                "symbol",
                fromId,
                "symbol",
                toId,
                edge.RelType,
                RelationshipBasis.Explicit,
                1.0,
                edge.ToFqn));
        }
    }

    private async Task EmbedMissingAsync(CancellationToken cancellationToken)
    {
        foreach (var chunk in store.ListChunksWithoutEmbeddings())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = await embeddings.EmbedAsync(chunk.Text, isQuery: false, cancellationToken)
                .ConfigureAwait(false);
            if (vector is null || vector.Length == 0)
            {
                continue;
            }

            store.SaveEmbedding(chunk.Id, chunk.Kind, vector, embeddings.ModelName, vector.Length);
        }
    }
}
