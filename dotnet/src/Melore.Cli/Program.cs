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
referencesCommand.SetAction((parse, _) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    var target = parse.GetRequiredValue(targetArgument);
    using var host = CreateRuntime(settings);
    foreach (var related in host.References.GetReferences(target, parse.GetValue(limitOption)))
    {
        Console.WriteLine(
            $"{related.Basis}\t{related.RelType}\t{related.Hit.Source.Path}:{related.Hit.Source.StartLine}\t{related.Hit.Title}");
    }

    return Task.FromResult(0);
});

var traceCommand = new Command("trace", "所属と継承を追跡する");
var depthOption = new Option<int>("--depth") { DefaultValueFactory = _ => 2 };
traceCommand.Options.Add(configOption);
traceCommand.Options.Add(depthOption);
traceCommand.Arguments.Add(targetArgument);
traceCommand.SetAction((parse, _) =>
{
    var settings = SettingsLoader.Load(parse.GetRequiredValue(configOption));
    using var host = CreateRuntime(settings);
    var result = host.Trace.Trace(parse.GetRequiredValue(targetArgument), parse.GetValue(depthOption));
    if (result.Root is null)
    {
        Console.WriteLine(result.Limitation);
        return Task.FromResult(1);
    }

    Console.WriteLine($"root\t{result.Root.Title}\t{result.Root.Source.Path}:{result.Root.Source.StartLine}");
    foreach (var edge in result.Edges)
    {
        var from = result.Nodes.First(n => n.Id == edge.FromId).Title;
        var to = result.Nodes.First(n => n.Id == edge.ToId).Title;
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
    var report = await new Melore.Core.Evaluation.EvalService(host.Search)
        .RunAsync(limit, ct)
        .ConfigureAwait(false);
    Console.WriteLine($"hit_at_{report.Limit}\t{report.HitAtK:0.000}");
    foreach (var row in report.Rows)
    {
        Console.WriteLine($"{row.Id}\t{(row.Hit ? "HIT" : "MISS")}\t{row.Query}");
        if (!row.Hit)
        {
            foreach (var path in row.Paths.Take(8))
            {
                Console.WriteLine($"  {path}");
            }
        }
    }

    return report.HitAtK >= 0.9 ? 0 : 1;
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
}
