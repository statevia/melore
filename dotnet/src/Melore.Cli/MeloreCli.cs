using System.CommandLine;
using Melore.Contracts;
using Melore.Core.Evaluation;
using Melore.Core.Search;
using Melore.Host;
using Microsoft.Extensions.Logging;

namespace Melore.Cli;

/// <summary>
/// melore のサブコマンド定義と実行。
/// </summary>
/// <remarks>
/// 結果は <paramref name="output"/> へ書く。ログは既定のコンソールへ出る。
/// </remarks>
/// <param name="output">結果の書き込み先。</param>
public sealed class MeloreCli(TextWriter output)
{
    private readonly Option<FileInfo> _config = new("--config")
    {
        Description = "melore.json のパス",
        Required = true
    };

    private readonly Option<int> _limit = new("--limit")
    {
        Description = "検索件数",
        DefaultValueFactory = _ => 8
    };

    private readonly Option<int> _traceLimit = new("--limit")
    {
        Description = "ノード数の上限",
        DefaultValueFactory = _ => TraceService.DefaultLimit
    };

    private readonly Option<int> _depth = new("--depth") { DefaultValueFactory = _ => 2 };

    private readonly Option<string?> _rel = new("--rel")
    {
        Description = "関係種別。カンマ区切り。空なら全部"
    };

    private readonly Argument<string> _query = new("query")
    {
        Description = "自然言語またはキーワード"
    };

    private readonly Argument<string> _target = new("target") { Description = "ID / FQN / 名前" };

    /// <summary>
    /// 全サブコマンドを持つルートコマンドを作る。
    /// </summary>
    public RootCommand BuildRoot()
    {
        var root = new RootCommand("Melore — ローカル仕様インデックス");
        root.Subcommands.Add(IndexCommand());
        root.Subcommands.Add(StatusCommand());
        root.Subcommands.Add(SearchCommand());
        root.Subcommands.Add(ReferencesCommand());
        root.Subcommands.Add(TraceCommand());
        root.Subcommands.Add(ExplainCommand());
        root.Subcommands.Add(EvalCommand());
        return root;
    }

    private Command IndexCommand()
    {
        var command = new Command("index", "対象リポジトリを差分インデックスする");
        command.Options.Add(_config);
        command.SetAction((parse, ct) => RunIndexAsync(parse, ct));
        return command;
    }

    private Command StatusCommand()
    {
        var command = new Command("status", "インデックスの件数とリビジョンを表示する");
        command.Options.Add(_config);
        command.SetAction((parse, _) => Task.FromResult(RunStatus(parse)));
        return command;
    }

    private Command SearchCommand()
    {
        var command = new Command("search", "仕様とコードを検索する");
        command.Options.Add(_config);
        command.Options.Add(_limit);
        command.Arguments.Add(_query);
        command.SetAction((parse, ct) => RunSearchAsync(parse, ct));
        return command;
    }

    private Command ReferencesCommand()
    {
        var command = new Command("references", "仕様またはシンボルの関連を列挙する");
        command.Options.Add(_config);
        command.Options.Add(_limit);
        command.Arguments.Add(_target);
        command.SetAction((parse, ct) => RunReferencesAsync(parse, ct));
        return command;
    }

    private Command TraceCommand()
    {
        var command = new Command("trace", "所属と呼び出しを追跡する");
        command.Options.Add(_config);
        command.Options.Add(_depth);
        command.Options.Add(_traceLimit);
        command.Options.Add(_rel);
        command.Arguments.Add(_target);
        command.SetAction((parse, _) => Task.FromResult(RunTrace(parse)));
        return command;
    }

    private Command ExplainCommand()
    {
        var command = new Command("explain", "検索上位だけを根拠に説明する");
        command.Options.Add(_config);
        command.Options.Add(_limit);
        command.Arguments.Add(_query);
        command.SetAction((parse, ct) => RunExplainAsync(parse, ct));
        return command;
    }

    private Command EvalCommand()
    {
        var command = new Command("eval", "ゴールデン Hit@8 を測る");
        command.Options.Add(_config);
        command.Options.Add(_limit);
        command.SetAction((parse, ct) => RunEvalAsync(parse, ct));
        return command;
    }

