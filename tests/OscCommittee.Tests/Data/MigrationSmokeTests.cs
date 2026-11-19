using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;

namespace OscCommittee.Tests.Data;

/// <summary>
/// PLAN §18: the SQLite tests prove the model shape, but only SQL Server can prove the migrations.
/// The snapshot check needs no server; the apply check uses LocalDB when it is installed and is a
/// no-op otherwise (build agents without LocalDB still run the rest of the suite).
/// </summary>
public sealed class MigrationSmokeTests
{
    [Fact]
    public async Task The_model_matches_the_latest_migration_snapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=OscCommittee_ModelOnly;")
            .Options;

        await using var db = new AppDbContext(options);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "A model change is missing a migration. Run `dotnet ef migrations add`.");
    }

    [Fact]
    public async Task Migrations_apply_to_a_fresh_localdb_database()
    {
        if (!await LocalDbIsAvailableAsync())
        {
            return;
        }

        var database = "OscCommittee_MigrationSmoke_" + Guid.NewGuid().ToString("N");
        var connectionString = $"""
            Server=(localdb)\MSSQLLocalDB;Database={database};Trusted_Connection=True;
            MultipleActiveResultSets=True;TrustServerCertificate=True
            """;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new AppDbContext(options);

        try
        {
            await db.Database.MigrateAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            // Identity plus the app schema; a cheap probe that the tables really exist.
            Assert.False(await db.Users.AnyAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<bool> LocalDbIsAvailableAsync()
    {
        try
        {
            await using var connection = new SqlConnection(
                "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;Connect Timeout=3");
            await connection.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            // LocalDB is a developer-machine feature; CI can run the rest of the suite without it.
            return false;
        }
    }
}

