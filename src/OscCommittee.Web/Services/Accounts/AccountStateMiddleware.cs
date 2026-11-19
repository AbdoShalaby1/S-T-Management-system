using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Services.Accounts;

/// <summary>
/// Keeps a signed-in account inside the screen its state allows. A pending person only
/// ever sees the waiting screen; a rejected or deactivated cookie is thrown away.
/// The <c>Active</c> claim is trusted as a fast path, so active traffic pays no extra query.
/// </summary>
public sealed class AccountStateMiddleware(RequestDelegate next)
{
    private static readonly PathString PendingPath = "/account/pending";

    private static readonly PathString[] AlwaysAllowed =
    [
        new("/account/logout"),
        new("/error"),
        new("/health")
    ];

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(AppUserClaimsPrincipalFactory.StatusClaim)?.Value != nameof(UserStatus.Active))
        {
            var user = await userManager.GetUserAsync(context.User);

            if (user is null || user.Status is UserStatus.Rejected or UserStatus.Deactivated)
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Redirect("/account/login");
                return;
            }

            if (user.Status == UserStatus.Pending
                && !context.Request.Path.StartsWithSegments(PendingPath)
                && !AlwaysAllowed.Any(segment => context.Request.Path.StartsWithSegments(segment)))
            {
                context.Response.Redirect(PendingPath);
                return;
            }
        }

        await next(context);
    }
}
