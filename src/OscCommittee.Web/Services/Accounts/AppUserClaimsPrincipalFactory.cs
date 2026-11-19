using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Services.Accounts;

/// <summary>
/// Adds the display name and account status to the cookie so views never have to
/// query the database just to render the top bar.
/// </summary>
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    public const string DisplayNameClaim = "display_name";

    public const string StatusClaim = "account_status";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));
        identity.AddClaim(new Claim(StatusClaim, user.Status.ToString()));
        return identity;
    }
}
