namespace OscCommittee.Web.Domain.Enums;

/// <summary>Where a DS problem lives (PLAN §11). Used for dedupe and for the sync clients.</summary>
public enum Platform
{
    LeetCode = 0,
    Codeforces = 1
}