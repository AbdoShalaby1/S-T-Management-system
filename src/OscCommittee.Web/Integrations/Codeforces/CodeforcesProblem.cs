namespace OscCommittee.Web.Integrations.Codeforces;

/// <summary>One row of <c>problemset.problems</c>, reduced to what the app shows.</summary>
public sealed record CodeforcesProblem(
    int ContestId,
    string Index,
    string Name,
    int? Rating,
    IReadOnlyList<string> Tags)
{
    /// <summary>Key shared with <see cref="Domain.Entities.DsProblem.ExternalId"/>.</summary>
    public string ExternalId => $"{ContestId}:{Index}";
}