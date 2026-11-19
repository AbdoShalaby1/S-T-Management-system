using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Seed;

/// <summary>
/// Copies configuration values into <see cref="Setting"/> rows the first time each key is seen
/// (PLAN §17.1). Values already present are left alone so board edits survive restarts.
/// </summary>
public sealed class SettingsSeeder(
    AppDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SettingsSeeder> logger) : ISettingsSeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Points.Easy"] = configuration["Points:Easy"] ?? "1",
            ["Points.Medium"] = configuration["Points:Medium"] ?? "2",
            ["Points.Hard"] = configuration["Points:Hard"] ?? "3",
            ["Grading.Prompt"] = DefaultGradingPrompt.Text,
            ["Grading.PromptVersion"] = "1",
            ["Grading.MemberKnowledge"] = string.Empty,
            ["Uploads.MaxFiles"] = configuration["Uploads:MaxFiles"] ?? "300"
        };

        var existing = await db.Settings
            .Select(setting => setting.Key)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var added = 0;

        foreach (var (key, value) in defaults)
        {
            if (existing.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            db.Settings.Add(new Setting
            {
                Key = key,
                Value = value,
                UpdatedAtUtc = now
            });

            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} setting rows.", added);
        }
    }
}
