namespace OscCommittee.Web.Domain.Enums;

/// <summary>
/// Account lifecycle state (PLAN §5). This is the source of truth for access;
/// the Identity role only decides whether the board area is reachable.
/// </summary>
public enum UserStatus
{
    Pending = 0,
    Active = 1,
    Rejected = 2,
    Deactivated = 3
}
