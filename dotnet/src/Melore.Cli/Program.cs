using System.CommandLine;
using Melore.Contracts;
using Melore.Host;
using Microsoft.Extensions.Logging;

var configOption = new Option<FileInfo>("--config")
{
    Description = "melore.json のパス",
    Required = true
};
var limitOption = new Option<int>("--limit")
{
    Description = "検索件数",
    DefaultValueFactory = _ => 8
};
var queryArgument = new Argument<string>("query")
{
    Description = "自然言語またはキーワード"
};

var indexCommand = new Command("index", "対象リポジトリを差分インデックスする");
indexCommand.Options.Add(configOption);
indexCommand.SetAction(async (parse, ct) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    await using var host = CreateRuntime(settings);
    var status = await host.Indexer.IndexAsync(settings, ct).ConfigureAwait(false);
    PrintStatus(status);
    return 0;
});

var statusCommand = new Command("status", "インデックスの件数とリビジョンを表示する");
statusCommand.Options.Add(configOption);
statusCommand.SetAction((parse, _) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    using var host = CreateRuntime(settings);
    PrintStatus(host.Search.Status(settings.RepositoryName));
    return Task.FromResult(0);
});

var searchCommand = new Command("search", "仕様とコードを検索する");
searchCommand.Options.Add(configOption);
searchCommand.Options.Add(limitOption);
searchCommand.Arguments.Add(queryArgument);
searchCommand.SetAction(async (parse, ct) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    var query = parse.GetRequiredValue(queryArgument);
    var limit = parse.GetValue(limitOption);
    await using var host = CreateRuntime(settings);
    var freshness = await host.Indexer.EnsureFreshAsync(settings, ct).ConfigureAwait(false);
    Console.WriteLine(
        $"dirty\t{freshness.Dirty}\tfiles\t{freshness.DirtyFileCount}\trefreshed\t{freshness.RefreshedFileCount}");
    var hits = await host.Search.SearchAsync(query, limit, ct).ConfigureAwait(false);
    foreach (var hit in hits)
    {
        Console.WriteLine(
            $"{hit.Kind}\t{hit.Source.Path}:{hit.Source.StartLine}-{hit.Source.EndLine}\t{hit.Title}");
        Console.WriteLine($"  {hit.Snippet}");
    }

    if (hits.Count == 0)
    {
        Console.WriteLine("ヒットなし");
    }

    return 0;
});

var referencesCommand = new Command("references", "仕様またはシンボルの関連を列挙する");
var targetArgument = new Argument<string>("target") { Description = "ID / FQN / 名前" };
referencesCommand.Options.Add(configOption);
referencesCommand.Options.Add(limitOption);
referencesCommand.Arguments.Add(targetArgument);
referencesCommand.SetAction(async (parse, ct) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    var target = parse.GetRequiredValue(targetArgument);
    await using var host = CreateRuntime(settings);
    await host.Indexer.EnsureFreshAsync(settings, ct).ConfigureAwait(false);
    foreach (var related in host.References.GetReferences(target, parse.GetValue(limitOption)))
    {
        Console.WriteLine(
            $"{related.Basis}\t{related.RelType}\t{related.Hit.Source.Path}:{related.Hit.Source.StartLine}\t{related.Hit.Title}");
    }

    return 0;
});

var traceCommand = new Command("trace", "所属と呼び出しを追跡する");
var depthOption = new Option<int>("--depth") { DefaultValueFactory = _ => 2 };
var relOption = new Option<string?>("--rel")
{
    Description = "関係種別。カンマ区切り。空なら全部"
};
var traceLimitOption = new Option<int>("--limit")
{
    Description = "ノード数の上限",
    DefaultValueFactory = _ => Melore.Core.Search.TraceService.DefaultLimit
};
traceCommand.Options.Add(configOption);
traceCommand.Options.Add(depthOption);
traceCommand.Options.Add(traceLimitOption);
traceCommand.Options.Add(relOption);
traceCommand.Arguments.Add(targetArgument);
traceCommand.SetAction((parse, _) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    using var host = CreateRuntime(settings);
    var rel = parse.GetValue(relOption);
    var relTypes = string.IsNullOrWhiteSpace(rel)
        ? null
        : rel.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var result = host.Trace.Trace(
        parse.GetRequiredValue(targetArgument),
        parse.GetValue(depthOption),
        parse.GetValue(traceLimitOption),
        relTypes);
    if (result.Root is null)
    {
        Console.WriteLine(result.Limitation);
        return Task.FromResult(1);
    }

    Console.WriteLine($"root\t{result.Root.Name}\t{result.Root.Path}:{result.Root.StartLine}");
    var byId = result.Nodes.ToDictionary(n => n.Id);
    foreach (var edge in result.Edges)
    {
        var from = byId[edge.FromId].Name;
        var to = byId[edge.ToId].Name;
        Console.WriteLine($"{edge.Basis}\t{edge.RelType}\t{from} -> {to}");
    }

    if (result.Limitation is not null)
    {
        Console.WriteLine(result.Limitation);
    }

    return Task.FromResult(0);
});