    private async Task<int> RunIndexAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        var settings = SettingsLoader.Load(parse.GetRequiredValue(_config));
        await using var host = CreateRuntime(settings);
        var status = await host.Indexer.IndexAsync(settings, cancellationToken).ConfigureAwait(false);
        PrintStatus(status);
        return 0;
    }

    private int RunStatus(ParseResult parse)
    {
        var settings = SettingsLoader.Load(parse.GetRequiredValue(_config));
        using var host = CreateRuntime(settings);
        PrintStatus(host.Search.Status(settings.RepositoryName));
        return 0;
    }

    private async Task<int> RunSearchAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        var settings = SettingsLoader.Load(parse.GetRequiredValue(_config));
        var query = parse.GetRequiredValue(_query);
        var limit = parse.GetValue(_limit);
        await using var host = CreateRuntime(settings);
        var freshness = await host.Indexer.EnsureFreshAsync(settings, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(
            $"dirty\t{freshness.Dirty}\tfiles\t{freshness.DirtyFileCount}\trefreshed\t{freshness.RefreshedFileCount}").ConfigureAwait(false);
        var hits = await host.Search.SearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
        foreach (var hit in hits)
        {
            await output.WriteLineAsync(
                $"{hit.Kind}\t{hit.Source.Path}:{hit.Source.StartLine}-{hit.Source.EndLine}\t{hit.Title}").ConfigureAwait(false);
            await output.WriteLineAsync($"  {hit.Snippet}").ConfigureAwait(false);
        }

        if (hits.Count == 0)
        {
            await output.WriteLineAsync("ヒットなし").ConfigureAwait(false);
        }

        return 0;
    }

    private async Task<int> RunReferencesAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        var settings = SettingsLoader.Load(parse.GetRequiredValue(_config));
        var target = parse.GetRequiredValue(_target);
        await using var host = CreateRuntime(settings);
        await host.Indexer.EnsureFreshAsync(settings, cancellationToken).ConfigureAwait(false);
        foreach (var related in host.References.GetReferences(target, parse.GetValue(_limit)))
        {
            await output.WriteLineAsync(
                $"{related.Basis}\t{related.RelType}\t{related.Hit.Source.Path}:{related.Hit.Source.StartLine}\t{related.Hit.Title}").ConfigureAwait(false);
        }

        return 0;
    }

    private int RunTrace(ParseResult parse)
    {
        var settings = SettingsLoader.Load(parse.GetRequiredValue(_config));
        using var host = CreateRuntime(settings);
        var rel = parse.GetValue(_rel);
        var relTypes = string.IsNullOrWhiteSpace(rel)
            ? null
            : rel.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = host.Trace.Trace(
            parse.GetRequiredValue(_target),
            parse.GetValue(_depth),
            parse.GetValue(_traceLimit),
            relTypes);
        if (result.Root is null)
        {
            output.WriteLine(result.Limitation);
            return 1;
        }

        output.WriteLine($"root\t{result.Root.Name}\t{result.Root.Path}:{result.Root.StartLine}");
        var byId = result.Nodes.ToDictionary(n => n.Id);
        foreach (var edge in result.Edges)
        {
            var from = byId[edge.FromId].Name;
            var to = byId[edge.ToId].Name;
            output.WriteLine($"{edge.Basis}\t{edge.RelType}\t{from} -> {to}");
        }

        if (result.Limitation is not null)
        {
            output.WriteLine(result.Limitation);
        }

        return 0;
    }

    private async Task<int> RunExplainAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        var settings = SettingsLoader.Load(parse.GetRequiredValue(_config));
        var query = parse.GetRequiredValue(_query);
        var limit = parse.GetValue(_limit);
        await using var host = CreateRuntime(settings);
        await host.Indexer.EnsureFreshAsync(settings, cancellationToken).ConfigureAwait(false);
        var result = await host.Explain.ExplainAsync(query, limit, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(result.Answer).ConfigureAwait(false);
        if (result.Limitation is not null)
        {
            await output.WriteLineAsync($"limitation\t{result.Limitation}").ConfigureAwait(false);
        }

        await output.WriteLineAsync($"chat\t{result.ChatUsed}\t{result.ChatModel}").ConfigureAwait(false);
        await output.WriteLineAsync($"evidence_ids\t{string.Join(",", result.EvidenceIds)}").ConfigureAwait(false);
        foreach (var hit in result.Evidence)
        {
            await output.WriteLineAsync($"{hit.Kind}\t{hit.Path}:{hit.StartLine}-{hit.EndLine}\t{hit.Title}").ConfigureAwait(false);
        }

        return 0;
    }

    private async Task<int> RunEvalAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        var settings = SettingsLoader.Load(parse.GetRequiredValue(_config));
        var limit = parse.GetValue(_limit);
        await using var host = CreateRuntime(settings);
        var bundle = await new EvalService(host.Search)
            .RunAsync(limit, cancellationToken)
            .ConfigureAwait(false);
        PrintEval(bundle.Dev);
        PrintEval(bundle.Test);
        var failed = bundle.Test.HitAt8 < EvalService.TestHitAt8Threshold
                     || bundle.Test.HitAt1 < EvalService.TestHitAt1Threshold;
        return failed ? 1 : 0;
    }

    private static MeloreRuntime CreateRuntime(MeloreSettings settings)
    {
        var loggers = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options => options.SingleLine = true);
            builder.SetMinimumLevel(LogLevel.Information);
        });
        return MeloreRuntime.Create(settings, loggers);
    }

    private void PrintStatus(IndexStatus status)
    {
        output.WriteLine($"repository\t{status.RepositoryName}");
        output.WriteLine($"revision\t{status.IndexedRevision ?? "(none)"}");
        output.WriteLine($"document_chunks\t{status.DocumentChunkCount}");
        output.WriteLine($"symbols\t{status.SymbolCount}");
        output.WriteLine($"tests\t{status.TestCount}");
        output.WriteLine($"vector\t{status.VectorSearchEnabled}");
        output.WriteLine($"embeddings\t{status.EmbeddingEnabled}");
        output.WriteLine($"updated_at\t{status.UpdatedAtUtc?.ToString("O") ?? "(none)"}");
        output.WriteLine($"mcp_invocations\t{status.McpInvocationCount}");
        output.WriteLine($"mcp_last_at\t{status.LastMcpInvocationUtc?.ToString("O") ?? "(none)"}");
    }

    private void PrintEval(EvalReport report)
    {
        output.WriteLine($"{report.Name}\thit_at_1\t{report.HitAt1:0.000}");
        output.WriteLine($"{report.Name}\thit_at_8\t{report.HitAt8:0.000}");
        foreach (var row in report.Rows)
        {
            output.WriteLine($"{report.Name}\t{row.Id}\t{(row.HitAt8 ? "HIT" : "MISS")}\t{row.Query}");
            if (row.HitAt8)
            {
                continue;
            }

            foreach (var path in row.Paths.Take(8))
            {
                output.WriteLine($"  {path}");
            }
        }
    }
}
