using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Services.Accounts;

public interface IBoardAccountSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Makes sure the <c>Board</c> and <c>Member</c> roles exist and that one board account
/// exists from configuration. Idempotent: it never touches an existing board account.
/// </summary>
public sealed class BoardAccountSeeder(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<SeedOptions> options,
    TimeProvider timeProvider,
    ILogger<BoardAccountSeeder> logger) : IBoardAccountSeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRoleAsync(Roles.Board);
        await EnsureRoleAsync(Roles.Member);

        var boardUsers = await userManager.GetUsersInRoleAsync(Roles.Board);
        if (boardUsers.Count > 0)
        {
            return;
        }

        var seed = options.Value.Board;
        if (!seed.IsConfigured)
        {
            logger.LogWarning(
                "No board account exists and Seed:Board is not configured. Set it through user-secrets or environment variables.");
            return;
        }

        var user = new ApplicationUser
        {
            DisplayName = seed.DisplayName.Trim(),
            Email = seed.Email.Trim(),
            UserName = seed.Email.Trim(),
            EmailConfirmed = true,
            Status = UserStatus.Active,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            ApprovedAtUtc = timeProvider.GetUtcNow()
        };

        var created = await userManager.CreateAsync(user, seed.Password);
        if (!created.Succeeded)
        {
            logger.LogError(
                "Seed:Board could not be created: {Errors}",
                string.Join(" ", created.Errors.Select(error => error.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, Roles.Board);
        await userManager.AddToRoleAsync(user, Roles.Member);

        logger.LogInformation("Seeded the first board account {UserId}.", user.Id);
    }

    private async Task EnsureRoleAsync(string name)
    {
        if (await roleManager.RoleExistsAsync(name))
        {
            return;
        }

        var created = await roleManager.CreateAsync(
            new IdentityRole<Guid>(name) { Id = Guid.CreateVersion7() });
        if (!created.Succeeded)
        {
            logger.LogError(
                "Could not create the {Role} role: {Errors}",
                name,
                string.Join(" ", created.Errors.Select(error => error.Description)));
        }
    }
}
