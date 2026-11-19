using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Accounts;

public sealed class ProfileService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : IProfileService
{
    public async Task<ProfileSnapshot?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        return new ProfileSnapshot(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.Status,
            user.LeetCodeHandle,
            user.CodeforcesHandle,
            user.LeetCodeVerifiedAtUtc,
            user.CodeforcesVerifiedAtUtc,
            user.CreatedAtUtc,
            await userManager.IsInRoleAsync(user, Roles.Board));
    }

    public async Task<Result> UpdateAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("not_found", "That account no longer exists.");
        }

        var displayName = request.DisplayName.Trim();
        if (displayName.Length is < 2 or > 80)
        {
            return Result.Fail("invalid_name", "Use between 2 and 80 characters for your display name.");
        }

        var leetCodeHandle = NormalizeHandle(request.LeetCodeHandle);
        var codeforcesHandle = NormalizeHandle(request.CodeforcesHandle);

        if (leetCodeHandle is { Length: > 64 } || codeforcesHandle is { Length: > 64 })
        {
            return Result.Fail("invalid_handle", "Handles cannot be longer than 64 characters.");
        }

        var changed = false;
        var now = timeProvider.GetUtcNow();

        if (!string.Equals(user.DisplayName, displayName, StringComparison.Ordinal))
        {
            user.DisplayName = displayName;
            changed = true;
        }

        if (!string.Equals(user.LeetCodeHandle, leetCodeHandle, StringComparison.Ordinal))
        {
            user.LeetCodeHandle = leetCodeHandle;
            user.LeetCodeVerifiedAtUtc = null;
            await ResetSyncAsync(user.Id, SyncPlatform.LeetCode, now, cancellationToken);
            changed = true;
        }

        if (!string.Equals(user.CodeforcesHandle, codeforcesHandle, StringComparison.Ordinal))
        {
            user.CodeforcesHandle = codeforcesHandle;
            user.CodeforcesVerifiedAtUtc = null;
            await ResetSyncAsync(user.Id, SyncPlatform.Codeforces, now, cancellationToken);
            changed = true;
        }

        if (!changed)
        {
            return Result.Ok("Nothing changed.");
        }

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result.Fail(
                "update_failed",
                string.Join(" ", updated.Errors.Select(error => error.Description)));
        }

        return Result.Ok("Your profile is saved.");
    }

    public async Task<Result> MarkHandleVerifiedAsync(
        Guid userId,
        SyncPlatform platform,
        string handle,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("not_found", "That account no longer exists.");
        }

        var normalized = NormalizeHandle(handle);
        var saved = platform == SyncPlatform.LeetCode ? user.LeetCodeHandle : user.CodeforcesHandle;

        if (normalized is null || !string.Equals(normalized, saved, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Ok("That handle checks out. Save your profile to keep it.");
        }

        var now = timeProvider.GetUtcNow();
        if (platform == SyncPlatform.LeetCode)
        {
            user.LeetCodeVerifiedAtUtc = now;
        }
        else
        {
            user.CodeforcesVerifiedAtUtc = now;
        }

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result.Fail(
                "update_failed",
                string.Join(" ", updated.Errors.Select(error => error.Description)));
        }

        return Result.Ok("Handle verified.");
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("not_found", "That account no longer exists.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return Result.Fail("wrong_password", "That is not your current password.");
        }

        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
        {
            return Result.Fail("same_password", "Choose a password different from your current one.");
        }

        var changed = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return Result.Fail(
                "password_failed",
                string.Join(" ", changed.Errors.Select(error => error.Description)));
        }

        return Result.Ok("Your password is changed.");
    }

    /// <summary>Trim, drop a leading <c>@</c>, and treat blank as "not set".</summary>
    private static string? NormalizeHandle(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        return handle.Trim().TrimStart('@');
    }

    /// <summary>
    /// PLAN §5: a changed handle restarts syncing from scratch. Part 07 consumes these fields;
    /// until then the row simply sits reset.
    /// </summary>
    private async Task ResetSyncAsync(
        Guid userId,
        SyncPlatform platform,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = await db.SyncStates
            .FirstOrDefaultAsync(row => row.UserId == userId && row.Platform == platform, cancellationToken);

        if (state is null)
        {
            return;
        }

        state.LastSyncAtUtc = null;
        state.HistoryGapPossible = false;
        state.FailureCount = 0;
        state.LastError = null;
        state.UpdatedAtUtc = now;
    }
}
