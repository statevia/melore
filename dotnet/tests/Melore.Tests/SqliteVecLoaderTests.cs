using Melore.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace Melore.Tests;

public sealed class SqliteVecLoaderTests
{
    /// <summary>Windows で sqlite-vec が読めなくても例外にしない。</summary>
    [Fact]
    public void TryLoad_DoesNotThrow()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        // Act
        var exception = Record.Exception(() => SqliteVecLoader.TryLoad(connection));

        // Assert
        Assert.Null(exception);
    }
}
