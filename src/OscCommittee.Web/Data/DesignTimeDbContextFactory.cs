using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OscCommittee.Web.Data;

/// <summary>
/// Lets the EF Core tooling build the context without starting the web host,
/// so migrations can be added while the app is not running.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ResolveProjectDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }

    private static string ResolveProjectDirectory()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        if (File.Exists(Path.Combine(current.FullName, "appsettings.json")))
        {
            return current.FullName;
        }

        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        while (probe is not null && !File.Exists(Path.Combine(probe.FullName, "appsettings.json")))
        {
            probe = probe.Parent;
        }

        return probe?.FullName ?? current.FullName;
    }
}
