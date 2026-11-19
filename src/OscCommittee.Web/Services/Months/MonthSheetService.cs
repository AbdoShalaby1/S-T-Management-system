using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Months;

/// <summary>
/// Owns the month sheet lifecycle. Lazy creation is safe because the unique
/// <c>(Year, Month)</c> index backs the retry in <see cref="GetOrCreateAsync"/>.
/// </summary>
public sealed class MonthSheetService(
    AppDbContext db,
    ITimeZoneService timeZone,
    TimeProvider timeProvider) : IMonthSheetService
{
    public Task<MonthSheet> GetOrCreateCurrentAsync(CancellationToken cancellationToken = default)
    {
        var local = timeZone.NowLocal();
        return GetOrCreateAsync(local.Year, local.Month, cancellationToken);
    }

    public async Task<MonthSheet> GetOrCreateAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        var existing = await db.MonthSheets
            .FirstOrDefaultAsync(sheet => sheet.Year == year && sheet.Month == month, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var sheet = new MonthSheet
        {
            Year = year,
            Month = month,
            Status = MonthStatus.Open,
            OpenedAtUtc = timeProvider.GetUtcNow()
        };

        db.MonthSheets.Add(sheet);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return sheet;
        }
        catch (DbUpdateException)
        {
            // Another request created the same month between our read and write.
            db.Entry(sheet).State = EntityState.Detached;

            return await db.MonthSheets
                .FirstAsync(row => row.Year == year && row.Month == month, cancellationToken);
        }
    }

    public Task<MonthSheet?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.MonthSheets.FirstOrDefaultAsync(sheet => sheet.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MonthSheetSummary>> GetListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await db.MonthSheets
            .AsNoTracking()
            .OrderByDescending(sheet => sheet.Year)
            .ThenByDescending(sheet => sheet.Month)
            .Select(sheet => new
            {
                sheet.Id,
                sheet.Year,
                sheet.Month,
                sheet.Status,
                sheet.OpenedAtUtc,
                sheet.ClosedAtUtc,
                ClosedByName = db.Users
                    .Where(user => user.Id == sheet.ClosedByUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new MonthSheetSummary(
                row.Id,
                row.Year,
                row.Month,
                MonthLabels.Format(row.Year, row.Month),
                row.Status,
                row.OpenedAtUtc,
                row.ClosedAtUtc,
                row.ClosedByName))
            .ToList();
    }

    public async Task<Result> CloseAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await FindAsync(id, cancellationToken);
        if (sheet is null)
        {
            return Result.Fail("not_found", "That month no longer exists.");
        }

        if (sheet.Status == MonthStatus.Closed)
        {
            return Result.Ok($"{MonthLabels.Format(sheet.Year, sheet.Month)} is already closed.");
        }

        sheet.Status = MonthStatus.Closed;
        sheet.ClosedAtUtc = timeProvider.GetUtcNow();
        sheet.ClosedByUserId = boardUserId;

        if (!await SaveAsync(sheet, cancellationToken))
        {
            return Result.Fail("changed", "Somebody else changed that month. Reload and try again.");
        }

        return Result.Ok($"{MonthLabels.Format(sheet.Year, sheet.Month)} is closed.");
    }

    public async Task<Result> OpenAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await FindAsync(id, cancellationToken);
        if (sheet is null)
        {
            return Result.Fail("not_found", "That month no longer exists.");
        }

        if (sheet.Status == MonthStatus.Open)
        {
            return Result.Ok($"{MonthLabels.Format(sheet.Year, sheet.Month)} is already open.");
        }

        sheet.Status = MonthStatus.Open;
        sheet.ClosedAtUtc = null;
        sheet.ClosedByUserId = null;

        if (!await SaveAsync(sheet, cancellationToken))
        {
            return Result.Fail("changed", "Somebody else changed that month. Reload and try again.");
        }

        return Result.Ok($"{MonthLabels.Format(sheet.Year, sheet.Month)} is open again.");
    }

    public async Task<int> CloseElapsedAsync(CancellationToken cancellationToken = default)
    {
        var local = timeZone.NowLocal();
        var now = timeProvider.GetUtcNow();

        var elapsed = await db.MonthSheets
            .Where(sheet => sheet.Status == MonthStatus.Open
                && (sheet.Year < local.Year || (sheet.Year == local.Year && sheet.Month < local.Month)))
            .ToListAsync(cancellationToken);

        if (elapsed.Count == 0)
        {
            return 0;
        }

        foreach (var sheet in elapsed)
        {
            sheet.Status = MonthStatus.Closed;
            sheet.ClosedAtUtc = now;
            sheet.ClosedByUserId = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        return elapsed.Count;
    }

    private async Task<bool> SaveAsync(MonthSheet sheet, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
