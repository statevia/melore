using Melore.Contracts;
using Melore.Core.Search;

namespace Melore.Tests;

public sealed class ReciprocalRankFusionTests
{
    /// <summary>同一 ID はスコアを加算し、片方にしか無い ID も残る。</summary>
    [Fact]
    public void Merge_CombinesRanks()
    {
        // Arrange
        var a = Hit("1", "A");
        var b = Hit("2", "B");
        var c = Hit("3", "C");

        // Act
        var merged = ReciprocalRankFusion.Merge(
            [
                [a, b],
                [b, c]
            ],
            limit: 10);

        // Assert
        Assert.Equal("2", merged[0].Id);
        Assert.Contains(merged, h => h.Id == "1");
        Assert.Contains(merged, h => h.Id == "3");
    }

    private static SearchHit Hit(string id, string title)
        => new(
            id,
            HitKind.DocumentChunk,
            title,
            title,
            0,
            new SourceSpan("docs/a.md", 1, 2, "abc"),
            DocumentLifecycle.Current);
}
