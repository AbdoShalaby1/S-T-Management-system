namespace OscCommittee.Web.Data.Seed;

/// <summary>Seeds the editable setting keys once, without ever overwriting board changes.</summary>
public interface ISettingsSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
