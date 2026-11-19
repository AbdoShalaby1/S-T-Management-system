namespace OscCommittee.Web.Integrations;

/// <summary>A handle as the platform knows it. Used by the advisory Verify buttons.</summary>
public sealed record PlatformHandle(string Handle, string? DisplayName);