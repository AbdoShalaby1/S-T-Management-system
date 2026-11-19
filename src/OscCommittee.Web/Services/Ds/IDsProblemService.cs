using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Ds;

/// <summary>A credited solve, reduced to what the member sheet shows.</summary>
public sealed record DsSolveSummary(
    Guid ProblemId,
    DateTimeOffset SolvedAtUtc,
    string? Language,
    bool IsLate,
    int Points,
    string PlatformUrl,
    bool IsManual);

/// <summary>One problem as the board and members see it.</summary>
public sealed record DsProblemRow(
    Guid Id,
    Guid DsSheetId,
    string SheetTitle,
    Platform Platform,
    string ExternalId,
    string Title,
    string Url,
    Difficulty Difficulty,
    int? PointsOverride,
    int Points,
    int? Rating,
    bool IsPaidOnly,
    DateTimeOffset AssignedAtUtc,
    int SortOrder,
    int SolveCount,
    DsSolveSummary? Solve = null)
{
    public bool IsSolved => Solve is not null;
}

/// <summary>A solver row on the board's per-problem page.</summary>
public sealed record DsSolverRow(
    Guid UserId,
    string DisplayName,
    DateTimeOffset SolvedAtUtc,
    string? Language,
    int Points,
    bool IsLate,
    bool IsManual,
    string PlatformUrl);

/// <summary>What the board submits after (or instead of) a metadata import.</summary>
public sealed record DsProblemRequest(
    Guid DsSheetId,
    Platform Platform,
    string ExternalId,
    string Title,
    Difficulty Difficulty,
    int? PointsOverride,
    int? Rating,
    bool IsPaidOnly);

/// <summary>Metadata resolved from a pasted URL. <paramref name="Warning"/> is set when the
/// platform could not be reached but the URL itself was valid.</summary>
public sealed record DsProblemPreview(
    Platform Platform,
    string ExternalId,
    string CanonicalUrl,
    string Title,
    int? Rating,
    bool IsPaidOnly,
    string? Reference,
    string? Warning);

/// <summary>DS problem rules from PLAN §8.5, §6.4 and §6.6.</summary>
public interface IDsProblemService
{
    Task<IReadOnlyList<DsProblemRow>> GetForSheetAsync(
        Guid dsSheetId,
        CancellationToken cancellationToken = default);

    /// <summary>The same sheet, with the viewer's own solves attached to each problem.</summary>
    Task<IReadOnlyList<DsProblemRow>> GetForMemberAsync(
        Guid dsSheetId,
        Guid viewerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Who solved a problem, when, in what language, and for how many points.</summary>
    Task<IReadOnlyList<DsSolverRow>> GetSolversAsync(
        Guid problemId,
        CancellationToken cancellationToken = default);

    Task<DsProblemRow?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Parses the URL and asks the platform for metadata. Never throws on network failure.</summary>
    Task<Result<DsProblemPreview>> PreviewAsync(
        string? url,
        CancellationToken cancellationToken = default);

    Task<Result<Guid>> CreateAsync(
        DsProblemRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a problem one place up or down inside its sheet.</summary>
    Task<Result> MoveAsync(
        Guid id,
        int direction,
        Guid boardUserId,
        CancellationToken cancellationToken = default);
}