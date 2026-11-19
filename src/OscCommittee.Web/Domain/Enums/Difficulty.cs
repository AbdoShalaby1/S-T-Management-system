namespace OscCommittee.Web.Domain.Enums;

/// <summary>
/// Human-chosen difficulty (PLAN §8.5). Never inferred from a rating: the platform's own label
/// and the Codeforces rating are shown as reference only.
/// </summary>
public enum Difficulty
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}