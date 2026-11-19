namespace OscCommittee.Web.Services.Uploads;

/// <summary>Folder-upload limits from the <c>Uploads</c> section (PLAN §17).</summary>
public sealed class UploadsOptions
{
    public const string SectionName = "Uploads";

    /// <summary>Relative to the content root. Never under <c>wwwroot</c>.</summary>
    public string Root { get; set; } = "App_Data/submissions";

    public int MaxFiles { get; set; } = 300;

    public int MaxTotalMb { get; set; } = 30;

    public int MaxFileMb { get; set; } = 2;

    /// <summary>Case-insensitive extension allowlist, leading dot included.</summary>
    public List<string> AllowedExtensions { get; set; } = [".cs", ".csproj", ".sln"];

    /// <summary>Case-insensitive whole-name patterns; <c>*</c> is the only wildcard.</summary>
    public List<string> AllowedFileNames { get; set; } = ["README*"];

    /// <summary>Case-insensitive file-name patterns such as <c>appsettings*.json</c>.</summary>
    public List<string> AllowedNamePatterns { get; set; } = ["appsettings*.json"];

    public long MaxTotalBytes => MaxTotalMb * 1024L * 1024L;

    public long MaxFileBytes => MaxFileMb * 1024L * 1024L;
}