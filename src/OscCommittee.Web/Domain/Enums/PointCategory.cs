namespace OscCommittee.Web.Domain.Enums;

/// <summary>Where a point entry came from (PLAN §6.4).</summary>
public enum PointCategory
{
    DsSolve = 0,
    CSharpSubmission = 1,
    Bonus = 2,
    Adjustment = 3
}