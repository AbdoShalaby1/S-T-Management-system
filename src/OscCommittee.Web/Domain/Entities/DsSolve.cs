using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// One accepted submission credited to a member (PLAN §11). <see cref="Platform"/> and
/// <see cref="ExternalId"/> are denormalised so the global "once per problem, ever" rule is a
/// single unique index rather than a join.
/// </summary>
public sealed class DsSolve
{
    public DsSolve()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid DsProblemId { get; set; }

    public Platform Platform { get; set; }

    /// <summary>Normalised platform id, matching the copy on <see cref="DsProblem.ExternalId"/>.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Platform submission id, or <c>manual:{guid}</c> for a board credit.</summary>
    public string PlatformSubmissionId { get; set; } = string.Empty;

    public DateTimeOffset SolvedAtUtc { get; set; }

    public string? Language { get; set; }

    /// <summary>Submission page when the platform exposes one, otherwise the problem URL.</summary>
    public string PlatformUrl { get; set; } = string.Empty;

    /// <summary>Accepted after the owning sheet's deadline; still recorded, worth 0 points.</summary>
    public bool IsLate { get; set; }

    public bool IsManual { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ApplicationUser? User { get; set; }

    public DsProblem? DsProblem { get; set; }
}