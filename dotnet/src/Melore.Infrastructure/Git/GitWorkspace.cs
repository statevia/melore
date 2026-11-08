using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Melore.Contracts;
using Melore.Core.Models;
using Melore.Core.Ports;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Melore.Infrastructure.Git;

/// <summary>
/// Git と glob で対象ファイルを列挙する。
/// </summary>
/// <param name="settings">対象リポジトリ設定。</param>
public sealed class GitWorkspace(MeloreSettings settings) : IGitWorkspace
{
    private readonly string? _gitPath = GitExecutableLocator.Find();
    private readonly GlobLifecycleClassifier _lifecycles = new(settings.History);

    /// <inheritdoc />
    public async Task<string?> GetHeadRevisionAsync(CancellationToken cancellationToken)
    {
        var (exit, stdout, _) = await RunGitAsync(["rev-parse", "HEAD"], stdin: null, cancellationToken)
            .ConfigureAwait(false);
        return exit == 0 ? stdout.Trim() : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositoryFile>> ListFilesAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(settings.RepositoryPath);
        var matcher = CreateMatcher();

        var matches = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(root)));
        var relatives = new List<(string Relative, string Full, string Language)>();
        foreach (var file in matches.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = file.Path.Replace('\\', '/');
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(full))
            {
                continue;
            }

            var language = Path.GetExtension(relative).ToLowerInvariant() switch
            {
                ".md" => "markdown",
                ".cs" => "csharp",
                _ => null
            };
            if (language is null)
            {
                continue;
            }

            relatives.Add((relative, full, language));
        }

        var hashes = await HashWorkingTreeAsync(relatives.Select(r => r.Relative).ToList(), cancellationToken)
            .ConfigureAwait(false);
        var files = new List<RepositoryFile>();
        foreach (var (relative, full, language) in relatives)
        {
            if (!hashes.TryGetValue(relative, out var sha))
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(full, cancellationToken)
                    .ConfigureAwait(false);
                sha = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            }

            files.Add(new RepositoryFile(relative, full, sha, language, _lifecycles.Classify(relative)));
        }

        return files;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitRename>> ListRenamesAsync(
        string? fromRevision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fromRevision))
        {
            return [];
        }

        var (exit, stdout, _) = await RunGitAsync(
                ["diff", "--name-status", "-M90", fromRevision, "HEAD"],
                stdin: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0 || stdout.Length == 0)
        {
            return [];
        }

        var list = new List<GitRename>();
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3 || line[0] != 'R')
            {
                continue;
            }

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            list.Add(new GitRename(parts[1].Replace('\\', '/'), parts[2].Replace('\\', '/')));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorktreeChange>> ListWorktreeChangesAsync(CancellationToken cancellationToken)
    {
        var (exit, stdout, _) = await RunGitAsync(
                ["status", "--porcelain", "--untracked-files=all"],
                stdin: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (exit != 0 || stdout.Length == 0)
        {
            return [];
        }

        return GitStatusParser.Parse(stdout)
            .Where(change => IsIncluded(change.RelativePath))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<RepositoryFile?> TryReadFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var relative = relativePath.Replace('\\', '/');
        if (!IsIncluded(relative))
        {
            return null;
        }

        var root = Path.GetFullPath(settings.RepositoryPath);
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            return null;
        }

        var language = Path.GetExtension(relative).ToLowerInvariant() switch
        {
            ".md" => "markdown",
            ".cs" => "csharp",
            _ => null
        };
        if (language is null)
        {
            return null;
        }

        var hashes = await HashWorkingTreeAsync([relative], cancellationToken).ConfigureAwait(false);
        if (!hashes.TryGetValue(relative, out var sha))
        {
            var bytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
            sha = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        return new RepositoryFile(relative, full, sha, language, _lifecycles.Classify(relative));
    }

    private Matcher CreateMatcher()
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var include in settings.Include)
        {
            matcher.AddInclude(include.Replace('\\', '/'));
        }

        foreach (var exclude in settings.Exclude)
        {
            matcher.AddExclude(exclude.Replace('\\', '/'));
        }

        return matcher;
    }

    private bool IsIncluded(string relativePath)
        => CreateMatcher().Match(relativePath.Replace('\\', '/')).HasMatches;

    private async Task<Dictionary<string, string>> HashWorkingTreeAsync(
        List<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (relativePaths.Count == 0)
        {
            return map;
        }

        const int batchSize = 64;
        for (var offset = 0; offset < relativePaths.Count; offset += batchSize)
        {
            var batch = relativePaths.Skip(offset).Take(batchSize).ToList();
            var stdin = string.Join('\n', batch) + '\n';
            var (exit, stdout, _) = await RunGitAsync(
                    ["hash-object", "--stdin-paths"],
                    stdin,
                    cancellationToken)
                .ConfigureAwait(false);
            if (exit != 0)
            {
                return map;
            }

            var hashes = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var count = Math.Min(hashes.Length, batch.Count);
            for (var i = 0; i < count; i++)
            {
                map[batch[i]] = hashes[i].Trim();
            }
        }

        return map;
    }

    private async Task<(int Exit, string Stdout, string Stderr)> RunGitAsync(
        string[] args,
        string? stdin,
        CancellationToken cancellationToken)
    {
        if (_gitPath is null)
        {
            return (1, "", "git was not found");
        }

        var start = new ProcessStartInfo
        {
            FileName = _gitPath,
            WorkingDirectory = settings.RepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(settings.RepositoryPath);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return (1, "", "git was not started");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        // MCP サーバーの stdin はプロトコル専用。git へ継承させると、読み取り中の同期パイプで止まることがある。
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        process.StandardInput.Close();

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }
}
