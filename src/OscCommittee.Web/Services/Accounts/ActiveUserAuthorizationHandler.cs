using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Services.Accounts;

public sealed class ActiveUserAuthorizationHandler(UserManager<ApplicationUser> userManager)
    : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is not null && user.Status == UserStatus.Active)
        {
            context.Succeed(requirement);
        }
    }
}
