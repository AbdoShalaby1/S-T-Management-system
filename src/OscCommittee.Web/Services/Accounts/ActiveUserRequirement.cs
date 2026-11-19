using Microsoft.AspNetCore.Authorization;

namespace OscCommittee.Web.Services.Accounts;

/// <summary>
/// Satisfied only when the signed-in account still has <c>Status == Active</c>.
/// This runs on every request that uses the <c>Member</c> policy, so an account
/// deactivated mid-session loses access without waiting for the cookie to expire.
/// </summary>
public sealed class ActiveUserRequirement : IAuthorizationRequirement;
