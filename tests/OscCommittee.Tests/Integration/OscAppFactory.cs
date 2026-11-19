using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Data;
using OscCommittee.Web.Data.Seed;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Integrations;
using OscCommittee.Web.Integrations.Codeforces;
using OscCommittee.Web.Integrations.LeetCode;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;
using OscCommittee.Tests.Infrastructure;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// A real HTTP application over SQLite with the network clients, background workers and startup
/// seeders swapped out. Everything else (routing, Identity, policies, antiforgery) is the app.
/// </summary>
public sealed class OscAppFactory : WebApplicationFactory<Program>
{
    public const string BoardEmail = "board@test.local";
    public const string BoardPassword = "Board12345";
    public const string MemberPassword = "Member12345";

    private readonly string _root;
    private readonly string _dbPath;
    private readonly string _uploadsRoot;
    private readonly IGeminiClient _gemini;
    private bool _initialized;

    public OscAppFactory()
        : this(new StaticGeminiClient(GeminiCallResult.Failed("grading not used", retryable: false)))
    {
    }

    public OscAppFactory(IGeminiClient gemini)
    {
        _gemini = gemini;
        _root = Path.Combine(Path.GetTempPath(), "osc-committee-tests", Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_root, "tests.db");
        _uploadsRoot = Path.Combine(_root, "uploads");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_uploadsRoot);
    }

    public string UploadsRoot => _uploadsRoot;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        // Starts the host. Program's seeders are no-ops, so nothing touches the database yet.
        using (CreateClient())
        {
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { Roles.Board, Roles.Member })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
            }
        }

        await CreateBoardUserAsync(scope);
    }

    public async Task<ApplicationUser> CreateUserAsync(
        string email,
        string displayName,
        UserStatus status = UserStatus.Active,
        bool board = false,
        string password = MemberPassword)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            DisplayName = displayName,
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            Status = status,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = status == UserStatus.Active ? DateTimeOffset.UtcNow : null
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join(" ", created.Errors.Select(error => error.Description)));
        }

        await users.AddToRoleAsync(user, Roles.Member);
        if (board)
        {
            await users.AddToRoleAsync(user, Roles.Board);
        }

        return user;
    }

    public async Task<Guid> CreateTaskAsync(
        string title,
        DateTimeOffset? dueAtUtc = null,
        bool hidden = false,
        string? descriptionMd = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var months = scope.ServiceProvider.GetRequiredService<IMonthSheetService>();
        var month = await months.GetOrCreateCurrentAsync();

        var task = new CSharpTask
        {
            MonthSheetId = month.Id,
            Title = title,
            DescriptionMd = descriptionMd ?? "## Task\n\nWrite a console application.",
            DueAtUtc = dueAtUtc ?? DateTimeOffset.UtcNow.AddDays(7),
            IsHidden = hidden,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.CSharpTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    public async Task<Guid> CreateSubmissionAsync(Guid taskId, Guid userId, string fileName = "Program.cs")
    {
        using var scope = Services.CreateScope();
        var submissions = scope.ServiceProvider.GetRequiredService<ICSharpSubmissionService>();
        var content = "using System;\n\nConsole.WriteLine(\"Hello, world!\");\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        var result = await submissions.SubmitAsync(
            taskId,
            userId,
            [new SubmissionInputFile(fileName, bytes.Length, _ => Task.FromResult<Stream>(new MemoryStream(bytes)))]);

        if (result.Failed)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.Value!.SubmissionId;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(IsDbContextRegistration).ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options
                .UseSqlite($"Data Source={_dbPath}")
                .ReplaceService<IModelCustomizer, SqliteModelCustomizer>());

            // The workers would hit the network or race the tests; each test drives its own.
            foreach (var descriptor in services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType is { } type
                    && (type == typeof(GradingWorker)
                        || type == typeof(MonthRolloverService)
                        || type == typeof(CodeforcesCacheRefreshService)))
                .ToList())
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<IBoardAccountSeeder>();
            services.AddScoped<IBoardAccountSeeder, NoOpBoardSeeder>();
            services.RemoveAll<ISettingsSeeder>();
            services.AddScoped<ISettingsSeeder, NoOpSettingsSeeder>();

            services.RemoveAll<ILeetCodeClient>();
            services.AddSingleton<ILeetCodeClient, EmptyLeetCodeClient>();
            services.RemoveAll<ICodeforcesClient>();
            services.AddSingleton<ICodeforcesClient, EmptyCodeforcesClient>();
            services.RemoveAll<IGeminiClient>();
            services.AddSingleton(_gemini);

            services.Configure<AppOptions>(options => options.TimeZone = "Africa/Cairo");
            services.Configure<UploadsOptions>(options => options.Root = _uploadsRoot);

            // The test server speaks plain HTTP; the production cookie is HTTPS-only.
            services.ConfigureApplicationCookie(options =>
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
            services.Configure<AiOptions>(options =>
            {
                options.ApiKey = "test-key";
                options.Model = "gemini-test";
                options.RetryDelaysSeconds = [0, 0];
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // SQLite pools connections per file; without this the temp database stays locked.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(() => Directory.Delete(_root, recursive: true));
    }

    private async Task CreateBoardUserAsync(IServiceScope scope)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(BoardEmail) is not null)
        {
            return;
        }

        var board = new ApplicationUser
        {
            DisplayName = "Board Member",
            Email = BoardEmail,
            UserName = BoardEmail,
            EmailConfirmed = true,
            Status = UserStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await users.CreateAsync(board, BoardPassword);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join(" ", created.Errors.Select(error => error.Description)));
        }

        await users.AddToRoleAsync(board, Roles.Member);
        await users.AddToRoleAsync(board, Roles.Board);
    }

    private static bool IsDbContextRegistration(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType == typeof(AppDbContext)
            || descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>))
        {
            return true;
        }

        return descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition().Name
                .StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal);
    }

    private static void TryDelete(Action action)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class NoOpBoardSeeder : IBoardAccountSeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpSettingsSeeder : ISettingsSeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyLeetCodeClient : ILeetCodeClient
    {
        public Task<Result<LeetCodeProblemMetadata>> GetProblemAsync(
            string slug,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Fail<LeetCodeProblemMetadata>("unavailable", "Not used in tests."));

        public Task<Result<IReadOnlyList<LeetCodeSubmission>>> GetRecentAcceptedAsync(
            string username,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<LeetCodeSubmission>>([]));

        public Task<Result<PlatformHandle>> CheckHandleAsync(
            string username,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Fail<PlatformHandle>("unavailable", "Not used in tests."));
    }

    private sealed class EmptyCodeforcesClient : ICodeforcesClient
    {
        public Task<Result<IReadOnlyList<CodeforcesProblem>>> GetProblemsetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<CodeforcesProblem>>([]));

        public Task<Result<IReadOnlyList<CodeforcesSubmission>>> GetSubmissionsAsync(
            string handle,
            int count,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<CodeforcesSubmission>>([]));

        public Task<Result<PlatformHandle>> CheckHandleAsync(
            string handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Fail<PlatformHandle>("unavailable", "Not used in tests."));
    }
}

/// <summary>Answers every call with the same result; useful when grading must not run.</summary>
public sealed class StaticGeminiClient(GeminiCallResult result) : IGeminiClient
{
    public int Calls { get; private set; }

    public Task<GeminiCallResult> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(result);
    }
}

/// <summary>Plays back a fixed sequence, repeating the last answer after it runs out.</summary>
public sealed class QueueGeminiClient(params GeminiCallResult[] results) : IGeminiClient
{
    public int Calls { get; private set; }

    public Task<GeminiCallResult> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var index = Math.Min(Calls, results.Length - 1);
        Calls++;
        return Task.FromResult(results[index]);
    }
}

/// <summary>xUnit class fixture that owns one initialized application.</summary>
public sealed class OscAppFixture : IAsyncLifetime
{
    public OscAppFactory App { get; } = new();

    public Task InitializeAsync() => App.InitializeAsync();

    public async Task DisposeAsync() => await App.DisposeAsync();
}