using Melore.Core.Models;
using Melore.Core.Ports;
using Melore.Core.Search;
using Melore.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Melore.Tests;

public sealed class ExplainTests
{
    /// <summary>ヒットが無いときは LLM を呼ばず、不足を返す。</summary>
    [Fact]
    public async Task Explain_NoHits_DoesNotCallChat()
    {
        // Arrange
        using var store = CreateStore();
        var chat = new RecordingChatClient();
        var explain = new ExplainService(
            new SearchService(store, new NullEmbeddingClient(), new NoOpReranker()),
            store,
            chat,
            NullLogger<ExplainService>.Instance);

        // Act
        var result = await explain.ExplainAsync("存在しないクエリ xyzzy-melore", 8, CancellationToken.None);

        // Assert
        Assert.Equal(ExplainPromptBuilder.NoEvidenceAnswer, result.Answer);
        Assert.False(result.ChatUsed);
        Assert.Empty(result.EvidenceIds);
        Assert.Equal(0, chat.CallCount);
        Assert.DoesNotContain("docs/invented.md", result.Answer, StringComparison.Ordinal);
    }

    /// <summary>Chat 不可でも根拠ヒットは返し、パスを捏造しない。</summary>
    [Fact]
    public async Task Explain_ChatUnavailable_ReturnsEvidenceOnly()
    {
        // Arrange
        using var store = SeedForkJoin();
        var explain = new ExplainService(
            new SearchService(store, new NullEmbeddingClient(), new NoOpReranker()),
            store,
            new NullChatClient(),
            NullLogger<ExplainService>.Instance);

        // Act
        var result = await explain.ExplainAsync("Fork Join", 8, CancellationToken.None);

        // Assert
        Assert.Equal(ExplainPromptBuilder.ChatUnavailableAnswer, result.Answer);
        Assert.False(result.ChatUsed);
        Assert.NotEmpty(result.EvidenceIds);
        Assert.Contains(result.Evidence, h => h.Path.Contains("fork-join.md", StringComparison.Ordinal));
    }

    /// <summary>プロンプトは検索上位の本文だけを含み、索引外テキストを入れない。</summary>
    [Fact]
    public async Task Explain_PromptContainsOnlyRetrievedChunks()
    {
        // Arrange
        using var store = SeedContradiction();
        var chat = new RecordingChatClient { Reply = "仕様は all、実装コメントは any。矛盾。" };
        var explain = new ExplainService(
            new SearchService(store, new NullEmbeddingClient(), new NoOpReranker()),
            store,
            chat,
            NullLogger<ExplainService>.Instance);

        // Act
        var result = await explain.ExplainAsync("Join", 8, CancellationToken.None);

        // Assert
        Assert.True(result.ChatUsed);
        Assert.Contains("all branches", chat.LastUser, StringComparison.Ordinal);
        Assert.Contains("any-branch", chat.LastUser, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-not-indexed", chat.LastUser, StringComparison.Ordinal);
        Assert.Contains("矛盾", ExplainPromptBuilder.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(result.EvidenceIds, chat.EvidenceIdsFromPrompt);
        Assert.All(result.Evidence, h => Assert.False(string.IsNullOrWhiteSpace(h.Path)));
    }

    /// <summary>文字数上限を超えると後続チャンクを落とす。</summary>
    [Fact]
    public void BuildUserPrompt_StopsAtBudget()
    {
        // Arrange
        var huge = new string('あ', ExplainPromptBuilder.MaxCharsPerChunk);
        var evidence = new List<ExplainEvidence>
        {
            new("a", "DocumentChunk", "A", "docs/a.md", 1, 2, huge, null),
            new("b", "DocumentChunk", "B", "docs/b.md", 3, 4, huge, null),
            new("c", "DocumentChunk", "C", "docs/c.md", 5, 6, huge, null),
            new("d", "DocumentChunk", "D", "docs/d.md", 7, 8, huge, null),
            new("e", "DocumentChunk", "E", "docs/e.md", 9, 10, huge, null),
            new("f", "DocumentChunk", "F", "docs/f.md", 11, 12, huge, null),
            new("g", "DocumentChunk", "G", "docs/g.md", 13, 14, huge, null),
            new("h", "DocumentChunk", "H", "docs/h.md", 15, 16, huge, null)
        };

        // Act
        var (_, ids) = ExplainPromptBuilder.BuildUserPrompt("q", evidence);

        // Assert
        Assert.NotEmpty(ids);
        Assert.True(ids.Count < evidence.Count);
        Assert.Equal("a", ids[0]);
        Assert.DoesNotContain("h", ids);
    }

    private static SqliteMeloreStore CreateStore()
    {
        var db = Path.Combine(Path.GetTempPath(), "melore-tests", Guid.NewGuid().ToString("N") + ".db");
        var store = new SqliteMeloreStore(db, "fixture", "C:\\tmp\\fixture");
        store.Initialize();
        return store;
    }

    private static SqliteMeloreStore SeedForkJoin()
    {
        var store = CreateStore();
        var file = new RepositoryFile("docs/specifications/execution/fork-join.md", "C:\\tmp\\fj.md", "sha", "markdown");
        store.UpsertDocument(
            store.EnsureFile(file),
            file,
            new ParsedDocument(
                "Fork Join",
                "specification",
                [new DocumentChunk("Fork Join", null, "Hosted Fork の親 graph を合成する。", 10, 20, 0)],
                []),
            "deadbeef");
        return store;
    }

    private static SqliteMeloreStore SeedContradiction()
    {
        var store = CreateStore();
        var spec = new RepositoryFile("docs/specifications/execution/fork-join.md", "C:\\tmp\\spec.md", "sha1", "markdown");
        store.UpsertDocument(
            store.EnsureFile(spec),
            spec,
            new ParsedDocument(
                "Join",
                "specification",
                [
                    new DocumentChunk(
                        "Join",
                        null,
                        "Join waits for all branches before proceeding.",
                        12,
                        16,
                        0)
                ],
                []),
            "rev");
        var impl = new RepositoryFile("dotnet/core/engine/Join.cs", "C:\\tmp\\Join.cs", "sha2", "csharp");
        store.UpsertDocument(
            store.EnsureFile(impl),
            impl,
            new ParsedDocument(
                "Join.cs",
                "code",
                [
                    new DocumentChunk(
                        "Join comment",
                        null,
                        "実装コメント: proceed when any-branch completes.",
                        40,
                        44,
                        0)
                ],
                []),
            "rev");
        return store;
    }

    private sealed class RecordingChatClient : IMeloreChatClient
    {
        public bool IsAvailable => true;

        public string ModelName => "fake";

        public string Reply { get; set; } = "ok";

        public int CallCount { get; private set; }

        public string LastUser { get; private set; } = "";

        public IReadOnlyList<string> EvidenceIdsFromPrompt { get; private set; } = [];

        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            _ = systemPrompt;
            CallCount++;
            LastUser = userPrompt;
            EvidenceIdsFromPrompt = System.Text.RegularExpressions.Regex
                .Matches(userPrompt, @"### \[([^\]]+)\]")
                .Select(m => m.Groups[1].Value)
                .ToList();
            return Task.FromResult<string?>(Reply);
        }
    }
}