var explainCommand = new Command("explain", "検索上位だけを根拠に説明する");
explainCommand.Options.Add(configOption);
explainCommand.Options.Add(limitOption);
explainCommand.Arguments.Add(queryArgument);
explainCommand.SetAction(async (parse, ct) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    var query = parse.GetRequiredValue(queryArgument);
    var limit = parse.GetValue(limitOption);
    await using var host = CreateRuntime(settings);
    await host.Indexer.EnsureFreshAsync(settings, ct).ConfigureAwait(false);
    var result = await host.Explain.ExplainAsync(query, limit, ct).ConfigureAwait(false);
    Console.WriteLine(result.Answer);
    if (result.Limitation is not null)
    {
        Console.WriteLine($"limitation\t{result.Limitation}");
    }

    Console.WriteLine($"chat\t{result.ChatUsed}\t{result.ChatModel}");
    Console.WriteLine($"evidence_ids\t{string.Join(",", result.EvidenceIds)}");
    foreach (var hit in result.Evidence)
    {
        Console.WriteLine($"{hit.Kind}\t{hit.Path}:{hit.StartLine}-{hit.EndLine}\t{hit.Title}");
    }

    return 0;
});

var evalCommand = new Command("eval", "ゴールデン Hit@8 を測る");
evalCommand.Options.Add(configOption);
evalCommand.Options.Add(limitOption);
evalCommand.SetAction(async (parse, ct) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    var limit = parse.GetValue(limitOption);
    await using var host = CreateRuntime(settings);
    var bundle = await new Melore.Core.Evaluation.EvalService(host.Search)
        .RunAsync(limit, ct)
        .ConfigureAwait(false);
    PrintEval(bundle.Dev);
    PrintEval(bundle.Test);
    var failed = bundle.Test.HitAt8 < Melore.Core.Evaluation.EvalService.TestHitAt8Threshold
                 || bundle.Test.HitAt1 < Melore.Core.Evaluation.EvalService.TestHitAt1Threshold;
    return failed ? 1 : 0;
});

var root = new RootCommand("Melore — ローカル仕様インデックス");
root.Subcommands.Add(indexCommand);
root.Subcommands.Add(statusCommand);
root.Subcommands.Add(searchCommand);
root.Subcommands.Add(referencesCommand);
root.Subcommands.Add(traceCommand);
root.Subcommands.Add(explainCommand);
root.Subcommands.Add(evalCommand);
return await root.Parse(args).InvokeAsync();

static MeloreRuntime CreateRuntime(MeloreSettings settings)
{
    var loggers = LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(options => options.SingleLine = true);
        builder.SetMinimumLevel(LogLevel.Information);
    });
    return MeloreRuntime.Create(settings, loggers);
}

static void PrintStatus(IndexStatus status)
{
    Console.WriteLine($"repository\t{status.RepositoryName}");
    Console.WriteLine($"revision\t{status.IndexedRevision ?? "(none)"}");
    Console.WriteLine($"document_chunks\t{status.DocumentChunkCount}");
    Console.WriteLine($"symbols\t{status.SymbolCount}");
    Console.WriteLine($"tests\t{status.TestCount}");
    Console.WriteLine($"vector\t{status.VectorSearchEnabled}");
    Console.WriteLine($"embeddings\t{status.EmbeddingEnabled}");
    Console.WriteLine($"updated_at\t{status.UpdatedAtUtc?.ToString("O") ?? "(none)"}");
    Console.WriteLine($"mcp_invocations\t{status.McpInvocationCount}");
    Console.WriteLine($"mcp_last_at\t{status.LastMcpInvocationUtc?.ToString("O") ?? "(none)"}");
}

static void PrintEval(Melore.Core.Evaluation.EvalReport report)
{
    Console.WriteLine($"{report.Name}\thit_at_1\t{report.HitAt1:0.000}");
    Console.WriteLine($"{report.Name}\thit_at_8\t{report.HitAt8:0.000}");
    foreach (var row in report.Rows)
    {
        Console.WriteLine($"{report.Name}\t{row.Id}\t{(row.HitAt8 ? "HIT" : "MISS")}\t{row.Query}");
        if (!row.HitAt8)
        {
            foreach (var path in row.Paths.Take(8))
            {
                Console.WriteLine($"  {path}");
            }
        }
    }
}
