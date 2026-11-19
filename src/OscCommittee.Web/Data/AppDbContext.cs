using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<MonthSheet> MonthSheets => Set<MonthSheet>();

    public DbSet<DsSheet> DsSheets => Set<DsSheet>();

    public DbSet<DsProblem> DsProblems => Set<DsProblem>();

    public DbSet<CSharpTask> CSharpTasks => Set<CSharpTask>();

    public DbSet<CSharpSubmission> CSharpSubmissions => Set<CSharpSubmission>();

    public DbSet<DsSolve> DsSolves => Set<DsSolve>();

    public DbSet<PointEntry> PointEntries => Set<PointEntry>();

    public DbSet<SyncState> SyncStates => Set<SyncState>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<ContentPage> ContentPages => Set<ContentPage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
