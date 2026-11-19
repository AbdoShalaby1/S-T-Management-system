namespace OscCommittee.Web.Integrations;

/// <summary>Network limits from the <c>Sync</c> configuration section (PLAN §17).</summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>Codeforces documents "one request every two seconds"; enforced process-wide.</summary>
    public int CodeforcesMinIntervalMs { get; set; } = 2000;

    public int ProblemsetCacheHours { get; set; } = 24;

    public int LeetCodeTimeoutSeconds { get; set; } = 20;

    public int CodeforcesTimeoutSeconds { get; set; } = 60;
}