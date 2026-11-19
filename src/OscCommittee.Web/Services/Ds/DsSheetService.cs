using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;

namespace OscCommittee.Web.Services.Ds;

/// <summary>
/// Owns DS sheet lifecycle. The one-active-per-month rule is enforced here (not only by the
/// filtered index).
/// </summary>
public sealed class DsSheetService(
    AppDbContext db,
    IMonthSheetService months,
    TimeProvider timeProvider) : IDsSheetService
{
    public const string TitleLengthMessage = "Use a title between 3 and 120 characters.";
    public const string DueInPastMessage = "Pick a deadline in the future.";
    public const string HiddenActiveMessage = "A hidden sheet cannot be current. Unhide it first.";
    public const string EmptyActiveMessage = "Add at least one problem before making a sheet current.";
    public const string ChangedMessage = "Somebody else changed the sheets for that month. Reload and try again.";

    public async Task<IReadOnlyList<DsSheetRow>> GetBoardListAsync(
        CancellationToken cancellationToken = default) =>
        await QueryAsync(visibleOnly: false, cancellationToken);

    public async Task<DsSheetRow?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(visibleOnly: false, cancellationToken);
        return rows.FirstOrDefault(row => row.Id == id);
    }

    public async Task<Result<Guid>> CreateAsync(
        DsSheetRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        if (Validate(request, requireFuture: true) is { } failure)
        {
            return Result.Fail<Guid>(failure.ErrorCode ?? "invalid", failure.Message);
        }

        if (request.IsActive)
        {
            return Result.Fail<Guid>(
                "empty_sheet",
                "A new sheet has no problems yet. Create it as a draft and make it current once its problems are in.");
        }

        var month = await months.FindAsync(request.MonthSheetId, cancellationToken);
        if (month is null)
        {
            return Result.Fail<Guid>("month_missing", "That month no longer exists.");
        }

        var sheet = new DsSheet
        {
            MonthSheetId = month.Id,
            Title = request.Title.Trim(),
            DescriptionMd = NormalizeDescription(request.DescriptionMd),
            DueAtUtc = request.DueAtUtc,
            IsHidden = request.IsHidden,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            CreatedByUserId = boardUserId
        };

        // Read the outgoing title before the direct UPDATE clears the flag.
        var previous = sheet.IsActive
            ? await ActiveTitleAsync(month.Id, Guid.Empty, cancellationToken)
            : null;

        // A filtered unique index allows one active row per month, so the old one has to go
        // first. This runs as a direct UPDATE: the retry strategy forbids user transactions,
        // and the worst case is a saved sheet with no active sibling, which is a legal state.
        if (sheet.IsActive)
        {
            await DeactivateOthersAsync(month.Id, Guid.Empty, cancellationToken);
        }

        db.DsSheets.Add(sheet);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail<Guid>("changed", ChangedMessage);
        }

        var message = $"“{sheet.Title}” was created.";
        if (previous is not null)
        {
            message += $" It is now the current sheet for that month, and “{previous}” was set as past.";
        }

        return Result.Ok(sheet.Id, message);
    }

    public async Task<Result> UpdateAsync(
        Guid id,
        DsSheetRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.DsSheets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (sheet is null)
        {
            return Result.Fail("not_found", "That sheet no longer exists.");
        }

        if (Validate(request, requireFuture: false) is { } failure)
        {
            return failure;
        }

        var month = await months.FindAsync(request.MonthSheetId, cancellationToken);
        if (month is null)
        {
            return Result.Fail("month_missing", "That month no longer exists.");
        }

        if (request.IsActive
            && !await db.DsProblems.AnyAsync(problem => problem.DsSheetId == id, cancellationToken))
        {
            return Result.Fail("empty_sheet", EmptyActiveMessage);
        }

        var title = request.Title.Trim();
        var description = NormalizeDescription(request.DescriptionMd);

        if (request.IsActive)
        {
            await DeactivateOthersAsync(month.Id, sheet.Id, cancellationToken);
        }

        sheet.MonthSheetId = month.Id;
        sheet.Title = title;
        sheet.DescriptionMd = description;
        sheet.DueAtUtc = request.DueAtUtc;
        sheet.IsHidden = request.IsHidden;
        sheet.IsActive = request.IsActive;
        sheet.SortOrder = request.SortOrder;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail("changed", ChangedMessage);
        }

        return Result.Ok($"“{sheet.Title}” is saved.");
    }

    public async Task<Result> ActivateAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.DsSheets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (sheet is null)
        {
            return Result.Fail("not_found", "That sheet no longer exists.");
        }

        if (sheet.IsHidden)
        {
            return Result.Fail("hidden", HiddenActiveMessage);
        }

        if (!await db.DsProblems.AnyAsync(problem => problem.DsSheetId == id, cancellationToken))
        {
            return Result.Fail("empty_sheet", EmptyActiveMessage);
        }

        if (sheet.IsActive)
        {
            return Result.Ok($"“{sheet.Title}” is already current.");
        }

        var previous = await ActiveTitleAsync(sheet.MonthSheetId, sheet.Id, cancellationToken);

        await DeactivateOthersAsync(sheet.MonthSheetId, sheet.Id, cancellationToken);
        sheet.IsActive = true;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail("changed", ChangedMessage);
        }

        return previous is null
            ? Result.Ok($"“{sheet.Title}” is now the current sheet.")
            : Result.Ok($"“{sheet.Title}” is current. “{previous}” was set as past.");
    }

    public async Task<Result> DeactivateAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.DsSheets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (sheet is null)
        {
            return Result.Fail("not_found", "That sheet no longer exists.");
        }

        if (!sheet.IsActive)
        {
            return Result.Ok($"“{sheet.Title}” is not current.");
        }

        sheet.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok($"“{sheet.Title}” is no longer current.");
    }

    public async Task<Result> SetHiddenAsync(
        Guid id,
        bool hidden,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.DsSheets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (sheet is null)
        {
            return Result.Fail("not_found", "That sheet no longer exists.");
        }

        if (sheet.IsHidden == hidden)
        {
            return Result.Ok(hidden
                ? $"“{sheet.Title}” is already hidden."
                : $"“{sheet.Title}” is visible to members.");
        }

        var wasActive = sheet.IsActive;
        sheet.IsHidden = hidden;

        // A hidden sheet cannot be featured; leave nothing in an impossible state.
        if (hidden)
        {
            sheet.IsActive = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        return hidden
            ? Result.Ok(wasActive
                ? $"“{sheet.Title}” is hidden and no longer current. Existing work is untouched."
                : $"“{sheet.Title}” is hidden. Existing work is untouched.")
            : Result.Ok($"“{sheet.Title}” is visible to members again.");
    }

    public async Task<IReadOnlyList<DsSheetGroup>> GetMemberGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(visibleOnly: true, cancellationToken);

        return rows
            .GroupBy(row => new { row.Year, row.Month })
            .OrderByDescending(group => group.Key.Year)
            .ThenByDescending(group => group.Key.Month)
            .Select(group => new DsSheetGroup(
                group.Key.Year,
                group.Key.Month,
                MonthLabels.Format(group.Key.Year, group.Key.Month),
                group
                    .OrderByDescending(row => row.IsActive)
                    .ThenBy(row => row.SortOrder)
                    .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
    }

    public async Task<DsSheetRow?> GetVisibleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(visibleOnly: true, cancellationToken);
        return rows.FirstOrDefault(row => row.Id == id);
    }

    private async Task<List<DsSheetRow>> QueryAsync(
        bool visibleOnly,
        CancellationToken cancellationToken)
    {
        var query = db.DsSheets.AsNoTracking();

        if (visibleOnly)
        {
            query = query.Where(sheet => !sheet.IsHidden);
        }

        var rows = await query
            .OrderByDescending(sheet => sheet.MonthSheet!.Year)
            .ThenByDescending(sheet => sheet.MonthSheet!.Month)
            .ThenByDescending(sheet => sheet.IsActive)
            .ThenBy(sheet => sheet.SortOrder)
            .ThenBy(sheet => sheet.CreatedAtUtc)
            .Select(sheet => new
            {
                sheet.Id,
                sheet.Title,
                sheet.DescriptionMd,
                sheet.MonthSheetId,
                sheet.MonthSheet!.Year,
                sheet.MonthSheet.Month,
                sheet.DueAtUtc,
                sheet.IsActive,
                sheet.IsHidden,
                sheet.SortOrder,
                sheet.CreatedAtUtc,
                CreatedByName = db.Users
                    .Where(user => user.Id == sheet.CreatedByUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault(),
                ProblemCount = db.DsProblems.Count(problem => problem.DsSheetId == sheet.Id)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new DsSheetRow(
                row.Id,
                row.Title,
                row.DescriptionMd,
                row.MonthSheetId,
                row.Year,
                row.Month,
                MonthLabels.Format(row.Year, row.Month),
                row.DueAtUtc,
                row.IsActive,
                row.IsHidden,
                row.SortOrder,
                row.CreatedAtUtc,
                row.CreatedByName,
                row.ProblemCount))
            .ToList();
    }

    /// <summary>Title of the month's currently active sheet, ignoring <paramref name="keepId"/>.</summary>
    private Task<string?> ActiveTitleAsync(
        Guid monthSheetId,
        Guid keepId,
        CancellationToken cancellationToken) =>
        db.DsSheets
            .Where(sheet => sheet.MonthSheetId == monthSheetId
                && sheet.IsActive
                && sheet.Id != keepId)
            .Select(sheet => sheet.Title)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Clears the month's other active sheet with a direct UPDATE, so the filtered unique index
    /// never sees two active rows, even for the moment between two tracked saves.
    /// </summary>
    private Task<int> DeactivateOthersAsync(
        Guid monthSheetId,
        Guid keepId,
        CancellationToken cancellationToken) =>
        db.DsSheets
            .Where(sheet => sheet.MonthSheetId == monthSheetId
                && sheet.IsActive
                && sheet.Id != keepId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(sheet => sheet.IsActive, false),
                cancellationToken);

    private Result? Validate(DsSheetRequest request, bool requireFuture)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 3 or > 120)
        {
            return Result.Fail("invalid_title", TitleLengthMessage);
        }

        if (request.SortOrder is < 0 or > 999)
        {
            return Result.Fail("invalid_sort", "Sort order must be between 0 and 999.");
        }

        if (request.IsHidden && request.IsActive)
        {
            return Result.Fail("hidden_active", HiddenActiveMessage);
        }

        if (requireFuture && request.DueAtUtc <= timeProvider.GetUtcNow())
        {
            return Result.Fail("due_in_past", DueInPastMessage);
        }

        return null;
    }

    private static string? NormalizeDescription(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? null : markdown.Replace("\r\n", "\n").Trim();
}