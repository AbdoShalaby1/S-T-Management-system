namespace OscCommittee.Tests.Infrastructure;

/// <summary>A clock the test owns, so month boundaries and deadlines are deterministic.</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}