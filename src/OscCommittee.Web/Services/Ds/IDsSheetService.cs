using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Ds;

/// <summary>One sheet with its month already resolved and the creator name looked up.</summary>
public sealed record DsSheetRow(
    Guid Id,
    string Title,
    string? DescriptionMd,
    Guid MonthSheetId,
    int Year,
    int Month,
    string MonthLabel,
    DateTimeOffset DueAtUtc,
    bool IsActive,
    bool IsHidden,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByName,
    int ProblemCount);

/// <summary>Visible sheets for one month, used by the member overview.</summary>
public sealed record DsSheetGroup(
    int Year,
    int Month,
    string Label,
    IReadOnlyList<DsSheetRow> Sheets);

/// <summary>The editable fields of a sheet. The controller turns the local deadline into UTC.</summary>
public sealed record DsSheetRequest(
    string Title,
    string? DescriptionMd,
    Guid MonthSheetId,
    DateTimeOffset DueAtUtc,
    bool IsHidden,
    bool IsActive,
    int SortOrder);

/// <summary>DS sheet rules from PLAN §6.2, §6.3 and §8.5.</summary>
public interface IDsSheetService
{
    Task<IReadOnlyList<DsSheetRow>> GetBoardListAsync(CancellationToken cancellationToken = default);

    Task<DsSheetRow?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Guid>> CreateAsync(
        DsSheetRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        Guid id,
        DsSheetRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Activates this sheet and deactivates the month's previous active sheet in one step.</summary>
    Task<Result> ActivateAsync(Guid id, Guid boardUserId, CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(Guid id, Guid boardUserId, CancellationToken cancellationToken = default);

    /// <summary>Hide/unhide. Hiding also deactivates, because a hidden sheet cannot be featured.</summary>
    Task<Result> SetHiddenAsync(Guid id, bool hidden, Guid boardUserId, CancellationToken cancellationToken = default);

    /// <summary>Visible sheets grouped by month, newest month first; active sheet first inside its month.</summary>
    Task<IReadOnlyList<DsSheetGroup>> GetMemberGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>One visible sheet, or null when it does not exist or is a draft.</summary>
    Task<DsSheetRow?> GetVisibleAsync(Guid id, CancellationToken cancellationToken = default);
}