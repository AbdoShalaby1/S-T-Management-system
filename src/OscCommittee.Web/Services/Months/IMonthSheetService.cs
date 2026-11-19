using System.Globalization;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Months;

/// <summary>Display facts about a month sheet, with the actor name already resolved.</summary>
public sealed record MonthSheetSummary(
    Guid Id,
    int Year,
    int Month,
    string Label,
    MonthStatus Status,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string? ClosedByName)
{
    public bool IsOpen => Status == MonthStatus.Open;
}

/// <summary>Month sheet rules from PLAN §6.1 and §8.4.</summary>
public interface IMonthSheetService
{
    /// <summary>The current local month, created on first access.</summary>
    Task<MonthSheet> GetOrCreateCurrentAsync(CancellationToken cancellationToken = default);

    Task<MonthSheet> GetOrCreateAsync(int year, int month, CancellationToken cancellationToken = default);

    Task<MonthSheet?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonthSheetSummary>> GetListAsync(CancellationToken cancellationToken = default);

    Task<Result> CloseAsync(Guid id, Guid boardUserId, CancellationToken cancellationToken = default);

    Task<Result> OpenAsync(Guid id, Guid boardUserId, CancellationToken cancellationToken = default);

    /// <summary>Closes every still-open sheet older than the current local month. Returns the count.</summary>
    Task<int> CloseElapsedAsync(CancellationToken cancellationToken = default);
}

/// <summary>One label format for every surface: <c>November 2026</c>.</summary>
public static class MonthLabels
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static string Format(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", Culture);
}
