namespace OscCommittee.Web.ViewModels.Shared;

/// <summary>
/// One entry in the main navigation. <paramref name="Controller"/> drives the active state;
/// <paramref name="ActivePath"/> pins it to a URL prefix when two items share a controller.
/// <paramref name="Soon"/> marks a destination that a later part will wire up.
/// </summary>
public sealed record NavItem(
    string Label,
    string Icon,
    string Href,
    string? Controller = null,
    bool Soon = false,
    string? Badge = null,
    string? ActivePath = null);
