using Melore.Core.Search;

namespace Melore.Tests;

public sealed class QueryNormalizerTests
{
    /// <summary>助詞は MATCH から外す。</summary>
    [Fact]
    public void Tokens_DropsJapaneseParticles()
    {
        // Act
        var tokens = QueryNormalizer.Tokens("Fork Join の仕様");

        // Assert
        Assert.Equal(["Fork", "Join", "仕様"], tokens);
    }

    /// <summary>既知の揺れは Expand で足す。</summary>
    [Fact]
    public void Expand_AddsSynonym()
    {
        // Act
        var expanded = QueryNormalizer.Expand("シナリオテスト Docker");

        // Assert
        Assert.Contains("シナリオテスト Docker", expanded);
        Assert.Contains("Scenario", expanded);
        Assert.True(expanded.Count <= 3);
    }
}
