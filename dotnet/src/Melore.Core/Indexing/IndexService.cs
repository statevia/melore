using Melore.Contracts;
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
public sealed class IndexService(
    IGitWorkspace git,
    IDocumentParser documents,
    ICodeAnalyzer code,
    IMeloreStore store,
    IEmbeddingClient embeddings,
    ILogger<IndexService> logger)
{
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

        var deleted = 0;
        foreach (var path in previous.Keys)
        {
            if (!currentPaths.Contains(path))
            {
                store.DeleteFile(path);
                deleted++;
            }
        }

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

            parsed++;
        }

        var related = RelationshipIndexer.Rebuild(store);
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
