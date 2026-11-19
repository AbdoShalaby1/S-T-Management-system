namespace OscCommittee.Web.Services.Common;

/// <summary>
/// Every timestamp is stored in UTC; every timestamp is shown in <see cref="AppOptions.TimeZone"/>.
/// </summary>
public interface ITimeZoneService
{
    TimeZoneInfo TimeZone { get; }

    DateTimeOffset UtcNow { get; }

    DateTimeOffset ToLocal(DateTimeOffset utc);

    /// <summary>Treats a wall-clock value as app-local and returns the matching UTC instant.</summary>
    DateTimeOffset FromLocal(DateTime local);

    DateTimeOffset NowLocal();

    /// <summary>Label for the current local month, e.g. <c>November 2026</c>.</summary>
    string CurrentMonthLabel();

    /// <summary>Label for the month a UTC timestamp falls into, in the app time zone.</summary>
    string MonthLabel(DateTimeOffset utc);

    string FormatLocal(DateTimeOffset utc, string format);
}
