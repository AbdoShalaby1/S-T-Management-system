namespace OscCommittee.Web.Services.Accounts;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public BoardSeedOptions Board { get; set; } = new();
}

public sealed class BoardSeedOptions
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(DisplayName);
}
