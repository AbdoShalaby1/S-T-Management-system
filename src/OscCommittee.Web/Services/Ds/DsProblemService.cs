using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Integrations.Codeforces;
using OscCommittee.Web.Integrations.LeetCode;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Settings;

namespace OscCommittee.Web.Services.Ds;

/// <summary>
/// DS problem lifecycle (PLAN §8.5) plus points materialisation (§6.4). Difficulty is always the
/// board's choice; platform metadata is reference only.
/// </summary>
public sealed class DsProblemService(
    AppDbContext db,
    ISettingsService settings,
    ILeetCodeClient leetCode,
    CodeforcesProblemCache codeforces,
    TimeProvider timeProvider,
    ILogger<DsProblemService> logger) : IDsProblemService
{
    public const string TitleMessage = "Use a title between 3 and 200 characters.";
    public const string PointsRangeMessage = "Points override must be between 0 and 999.";
    public const string MissingSheetMessage = "That sheet no longer exists.";
    public const string LastProblemMessage =
        "This is the last problem on the current sheet. Set the sheet as past or hide it first.";

    public async Task<IReadOnlyList<DsProblemRow>> GetForSheetAsync(
        Guid dsSheetId,
        CancellationToken cancellationToken = default) =>
        await GetRowsAsync(dsSheetId, viewerUserId: null, cancellationToken);

    public async Task<IReadOnlyList<DsProblemRow>> GetForMemberAsync(
        Guid dsSheetId,
        Guid viewerUserId,
        CancellationToken cancellationToken = default) =>
        await GetRowsAsync(dsSheetId, viewerUserId, cancellationToken);

    public async Task<IReadOnlyList<DsSolverRow>> GetSolversAsync(
        Guid problemId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.DsSolves
            .AsNoTracking()
            .Where(solve => solve.DsProblemId == problemId)
            .OrderBy(solve => solve.SolvedAtUtc)
            .ThenBy(solve => solve.Id)
            .Select(solve => new
            {
                solve.UserId,
                DisplayName = db.Users
                    .Where(user => user.Id == solve.UserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault(),
                solve.SolvedAtUtc,
                solve.Language,
                solve.IsLate,
                solve.IsManual,
                solve.PlatformUrl,
                Points = db.PointEntries
                    .Where(entry => entry.Category == PointCategory.DsSolve && entry.SourceId == solve.Id)
                    .Select(entry => (int?)entry.FinalPoints)
                    .FirstOrDefault() ?? 0
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new DsSolverRow(
                row.UserId,
                string.IsNullOrWhiteSpace(row.DisplayName) ? "Removed account" : row.DisplayName,
                row.SolvedAtUtc,
                row.Language,
                row.Points,
                row.IsLate,
                row.IsManual,
                row.PlatformUrl))
            .ToList();
    }

    private async Task<IReadOnlyList<DsProblemRow>> GetRowsAsync(
        Guid dsSheetId,
        Guid? viewerUserId,
        CancellationToken cancellationToken)
    {
        var rows = await db.DsProblems
            .AsNoTracking()
            .Where(problem => problem.DsSheetId == dsSheetId)
            .OrderBy(problem => problem.SortOrder)
            .ThenBy(problem => problem.AssignedAtUtc)
            .ThenBy(problem => problem.Title)
            .Select(problem => new
            {
                problem.Id,
                problem.DsSheetId,
                SheetTitle = problem.DsSheet!.Title,
                problem.Platform,
                problem.ExternalId,
                problem.Title,
                problem.Url,
                problem.Difficulty,
                problem.PointsOverride,
                problem.Points,
                problem.Rating,
                problem.IsPaidOnly,
                problem.AssignedAtUtc,
                problem.SortOrder
            })
            .ToListAsync(cancellationToken);

        var solveCounts = await db.DsSolves
            .AsNoTracking()
            .Where(solve => solve.DsProblem!.DsSheetId == dsSheetId)
            .GroupBy(solve => solve.DsProblemId)
            .Select(group => new { ProblemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ProblemId, row => row.Count, cancellationToken);

        var solves = new Dictionary<Guid, DsSolveSummary>();

        if (viewerUserId is { } viewer)
        {
            // Keyed by the sheet problem: the same platform problem may sit on several sheets,
            // and each copy is credited separately, so each row shows its own solve.
            var mine = await db.DsSolves
                .AsNoTracking()
                .Where(solve => solve.UserId == viewer)
                .Select(solve => new
                {
                    solve.DsProblemId,
                    solve.SolvedAtUtc,
                    solve.Language,
                    solve.IsLate,
                    solve.IsManual,
                    solve.PlatformUrl,
                    Points = db.PointEntries
                        .Where(entry => entry.Category == PointCategory.DsSolve && entry.SourceId == solve.Id)
                        .Select(entry => (int?)entry.FinalPoints)
                        .FirstOrDefault() ?? 0
                })
                .ToListAsync(cancellationToken);

            foreach (var solve in mine)
            {
                solves[solve.DsProblemId] = new DsSolveSummary(
                    solve.DsProblemId,
                    solve.SolvedAtUtc,
                    solve.Language,
                    solve.IsLate,
                    solve.Points,
                    solve.PlatformUrl,
                    solve.IsManual);
            }
        }

        return rows.Select(row => new DsProblemRow(
            row.Id,
            row.DsSheetId,
            row.SheetTitle,
            row.Platform,
            row.ExternalId,
            row.Title,
            row.Url,
            row.Difficulty,
            row.PointsOverride,
            row.Points,
            row.Rating,
            row.IsPaidOnly,
            row.AssignedAtUtc,
            row.SortOrder,
            solveCounts.GetValueOrDefault(row.Id),
            solves.GetValueOrDefault(row.Id))).ToList();
    }

    public async Task<DsProblemRow?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sheetId = await db.DsProblems
            .AsNoTracking()
            .Where(problem => problem.Id == id)
            .Select(problem => (Guid?)problem.DsSheetId)
            .FirstOrDefaultAsync(cancellationToken);

        if (sheetId is null)
        {
            return null;
        }

        var rows = await GetForSheetAsync(sheetId.Value, cancellationToken);
        return rows.FirstOrDefault(row => row.Id == id);
    }

    public async Task<Result<DsProblemPreview>> PreviewAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        var parsed = DsProblemUrl.Parse(url);
        if (parsed.Failed || parsed.Value is null)
        {
            return Result.Fail<DsProblemPreview>(parsed.ErrorCode ?? "invalid_url", parsed.Message);
        }

        var link = parsed.Value;

        try
        {
            if (link.Platform == Platform.LeetCode)
            {
                var metadata = await leetCode.GetProblemAsync(link.ExternalId, cancellationToken);

                if (metadata.Failed || metadata.Value is null)
                {
                    return Result.Fail<DsProblemPreview>(
                        metadata.ErrorCode ?? "leetcode_unavailable",
                        metadata.Message);
                }

                var value = metadata.Value;
                var reference = value.DifficultyLabel is null
                    ? "LeetCode"
                    : $"LeetCode says {value.DifficultyLabel}";

                if (!string.IsNullOrWhiteSpace(value.FrontendId))
                {
                    reference += $" · #{value.FrontendId}";
                }

                return Result.Ok(new DsProblemPreview(
                    link.Platform,
                    link.ExternalId,
                    link.CanonicalUrl,
                    value.Title,
                    null,
                    value.IsPaidOnly,
                    reference,
                    null));
            }

            var problem = await codeforces.FindAsync(GetContestId(link.ExternalId), GetIndex(link.ExternalId), cancellationToken);

            if (problem is null)
            {
                return Result.Fail<DsProblemPreview>(
                    "not_found",
                    "Codeforces has no problem with that id. Check the contest and index.");
            }

            return Result.Ok(new DsProblemPreview(
                link.Platform,
                link.ExternalId,
                link.CanonicalUrl,
                problem.Name,
                problem.Rating,
                false,
                $"Codeforces rating {problem.Rating?.ToString(CultureInfo.InvariantCulture) ?? "unrated"}",
                null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The URL is valid, the platform just did not answer. Let the board type the title.
            logger.LogWarning(ex, "Metadata lookup failed for {Url}.", link.CanonicalUrl);

            return Result.Ok(new DsProblemPreview(
                link.Platform,
                link.ExternalId,
                link.CanonicalUrl,
                SuggestTitle(link),
                null,
                false,
                null,
                "The platform could not be reached. Fill in the title and points yourself; the sync will still match this problem."));
        }
    }

    public async Task<Result<Guid>> CreateAsync(
        DsProblemRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        if (Validate(request) is { } failure)
        {
            return Result.Fail<Guid>(failure.ErrorCode ?? "invalid", failure.Message);
        }

        var sheet = await db.DsSheets
            .AsNoTracking()
            .Where(row => row.Id == request.DsSheetId)
            .Select(row => new { row.Id, row.MonthSheetId, row.Title, row.CreatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (sheet is null)
        {
            return Result.Fail<Guid>("sheet_missing", MissingSheetMessage);
        }

        var duplicate = await FindDuplicateAsync(
            request.DsSheetId,
            request.Platform,
            request.ExternalId,
            Guid.Empty,
            cancellationToken);

        if (duplicate is not null)
        {
            return Result.Fail<Guid>("duplicate", duplicate);
        }

        var presets = await settings.GetPointsPresetsAsync(cancellationToken);
        var sortOrder = await db.DsProblems
            .Where(problem => problem.DsSheetId == sheet.Id)
            .Select(problem => (int?)problem.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var problem = new DsProblem
        {
            DsSheetId = sheet.Id,
            Platform = request.Platform,
            ExternalId = request.ExternalId.Trim(),
            Title = request.Title.Trim(),
            Url = CanonicalUrl(request),
            Difficulty = request.Difficulty,
            PointsOverride = request.PointsOverride,
            Points = request.PointsOverride ?? presets.For(request.Difficulty),
            Rating = request.Rating,
            IsPaidOnly = request.IsPaidOnly,
            // The sheet is the assignment, not the row: boards publish the sheet and then type its
            // problems in, so stamping "now" here rejected solves made minutes before the typing.
            AssignedAtUtc = sheet.CreatedAtUtc == default ? timeProvider.GetUtcNow() : sheet.CreatedAtUtc,
            SortOrder = sortOrder + 1
        };

        db.DsProblems.Add(problem);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Adding problem {ExternalId} to sheet {SheetId} failed.", problem.ExternalId, sheet.Id);
            return Result.Fail<Guid>(
                "duplicate",
                "That problem was added to this sheet by somebody else. Reload the sheet and try again.");
        }

        return Result.Ok(
            problem.Id,
            $"“{problem.Title}” was added to “{sheet.Title}” for {problem.Points} points.");
    }

    public async Task<Result> DeleteAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var problem = await db.DsProblems.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (problem is null)
        {
            return Result.Fail("not_found", "That problem no longer exists.");
        }

        // Solve history is not disposable: hide the sheet instead of erasing earned points.
        if (await db.DsSolves.AnyAsync(solve => solve.DsProblemId == id, cancellationToken))
        {
            return Result.Fail(
                "has_solves",
                "This problem already has credited solves. Hide the sheet instead of deleting it.");
        }

        if (await IsLastProblemOnActiveSheetAsync(problem.DsSheetId, cancellationToken))
        {
            return Result.Fail("empty_sheet", LastProblemMessage);
        }

        db.DsProblems.Remove(problem);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // The foreign key also refuses the delete; answer with the friendly reason.
            logger.LogWarning(ex, "Deleting DS problem {ProblemId} was refused by the database.", id);
            return Result.Fail(
                "has_solves",
                "This problem already has credited solves. Hide the sheet instead of deleting it.");
        }

        return Result.Ok($"“{problem.Title}” was removed from the sheet.");
    }

    public async Task<Result> MoveAsync(
        Guid id,
        int direction,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        if (direction is not (-1 or 1))
        {
            return Result.Fail("invalid_move", "Move a problem one place at a time.");
        }

        var problem = await db.DsProblems.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (problem is null)
        {
            return Result.Fail("not_found", "That problem no longer exists.");
        }

        var ordered = await db.DsProblems
            .Where(row => row.DsSheetId == problem.DsSheetId)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.AssignedAtUtc)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var index = ordered.FindIndex(row => row.Id == problem.Id);
        var target = index + direction;

        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return Result.Ok("That problem is already at the edge of the sheet.");
        }

        var neighbour = ordered[target];

        // Rebuild the whole sequence: gaps or duplicate sort orders from older edits must not
        // make a swap a no-op.
        (ordered[index], ordered[target]) = (neighbour, problem);

        for (var position = 0; position < ordered.Count; position++)
        {
            ordered[position].SortOrder = position + 1;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok($"“{problem.Title}” moved {(direction < 0 ? "up" : "down")}.");
    }

    private Result? Validate(DsProblemRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 3 or > 200)
        {
            return Result.Fail("invalid_title", TitleMessage);
        }

        if (string.IsNullOrWhiteSpace(request.ExternalId))
        {
            return Result.Fail("invalid_external", "Import the problem from its URL first.");
        }

        if (request.PointsOverride is < 0 or > 999)
        {
            return Result.Fail("invalid_points", PointsRangeMessage);
        }

        if (DsProblemUrl.Canonical(request.Platform, request.ExternalId).Length == 0)
        {
            return Result.Fail("invalid_external", "That platform id is not valid.");
        }

        return null;
    }

    /// <summary>
    /// True when the sheet is active and holds just this one problem, so deleting or moving it
    /// would leave an active sheet with nothing to solve.
    /// </summary>
    private async Task<bool> IsLastProblemOnActiveSheetAsync(
        Guid sheetId,
        CancellationToken cancellationToken)
    {
        var active = await db.DsSheets
            .AsNoTracking()
            .Where(sheet => sheet.Id == sheetId)
            .Select(sheet => sheet.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        return active
            && await db.DsProblems.CountAsync(
                problem => problem.DsSheetId == sheetId,
                cancellationToken) == 1;
    }

    private async Task<string?> FindDuplicateAsync(
        Guid sheetId,
        Platform platform,
        string externalId,
        Guid ignoreId,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeExternalId(platform, externalId);

        var sameSheet = await db.DsProblems
            .AsNoTracking()
            .Where(problem => problem.DsSheetId == sheetId
                && problem.Platform == platform
                && problem.Id != ignoreId)
            .Select(problem => new { problem.ExternalId, problem.Title })
            .ToListAsync(cancellationToken);

        var onSheet = sameSheet.FirstOrDefault(row =>
            NormalizeExternalId(platform, row.ExternalId) == normalized);

        // The same problem may appear on several sheets (and several months); only a second copy
        // inside one sheet is a mistake.
        return onSheet is null ? null : $"“{onSheet.Title}” is already on this sheet.";
    }

    private static string NormalizeExternalId(Platform platform, string externalId)
    {
        var trimmed = externalId.Trim();

        if (platform == Platform.LeetCode)
        {
            return trimmed.ToLowerInvariant();
        }

        var parts = trimmed.Split(':');
        return parts.Length == 2 ? $"{parts[0]}:{parts[1].ToUpperInvariant()}" : trimmed.ToUpperInvariant();
    }

    private static string CanonicalUrl(DsProblemRequest request) =>
        DsProblemUrl.Canonical(request.Platform, request.ExternalId);

    private static int GetContestId(string externalId)
    {
        var parts = externalId.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var id) ? id : 0;
    }

    private static string GetIndex(string externalId)
    {
        var parts = externalId.Split(':');
        return parts.Length == 2 ? parts[1] : externalId;
    }

    private static string SuggestTitle(DsProblemLink link)
    {
        if (link.Platform != Platform.LeetCode)
        {
            return $"Codeforces {link.ExternalId}";
        }

        var words = string.Join(' ', link.ExternalId.Split('-', StringSplitOptions.RemoveEmptyEntries));

        return words.Trim().Length >= 3 ? TitleCase(words) : link.ExternalId;
    }

    private static string TitleCase(string words) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words);
}