using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;

namespace OscCommittee.Web.Services.Tasks;

/// <summary>
/// Owns C# task lifecycle. Mirrors <c>DsSheetService</c>: one active task per month, hidden implies
/// inactive, direct UPDATE for the swap so the filtered unique index never sees two active rows.
/// </summary>
public sealed class CSharpTaskService(
    AppDbContext db,
    IMonthSheetService months,
    TimeProvider timeProvider) : ICSharpTaskService
{
    public const string TitleLengthMessage = "Use a title between 3 and 120 characters.";
    public const string DueInPastMessage = "Pick a deadline in the future.";
    public const string HiddenActiveMessage = "A hidden task cannot be current. Unhide it first.";
    public const string ChangedMessage = "Somebody else changed the tasks for that month. Reload and try again.";

    public async Task<IReadOnlyList<CSharpTaskRow>> GetBoardListAsync(
        CancellationToken cancellationToken = default) =>
        await QueryAsync(visibleOnly: false, cancellationToken);

    public async Task<IReadOnlyList<CSharpTaskRow>> GetMemberListAsync(
        CancellationToken cancellationToken = default) =>
        await QueryAsync(visibleOnly: true, cancellationToken);

    public async Task<CSharpTaskDetail?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await db.CSharpTasks
            .AsNoTracking()
            .Where(task => task.Id == id)
            .Select(task => new
            {
                task.Id,
                task.MonthSheetId,
                task.Title,
                task.DescriptionMd,
                task.MonthSheet!.Year,
                task.MonthSheet.Month,
                task.DueAtUtc,
                task.IsActive,
                task.IsHidden,
                task.StarterZipPath,
                task.StarterZipFileName,
                task.StarterZipSizeBytes,
                task.ReferenceFolderPath,
                task.ReferenceFileCount,
                task.CreatedAtUtc,
                CreatedByName = db.Users
                    .Where(user => user.Id == task.CreatedByUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ToDetail(row.Id, row.MonthSheetId, row.Title, row.DescriptionMd,
            row.Year, row.Month, row.DueAtUtc, row.IsActive, row.IsHidden,
            row.StarterZipPath, row.StarterZipFileName, row.StarterZipSizeBytes,
            row.ReferenceFolderPath, row.ReferenceFileCount,
            row.CreatedAtUtc, row.CreatedByName);
    }

    public async Task<CSharpTaskDetail?> GetVisibleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var task = await FindAsync(id, cancellationToken);
        return task is { IsHidden: false } ? task : null;
    }

    public async Task<Result<Guid>> CreateAsync(
        CSharpTaskRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        if (Validate(request, requireFuture: true) is { } failure)
        {
            return Result.Fail<Guid>(failure.ErrorCode ?? "invalid", failure.Message);
        }

        var month = await months.FindAsync(request.MonthSheetId, cancellationToken);
        if (month is null)
        {
            return Result.Fail<Guid>("month_missing", "That month no longer exists.");
        }

        var starter = NormalizeStarter(
            request.StarterZipPath,
            request.StarterZipFileName,
            request.StarterZipSizeBytes);

        var task = new CSharpTask
        {
            MonthSheetId = month.Id,
            Title = request.Title.Trim(),
            DescriptionMd = NormalizeMarkdown(request.DescriptionMd),
            DueAtUtc = request.DueAtUtc,
            StarterZipPath = starter.Path,
            StarterZipFileName = starter.FileName,
            StarterZipSizeBytes = starter.SizeBytes,
            ReferenceFolderPath = NormalizeFolderPath(request.ReferenceFolderPath),
            ReferenceFileCount = request.ReferenceFileCount > 0 ? request.ReferenceFileCount : 0,
            IsHidden = request.IsHidden,
            IsActive = request.IsActive,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            CreatedByUserId = boardUserId
        };

        // Read the outgoing title before the direct UPDATE clears the flag.
        var previous = task.IsActive
            ? await ActiveTitleAsync(month.Id, Guid.Empty, cancellationToken)
            : null;

        if (task.IsActive)
        {
            await DeactivateOthersAsync(month.Id, Guid.Empty, cancellationToken);
        }

        db.CSharpTasks.Add(task);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail<Guid>("changed", ChangedMessage);
        }

        var message = $"“{task.Title}” was created.";
        if (previous is not null)
        {
            message += $" It is now the current task for that month, and “{previous}” was set as past.";
        }

        return Result.Ok(task.Id, message);
    }

    public async Task<Result> UpdateAsync(
        Guid id,
        CSharpTaskRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var task = await db.CSharpTasks.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (task is null)
        {
            return Result.Fail("not_found", "That task no longer exists.");
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

        if (request.IsActive)
        {
            await DeactivateOthersAsync(month.Id, task.Id, cancellationToken);
        }

        task.MonthSheetId = month.Id;
        task.Title = request.Title.Trim();
        task.DescriptionMd = NormalizeMarkdown(request.DescriptionMd);
        task.DueAtUtc = request.DueAtUtc;
        var starter = NormalizeStarter(
            request.StarterZipPath,
            request.StarterZipFileName,
            request.StarterZipSizeBytes);
        task.StarterZipPath = starter.Path;
        task.StarterZipFileName = starter.FileName;
        task.StarterZipSizeBytes = starter.SizeBytes;
        task.ReferenceFolderPath = NormalizeFolderPath(request.ReferenceFolderPath);
        task.ReferenceFileCount = request.ReferenceFileCount > 0 ? request.ReferenceFileCount : 0;
        task.IsHidden = request.IsHidden;
        task.IsActive = request.IsActive;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail("changed", ChangedMessage);
        }

        return Result.Ok($"“{task.Title}” is saved.");
    }

    public async Task<Result> ActivateAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var task = await db.CSharpTasks.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (task is null)
        {
            return Result.Fail("not_found", "That task no longer exists.");
        }

        if (task.IsHidden)
        {
            return Result.Fail("hidden", HiddenActiveMessage);
        }

        if (task.IsActive)
        {
            return Result.Ok($"“{task.Title}” is already current.");
        }

        var previous = await ActiveTitleAsync(task.MonthSheetId, task.Id, cancellationToken);

        await DeactivateOthersAsync(task.MonthSheetId, task.Id, cancellationToken);
        task.IsActive = true;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail("changed", ChangedMessage);
        }

        return previous is null
            ? Result.Ok($"“{task.Title}” is now the current task.")
            : Result.Ok($"“{task.Title}” is current. “{previous}” was set as past.");
    }

    public async Task<Result> DeactivateAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var task = await db.CSharpTasks.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (task is null)
        {
            return Result.Fail("not_found", "That task no longer exists.");
        }

        if (!task.IsActive)
        {
            return Result.Ok($"“{task.Title}” is not current.");
        }

        task.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok($"“{task.Title}” is no longer current.");
    }

    public async Task<Result> SetHiddenAsync(
        Guid id,
        bool hidden,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var task = await db.CSharpTasks.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (task is null)
        {
            return Result.Fail("not_found", "That task no longer exists.");
        }

        if (task.IsHidden == hidden)
        {
            return Result.Ok(hidden
                ? $"“{task.Title}” is already hidden."
                : $"“{task.Title}” is visible to members.");
        }

        var wasActive = task.IsActive;
        task.IsHidden = hidden;

        // A hidden task cannot be featured; leave nothing in an impossible state.
        if (hidden)
        {
            task.IsActive = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        return hidden
            ? Result.Ok(wasActive
                ? $"“{task.Title}” is hidden and no longer current. Existing work is untouched."
                : $"“{task.Title}” is hidden. Existing work is untouched.")
            : Result.Ok($"“{task.Title}” is visible to members again.");
    }

    private async Task<List<CSharpTaskRow>> QueryAsync(
        bool visibleOnly,
        CancellationToken cancellationToken)
    {
        var query = db.CSharpTasks.AsNoTracking();

        if (visibleOnly)
        {
            query = query.Where(task => !task.IsHidden);
        }

        var rows = await query
            .OrderByDescending(task => task.MonthSheet!.Year)
            .ThenByDescending(task => task.MonthSheet!.Month)
            .ThenByDescending(task => task.IsActive)
            .ThenBy(task => task.CreatedAtUtc)
            .Select(task => new
            {
                task.Id,
                task.MonthSheetId,
                task.Title,
                task.MonthSheet!.Year,
                task.MonthSheet.Month,
                task.DueAtUtc,
                task.IsActive,
                task.IsHidden,
                HasReference = task.ReferenceFolderPath != null && task.ReferenceFolderPath != "",
                task.ReferenceFileCount,
                task.CreatedAtUtc,
                CreatedByName = db.Users
                    .Where(user => user.Id == task.CreatedByUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CSharpTaskRow(
                row.Id,
                row.MonthSheetId,
                row.Title,
                row.Year,
                row.Month,
                MonthLabels.Format(row.Year, row.Month),
                row.DueAtUtc,
                row.IsActive,
                row.IsHidden,
                row.HasReference,
                row.ReferenceFileCount,
                row.CreatedAtUtc,
                row.CreatedByName))
            .ToList();
    }

    /// <summary>Title of the month's currently active task, ignoring <paramref name="keepId"/>.</summary>
    private Task<string?> ActiveTitleAsync(
        Guid monthSheetId,
        Guid keepId,
        CancellationToken cancellationToken) =>
        db.CSharpTasks
            .Where(task => task.MonthSheetId == monthSheetId
                && task.IsActive
                && task.Id != keepId)
            .Select(task => task.Title)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Clears the month's other active task with a direct UPDATE so the filtered unique index never
    /// catches two active rows, even between two tracked saves.
    /// </summary>
    private Task<int> DeactivateOthersAsync(
        Guid monthSheetId,
        Guid keepId,
        CancellationToken cancellationToken) =>
        db.CSharpTasks
            .Where(task => task.MonthSheetId == monthSheetId
                && task.IsActive
                && task.Id != keepId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(task => task.IsActive, false),
                cancellationToken);

    private Result? Validate(CSharpTaskRequest request, bool requireFuture)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 3 or > 120)
        {
            return Result.Fail("invalid_title", TitleLengthMessage);
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

    private static CSharpTaskDetail ToDetail(
        Guid id,
        Guid monthSheetId,
        string title,
        string? descriptionMd,
        int year,
        int month,
        DateTimeOffset dueAtUtc,
        bool isActive,
        bool isHidden,
        string? starterZipPath,
        string? starterZipFileName,
        long starterZipSizeBytes,
        string? referenceFolderPath,
        int referenceFileCount,
        DateTimeOffset createdAtUtc,
        string? createdByName) => new(
            id,
            monthSheetId,
            title,
            descriptionMd,
            year,
            month,
            MonthLabels.Format(year, month),
            dueAtUtc,
            isActive,
            isHidden,
            starterZipPath,
            starterZipFileName,
            starterZipSizeBytes,
            referenceFolderPath,
            referenceFileCount,
            createdAtUtc,
            createdByName);

    private static string? NormalizeMarkdown(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? null : markdown.Replace("\r\n", "\n").Trim();

    private static string? NormalizeFolderPath(string? folderPath) =>
        string.IsNullOrWhiteSpace(folderPath) ? null : folderPath.Replace('\\', '/').Trim().Trim('/');

    /// <summary>A starting-code triple only exists together: path, display name and size.</summary>
    private static (string? Path, string? FileName, long SizeBytes) NormalizeStarter(
        string? path,
        string? fileName,
        long sizeBytes)
    {
        var normalized = NormalizeFolderPath(path);
        if (normalized is null)
        {
            return (null, null, 0);
        }

        var name = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim();
        return (normalized, name, Math.Max(0, sizeBytes));
    }
}