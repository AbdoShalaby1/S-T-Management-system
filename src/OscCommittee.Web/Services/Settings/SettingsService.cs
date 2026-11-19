using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Services.Settings;

/// <summary>Easy/Medium/Hard point presets, stored as three <c>Setting</c> rows (PLAN §6.4).</summary>
public sealed record PointsPresets(int Easy, int Medium, int Hard)
{
    public static readonly PointsPresets Default = new(1, 2, 3);

    public int For(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => Easy,
        Difficulty.Medium => Medium,
        Difficulty.Hard => Hard,
        _ => Medium
    };
}

public interface ISettingsService
{
    Task<PointsPresets> GetPointsPresetsAsync(CancellationToken cancellationToken = default);
}

public sealed class SettingsService(
    AppDbContext db,
    ILogger<SettingsService> logger) : ISettingsService
{
    private const string EasyKey = "Points.Easy";
    private const string MediumKey = "Points.Medium";
    private const string HardKey = "Points.Hard";

    public async Task<PointsPresets> GetPointsPresetsAsync(CancellationToken cancellationToken = default)
    {
        var values = await db.Settings
            .AsNoTracking()
            .Where(setting => setting.Key == EasyKey || setting.Key == MediumKey || setting.Key == HardKey)
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        return new PointsPresets(
            Parse(values, EasyKey, PointsPresets.Default.Easy),
            Parse(values, MediumKey, PointsPresets.Default.Medium),
            Parse(values, HardKey, PointsPresets.Default.Hard));
    }

    private int Parse(IReadOnlyDictionary<string, string> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value) || value is < 0 or > 999)
        {
            logger.LogWarning("Setting {Key} is missing or invalid; using {Fallback}.", key, fallback);
            return fallback;
        }

        return value;
    }
}
