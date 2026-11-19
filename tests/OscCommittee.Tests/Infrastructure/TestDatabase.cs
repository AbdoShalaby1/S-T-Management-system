using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OscCommittee.Web.Data;

namespace OscCommittee.Tests.Infrastructure;

/// <summary>
/// One open SQLite in-memory database shared by every context created here. The connection has to
/// stay open or SQLite throws the schema away.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    private TestDatabase(SqliteConnection connection, DbContextOptions<AppDbContext> options)
    {
        _connection = connection;
        _options = options;
        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>The context owned by the test, reused so change tracking behaves like production.</summary>
    public AppDbContext Context { get; }

    public static TestDatabase Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .EnableSensitiveDataLogging()
            .Options;

        return new TestDatabase(connection, options);
    }

    /// <summary>A second context over the same database, for reads that must not see tracked copies.</summary>
    public AppDbContext NewContext() => new(_options);

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}