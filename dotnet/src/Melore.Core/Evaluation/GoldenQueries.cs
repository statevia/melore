namespace Melore.Core.Evaluation;

/// <summary>
/// evaluation.md のゴールデン 15 件。期待はパス部分文字列の OR。
/// </summary>
public static class GoldenQueries
{
    /// <summary>Phase 4 の Hit@8 対象。</summary>
    public static IReadOnlyList<GoldenQuery> All { get; } =
    [
        new("Q1", "Fork Join", ["docs/specifications/execution/fork-join.md"]),
        new("Q2", "ExecutionEngine", ["ExecutionEngine"]),
        new("Q3", "IExecutionService", ["IExecutionService"]),
        new("Q4", "シナリオテスト Docker", ["AGENTS.md", "getting-started", "guidelines"]),
        new("Q5", "Wait Cancel", ["docs/specifications/execution/wait-cancel.md"]),
        new("Q6", "HTTP ログイン テナント", ["docs/specifications/api-http.md"]),
        new("Q7", "Module zip layout", ["docs/specifications/actions/module-zip-layout.md"]),
        new("Q8", "CoreDbContext", ["CoreDbContext"]),
        new("Q9", "Architecture tests", ["Statevia.Architecture.Tests"]),
        new("Q10", "Definition publish", ["docs/concepts/", "docs/specifications/definition.md"]),
        new("Q11", "ログマスキング", ["docs/specifications/platform/io-log-masking.md"]),
        new("Q12", "Studio feature-first", ["docs/architecture/ui-studio-structure.md"]),
        new("Q13", "Guid.NewGuid", ["docs/development-guidelines.md"]),
        new("Q14", "Hosted Fork", ["fork-join.md"]),
        new("Q15", "Scenario Category", ["Scenario"])
    ];
}

/// <summary>
/// 1 件のゴールデンクエリ。
/// </summary>
/// <param name="Id">評価 ID。</param>
/// <param name="Query">検索文字列。</param>
/// <param name="ExpectedPathSubstrings">ヒットパスがどれか 1 つ含めば合格。</param>
public sealed record GoldenQuery(string Id, string Query, IReadOnlyList<string> ExpectedPathSubstrings);
