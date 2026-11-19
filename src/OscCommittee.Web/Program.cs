using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Data.Seed;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Integrations;
using OscCommittee.Web.Integrations.Codeforces;
using OscCommittee.Web.Integrations.LeetCode;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Content;
using OscCommittee.Web.Services.Ds;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Points;
using OscCommittee.Web.Services.Settings;
using OscCommittee.Web.Services.Standings;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// LeetCode sits behind Cloudflare; a browser-like agent plus Referer is the documented mitigation.
const string BrowserUserAgent =
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

var dataRoot = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(Path.Combine(dataRoot, "submissions"));
Directory.CreateDirectory(Path.Combine(dataRoot, "logs"));

builder.Host.UseSerilog((_, _, configuration) => configuration
    .MinimumLevel.Is(builder.Environment.IsDevelopment() ? LogEventLevel.Debug : LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(dataRoot, "logs", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<UploadsOptions>(builder.Configuration.GetSection(UploadsOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITimeZoneService, TimeZoneService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.EnableRetryOnFailure(3)));

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        // Min 8 characters with at least one letter and one digit (the letter rule is
        // also enforced on the registration view model).
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 10;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/error/403";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.ReturnUrlParameter = "returnUrl";
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.AddAuthorization(options =>
{
    // Active status is re-read from the database on every request that uses this policy,
    // so deactivating somebody ends their access immediately.
    options.AddPolicy("Member", policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ActiveUserRequirement()));

    options.AddPolicy("Board", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(Roles.Board)
        .AddRequirements(new ActiveUserRequirement()));

    // Settings is the member profile page: handles, display name and password. Board accounts
    // change nothing there, so the policy keeps them out even if they type the URL.
    options.AddPolicy("Settings", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => !context.User.IsInRole(Roles.Board))
        .AddRequirements(new ActiveUserRequirement()));
});

builder.Services.AddScoped<IAuthorizationHandler, ActiveUserAuthorizationHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IBoardAccountSeeder, BoardAccountSeeder>();
builder.Services.AddScoped<IMemberAdminService, MemberAdminService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IMonthSheetService, MonthSheetService>();
builder.Services.AddScoped<IDsSheetService, DsSheetService>();
builder.Services.AddScoped<IDsProblemService, DsProblemService>();
builder.Services.AddScoped<IDsCreditService, DsCreditService>();
builder.Services.AddScoped<IDsSyncService, DsSyncService>();
builder.Services.AddScoped<IPointsService, PointsService>();
builder.Services.AddScoped<IStandingsService, StandingsService>();
builder.Services.AddScoped<ICSharpTaskService, CSharpTaskService>();
builder.Services.AddScoped<ICSharpSubmissionService, CSharpSubmissionService>();
builder.Services.AddScoped<IBoardSubmissionService, BoardSubmissionService>();
builder.Services.AddScoped<ISubmissionFileService, SubmissionFileService>();
builder.Services.AddScoped<IReferenceFileService, ReferenceFileService>();
builder.Services.AddScoped<IStarterZipService, StarterZipService>();
builder.Services.AddSingleton<IMarkdownBundleService, MarkdownBundleService>();
builder.Services.AddSingleton<IGradingQueue, GradingQueue>();
builder.Services.AddScoped<IPromptBuilder, PromptBuilder>();
builder.Services.AddScoped<IGradingRunner, GradingRunner>();
builder.Services.AddScoped<ICSharpCreditService, CSharpCreditService>();
builder.Services.AddHostedService<GradingWorker>();
builder.Services.AddSingleton<DsSyncGate>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ISettingsSeeder, SettingsSeeder>();
builder.Services.AddScoped<IContentPageService, ContentPageService>();
builder.Services.AddScoped<IContentImageService, ContentImageService>();
builder.Services.AddSingleton<IMarkdownService, MarkdownService>();
builder.Services.AddHostedService<MonthRolloverService>();

builder.Services.AddHttpClient<ILeetCodeClient, LeetCodeClient>(LeetCodeClient.HttpClientName, client =>
    {
        client.BaseAddress = new Uri("https://leetcode.com/");
        client.Timeout = TimeSpan.FromSeconds(
            builder.Configuration.GetValue("Sync:LeetCodeTimeoutSeconds", 20));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        client.DefaultRequestHeaders.Referrer = new Uri("https://leetcode.com");
    });

builder.Services.AddHttpClient<ICodeforcesClient, CodeforcesClient>(CodeforcesClient.HttpClientName, client =>
    {
        client.BaseAddress = new Uri("https://codeforces.com/");
        client.Timeout = TimeSpan.FromSeconds(
            builder.Configuration.GetValue("Sync:CodeforcesTimeoutSeconds", 60));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
    });

builder.Services.AddHttpClient<IGeminiClient, GeminiClient>(GeminiClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue("Ai:TimeoutSeconds", 120));
});

builder.Services.AddSingleton<CodeforcesThrottle>();
builder.Services.AddSingleton<CodeforcesProblemCache>();
builder.Services.AddHostedService<CodeforcesCacheRefreshService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));

    options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));

    // Uploads are capped per signed-in member (PLAN §15), not per shared campus IP.
    options.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));
});

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["db"]);

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    await ApplyMigrationsAsync(app);
}
else
{
    app.UseExceptionHandler("/error/500");
    app.UseHsts();
    app.UseHttpsRedirection();
}

await SeedBoardAsync(app);
await SeedSettingsAsync(app);

app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<AccountStateMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

await app.RunAsync();

static string ClientKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static async Task ApplyMigrationsAsync(WebApplication application)
{
    using var scope = application.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database migrations failed.");
        throw;
    }
}

static async Task SeedBoardAsync(WebApplication application)
{
    using var scope = application.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await scope.ServiceProvider.GetRequiredService<IBoardAccountSeeder>().SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Board account seeding failed.");
        throw;
    }
}

static async Task SeedSettingsAsync(WebApplication application)
{
    using var scope = application.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await scope.ServiceProvider.GetRequiredService<ISettingsSeeder>().SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Settings seeding failed.");
        throw;
    }
}

public partial class Program;
