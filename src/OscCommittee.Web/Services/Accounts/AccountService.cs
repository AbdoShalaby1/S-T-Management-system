using Microsoft.AspNetCore.Identity;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Accounts;

public sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<AccountService> logger) : IAccountService
{
    /// <summary>Same copy for unknown emails, wrong passwords and deactivated accounts.</summary>
    public const string InvalidCredentialsMessage = "Email or password is incorrect.";

    public const string EmailTakenMessage = "That email is already registered. Sign in instead.";

    public const string NotApprovedMessage =
        "Your request was not approved. Talk to a board member if you think this is a mistake.";

    public async Task<Result<ApplicationUser>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var displayName = request.DisplayName.Trim();
        var email = request.Email.Trim();

        // Registration deliberately reveals a taken email: it prevents duplicate
        // accounts and the plan accepts the enumeration trade-off here.
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Result.Fail<ApplicationUser>("email_taken", EmailTakenMessage);
        }

        var user = new ApplicationUser
        {
            DisplayName = displayName,
            Email = email,
            UserName = email,
            Status = UserStatus.Pending,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return Result.Fail<ApplicationUser>(
                "registration_failed",
                string.Join(" ", created.Errors.Select(error => error.Description)));
        }

        var role = await userManager.AddToRoleAsync(user, Roles.Member);
        if (!role.Succeeded)
        {
            logger.LogWarning(
                "Created account {UserId} but could not add the Member role: {Errors}",
                user.Id,
                string.Join(" ", role.Errors.Select(error => error.Description)));
        }

        logger.LogInformation("Registered pending account {UserId}.", user.Id);
        return Result.Ok(user);
    }

    public async Task<Result<ApplicationUser>> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        // Login must not reveal whether an account exists, so an unknown email,
        // a deactivated account and a bad password all produce identical copy.
        if (user is null || user.Status == UserStatus.Deactivated)
        {
            return Result.Fail<ApplicationUser>("invalid_credentials", InvalidCredentialsMessage);
        }

        if (user.Status == UserStatus.Rejected)
        {
            return Result.Fail<ApplicationUser>("not_approved", NotApprovedMessage);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Result.Fail<ApplicationUser>("invalid_credentials", InvalidCredentialsMessage);
        }

        user.LastSeenAtUtc = timeProvider.GetUtcNow();
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            logger.LogWarning("Could not record the last-seen timestamp for {UserId}.", user.Id);
        }

        return Result.Ok(user);
    }

    public Task<bool> IsBoardAsync(ApplicationUser user) => userManager.IsInRoleAsync(user, Roles.Board);
}
