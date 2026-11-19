using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Services.Standings;

/// <summary>Which column the table ranks by (PLAN §9).</summary>
public enum StandingsTab
{
    All = 0,
    Ds = 1,
    Tasks = 2
}

/// <summary>The two views a member can open.</summary>
public enum StandingsView
{
    Summary = 0,
    Detail = 1
}

/// <summary>Parsing and wording for the query-string switches, kept next to the rules.</summary>
public static class StandingsQuery
{
    public static StandingsTab ParseTab(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ds" => StandingsTab.Ds,
        "tasks" => StandingsTab.Tasks,
        _ => StandingsTab.All
    };

    public static string TabSlug(StandingsTab tab) => tab switch
    {
        StandingsTab.Ds => "ds",
        StandingsTab.Tasks => "tasks",
        _ => "all"
    };

    public static string TabLabel(StandingsTab tab) => tab switch
    {
        StandingsTab.Ds => "DS",
        StandingsTab.Tasks => "Tasks",
        _ => "All"
    };

    public static StandingsView ParseView(string? value) =>
        string.Equals(value?.Trim(), "detail", StringComparison.OrdinalIgnoreCase)
            ? StandingsView.Detail
            : StandingsView.Summary;

    public static string ViewSlug(StandingsView view) =>
        view == StandingsView.Detail ? "detail" : "summary";

    public static string ViewLabel(StandingsView view) =>
        view == StandingsView.Detail ? "Detailed" : "Summary";
}

/// <summary>One ledger line reduced to what a standing needs.</summary>
public sealed record StandingPoint(PointCategory Category, int FinalPoints);

/// <summary>Category totals and solved counts for one member in one month.</summary>
public sealed record StandingsTally(
    int DsPoints,
    int TaskPoints,
    int BonusPoints,
    int DsSolved,
    int TasksSolved)
{
    public static StandingsTally Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>Bonus and adjustments share one column so the columns add up to the total.</summary>
    public int Total => DsPoints + TaskPoints + BonusPoints;

    public int PointsFor(StandingsTab tab) => tab switch
    {
        StandingsTab.Ds => DsPoints,
        StandingsTab.Tasks => TaskPoints,
        _ => Total
    };
}

/// <summary>A member with their month totals, before ranking.</summary>
public sealed record StandingsCandidate(
    Guid UserId,
    string DisplayName,
    StandingsTally Totals,
    DateTimeOffset RegisteredAtUtc);

/// <summary>A member with the rank the selected tab gives them.</summary>
public sealed record RankedStanding(
    Guid UserId,
    string DisplayName,
    int Rank,
    StandingsTally Totals,
    int SortPoints);

/// <summary>
/// Pure standings rules: category sums, eligibility and score-only ranking (PLAN §9).
/// Totals are always derived from each ledger row's <c>FinalPoints</c> — never cached.
/// </summary>
public static class StandingsRules
{
    /// <summary>
    /// A late DS solve is still a solve (it just earns 0), while a C# task counts as solved only
    /// when it earned points.
    /// </summary>
    public static StandingsTally Tally(IEnumerable<StandingPoint> entries)
    {
        var dsPoints = 0;
        var taskPoints = 0;
        var bonusPoints = 0;
        var dsSolved = 0;
        var tasksSolved = 0;

        foreach (var entry in entries)
        {
            switch (entry.Category)
            {
                case PointCategory.DsSolve:
                    dsPoints += entry.FinalPoints;
                    dsSolved++;
                    break;

                case PointCategory.CSharpSubmission:
                    taskPoints += entry.FinalPoints;
                    if (entry.FinalPoints > 0)
                    {
                        tasksSolved++;
                    }

                    break;

                case PointCategory.Bonus:
                case PointCategory.Adjustment:
                    bonusPoints += entry.FinalPoints;
                    break;
            }
        }

        return new StandingsTally(dsPoints, taskPoints, bonusPoints, dsSolved, tasksSolved);
    }

    /// <summary>Active members always appear; deactivated ones only when the board asks.</summary>
    public static bool IsEligible(UserStatus status, bool includeDeactivated) =>
        status == UserStatus.Active
        || (includeDeactivated && status == UserStatus.Deactivated);

    /// <summary>
    /// Ranked on the selected score alone: no solve counts, no registration dates.
    /// Equal scores share a rank; names only keep the list stable between requests.
    /// </summary>
    public static IReadOnlyList<RankedStanding> Rank(
        IEnumerable<StandingsCandidate> candidates,
        StandingsTab tab)
    {
        var ordered = candidates
            .OrderByDescending(candidate => candidate.Totals.PointsFor(tab))
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.UserId)
            .ToList();

        var ranked = new List<RankedStanding>(ordered.Count);
        var rank = 0;
        int? previousPoints = null;

        for (var index = 0; index < ordered.Count; index++)
        {
            var candidate = ordered[index];
            var points = candidate.Totals.PointsFor(tab);

            if (points != previousPoints)
            {
                rank = index + 1;
                previousPoints = points;
            }

            ranked.Add(new RankedStanding(
                candidate.UserId,
                candidate.DisplayName,
                rank,
                candidate.Totals,
                points));
        }

        return ranked;
    }
}