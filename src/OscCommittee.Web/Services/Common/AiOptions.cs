namespace OscCommittee.Web.Services.Common;

/// <summary>Gemini settings from the <c>Ai</c> section (PLAN §17). Grading stays off while empty.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    public double Temperature { get; set; } = 0.2;

    /// <summary>Backoff before each automatic retry (PLAN §10.3: 5 s, then 20 s).</summary>
    public List<int> RetryDelaysSeconds { get; set; } = [5, 20];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}