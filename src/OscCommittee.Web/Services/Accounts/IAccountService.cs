using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Accounts;

public sealed record RegisterRequest(string DisplayName, string Email, string Password);

public sealed record SignInRequest(string Email, string Password);

public interface IAccountService
{
    /// <summary>Creates a <c>Pending</c> account. Fails when the email is already registered.</summary>
    Task<Result<ApplicationUser>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies credentials and lifecycle state. Rejected and deactivated accounts both
    /// fail, but only a rejected account gets a specific message.
    /// </summary>
    Task<Result<ApplicationUser>> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    Task<bool> IsBoardAsync(ApplicationUser user);
}
