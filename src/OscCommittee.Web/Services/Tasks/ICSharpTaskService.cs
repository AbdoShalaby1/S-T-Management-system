using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Tasks;

/// <summary>Read-only facts about one C# task, used by every list.</summary>
public sealed record CSharpTaskRow(
    Guid Id,
    Guid MonthSheetId,
    string Title,
    int Year,
    int Month,
    string MonthLabel,
    DateTimeOffset DueAtUtc,
    bool IsActive,
    bool IsHidden,
    bool HasReference,
    int ReferenceFileCount,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByName);

/// <summary>The full task, including the statement and the board-only fields.</summary>
public sealed record CSharpTaskDetail(
    Guid Id,
    Guid MonthSheetId,
    string Title,
    string? DescriptionMd,
    int Year,
    int Month,
    string MonthLabel,
    DateTimeOffset DueAtUtc,
    bool IsActive,
    bool IsHidden,
    string? StarterZipPath,
    string? StarterZipFileName,
    long StarterZipSizeBytes,
    string? ReferenceFolderPath,
    int ReferenceFileCount,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByName);

/// <summary>The editable fields. The controller converts local deadlines to UTC first.</summary>
public sealed record CSharpTaskRequest(
    string Title,
    string? DescriptionMd,
    Guid MonthSheetId,
    DateTimeOffset DueAtUtc,
    string? StarterZipPath,
    string? StarterZipFileName,
    long StarterZipSizeBytes,
    string? ReferenceFolderPath,
    int ReferenceFileCount,
    bool IsHidden,
    bool IsActive);

/// <summary>
/// C# task lifecycle (PLAN §7.3 and §8.6). The one-active-per-month rule lives here, exactly like
/// DS sheets, and every mutation writes an audit row.
/// </summary>
public interface ICSharpTaskService
{
    /// <summary>Points a perfect submission is worth; the grader maps its 1–5 score onto this.</summary>
    const int MaxPoints = SubmissionScoring.MaxPoints;

    Task<IReadOnlyList<CSharpTaskRow>> GetBoardListAsync(CancellationToken cancellationToken = default);

    /// <summary>Every task a member can see, newest month first.</summary>
    Task<IReadOnlyList<CSharpTaskRow>> GetMemberListAsync(CancellationToken cancellationToken = default);

    Task<CSharpTaskDetail?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The task only if a member is allowed to see it; hidden tasks resolve to null.</summary>
    Task<CSharpTaskDetail?> GetVisibleAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Guid>> CreateAsync(
        CSharpTaskRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        Guid id,
        CSharpTaskRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    Task<Result> ActivateAsync(Guid id, Guid boardUserId, CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(Guid id, Guid boardUserId, CancellationToken cancellationToken = default);

    Task<Result> SetHiddenAsync(
        Guid id,
        bool hidden,
        Guid boardUserId,
        CancellationToken cancellationToken = default);
}