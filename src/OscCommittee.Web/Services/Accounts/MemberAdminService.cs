using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Accounts;

/// <summary>
/// The board's member tooling. Reads go straight through EF (the board lists need role and
/// status in one projection); writes go through <see cref="UserManager{TUser}"/> so validators
/// and the security stamp stay correct.
/// </summary>
public sealed class MemberAdminService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : IMemberAdminService
{
    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        db.Users.CountAsync(user => user.Status == UserStatus.Pending, cancellationToken);

    public Task<int> CountActiveMembersAsync(CancellationToken cancellationToken = default) =>
        db.Users.CountAsync(user => user.Status == UserStatus.Active, cancellationToken);

    public async Task<int> CountActiveBoardAsync(CancellationToken cancellationToken = default)
    {
        var boardRoleId = await BoardRoleIdAsync(cancellationToken);
        if (boardRoleId is null)
        {
            return 0;
        }

        return await db.UserRoles
            .Where(userRole => userRole.RoleId == boardRoleId)
            .Join(
                db.Users.Where(user => user.Status == UserStatus.Active),
                userRole => userRole.UserId,
                user => user.Id,
                (_, user) => user.Id)
            .CountAsync(cancellationToken);
    }

    public async Task<MemberCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await db.Users
            .GroupBy(user => user.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int CountFor(UserStatus status) =>
            groups.FirstOrDefault(group => group.Status == status)?.Count ?? 0;

        return new MemberCounts(
            groups.Sum(group => group.Count),
            CountFor(UserStatus.Active),
            CountFor(UserStatus.Pending),
            CountFor(UserStatus.Deactivated));
    }

    public async Task<IReadOnlyList<MemberSummary>> GetPendingAsync(
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = await SummariesAsync(
            db.Users
                .Where(user => user.Status == UserStatus.Pending)
                .OrderBy(user => user.CreatedAtUtc)
                .Take(take),
            cancellationToken);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemberSummary>> GetMembersAsync(
        MemberFilter filter,
        CancellationToken cancellationToken = default)
    {
        var users = db.Users.AsQueryable();

        if (filter.Status is { } status)
        {
            users = users.Where(user => user.Status == status);
        }

        if (filter.OnlyMissingHandles)
        {
            users = users.Where(user =>
                user.LeetCodeHandle == null
                || user.LeetCodeHandle == ""
                || user.CodeforcesHandle == null
                || user.CodeforcesHandle == "");
        }

        users = filter.Sort switch
        {
            "joined" => users.OrderByDescending(user => user.CreatedAtUtc),
            "status" => users.OrderBy(user => user.Status).ThenBy(user => user.DisplayName),
            _ => users.OrderBy(user => user.DisplayName)
        };

        var query = await SummariesAsync(users, cancellationToken);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<MemberSummary?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var query = await SummariesAsync(db.Users.Where(user => user.Id == userId), cancellationToken);
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Result> ApproveAsync(
        Guid userId,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("not_found", "That account no longer exists.");
        }

        if (user.Status != UserStatus.Pending)
        {
            return Result.Fail("not_pending", $"{user.DisplayName} is not waiting for approval.");
        }

        user.Status = UserStatus.Active;
        user.ApprovedAtUtc = timeProvider.GetUtcNow();
        user.ApprovedByUserId = boardUserId;
        user.EmailConfirmed = true;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result.Fail("update_failed", UpdateFailure(updated));
        }

        // No security-stamp rotation here: the pending member's cookie keeps a Pending claim,
        // and AccountStateMiddleware re-reads the database for any non-Active claim, so they
        // get in on their next request without being signed out first.
        return Result.Ok($"{user.DisplayName} can use the app now.");
    }

    public async Task<Result> RejectAsync(
        Guid userId,
        Guid boardUserId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("not_found", "That account no longer exists.");
        }

        if (user.Status != UserStatus.Pending)
        {
            return Result.Fail("not_pending", $"{user.DisplayName} is not waiting for approval.");
        }

        user.Status = UserStatus.Rejected;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result.Fail("update_failed", UpdateFailure(updated));
        }

        // Kill any cookie the rejected person still holds.
        await userManager.UpdateSecurityStampAsync(user);

        return Result.Ok($"{user.DisplayName} was rejected.");
    }

    public async Task<Result> DeactivateAsync(
        Guid userId,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        // PLAN §5: nobody can lock themselves out.
        if (userId == boardUserId)
        {
            return Result.Fail("self", "You cannot deactivate your own account.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("not_found", "That account no longer exists.");
        }

        if (user.Status == UserStatus.Deactivated)
        {
            return Result.Fail("already_deactivated", $"{user.DisplayName} is already deactivated.");
        }

        // Never remove the last active board account: that would strand the committee.
        if (await userManager.IsInRoleAsync(user, Roles.Board)
            && await CountActiveBoardAsync(cancellationToken) <= 1)
        {
            return Result.Fail(
                "last_board",
                "That is the only active board account.");
        }

        user.Status = UserStatus.Deactivated;
        user.DeactivatedAtUtc = timeProvider.GetUtcNow();
        user.DeactivatedByUserId = boardUserId;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result.Fail("update_failed", UpdateFailure(updated));
        }

        // Existing cookies die on the next validation pass.
        await userManager.UpdateSecurityStampAsync(user);

        return Result.Ok($"{user.DisplayName} was deactivated. Their history is kept.");
    }

    public async Task<Result> RestoreAsync(
        Guid userId,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("not_found", "That account no longer exists.");
        }

        if (user.Status == UserStatus.Active)
        {
            return Result.Fail("already_active", $"{user.DisplayName} is already active.");
        }

        user.Status = UserStatus.Active;
        user.DeactivatedAtUtc = null;
        user.DeactivatedByUserId = null;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result.Fail("update_failed", UpdateFailure(updated));
        }

        return Result.Ok($"{user.DisplayName} can sign in again.");
    }

    /// <summary>Projects user rows plus their board-role flag without an N+1 query.</summary>
    private async Task<IQueryable<MemberSummary>> SummariesAsync(
        IQueryable<ApplicationUser> users,
        CancellationToken cancellationToken)
    {
        var boardRoleId = await BoardRoleIdAsync(cancellationToken);

        if (boardRoleId is null)
        {
            return users.Select(user => new MemberSummary(
                user.Id,
                user.DisplayName,
                user.Email ?? string.Empty,
                user.Status,
                user.LeetCodeHandle,
                user.CodeforcesHandle,
                false,
                user.CreatedAtUtc,
                user.ApprovedAtUtc,
                user.DeactivatedAtUtc,
                user.LastSeenAtUtc));
        }

        var roleId = boardRoleId.Value;
        return users.Select(user => new MemberSummary(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.Status,
            user.LeetCodeHandle,
            user.CodeforcesHandle,
            db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == roleId),
            user.CreatedAtUtc,
            user.ApprovedAtUtc,
            user.DeactivatedAtUtc,
            user.LastSeenAtUtc));
    }

    private async Task<Guid?> BoardRoleIdAsync(CancellationToken cancellationToken) =>
        await db.Roles
            .Where(role => role.Name == Roles.Board)
            .Select(role => (Guid?)role.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static string UpdateFailure(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(error => error.Description));
}
