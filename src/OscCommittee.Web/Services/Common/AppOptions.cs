namespace OscCommittee.Web.Services.Common;

public sealed class AppOptions
{
    public const string SectionName = "App";

    public string TimeZone { get; set; } = "Africa/Cairo";

    public string Name { get; set; } = "OSC Science & Technology";
}
