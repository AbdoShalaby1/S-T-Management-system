using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Integrations.Codeforces;
using OscCommittee.Web.Integrations.LeetCode;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Points;
using OscCommittee.Web.Services.Standings;
using OscCommittee.Web.ViewModels.Board;
using OscCommittee.Web.ViewModels.Settings;

namespace OscCommittee.Web.Controllers;

/// <summary>Profile, handles and password for a member account. Board accounts are refused.</summary>
[Route("settings")]
[Authorize(Policy = "Settings")]
public sealed class SettingsController(
    IProfileService profiles,
    IStandingsService standings,
    IPointsService points,
    ITimeZoneService timeZone,
    ILeetCodeClient leetCode,
    ICodeforcesClient codeforces,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Settings";

        var model = await BuildAsync(cancellationToken: cancellationToken);
        return model is null ? Challenge() : View(model);
    }

    [HttpPost("profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(
        [Bind(Prefix = "Profile")] ProfileFormViewModel form,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Settings";

        if (!ModelState.IsValid)
        {
            return await RenderAsync(new SettingsViewModel { Profile = form }, "profile", cancellationToken);
        }

        var result = await profiles.UpdateAsync(
            CurrentUserId,
            new UpdateProfileRequest(form.DisplayName, form.LeetCodeHandle, form.CodeforcesHandle),
            cancellationToken);

        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return await RenderAsync(new SettingsViewModel { Profile = form }, "profile", cancellationToken);
        }

        TempData["Toast.Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(
        [Bind(Prefix = "Password")] ChangePasswordViewModel form,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Settings";

        if (!ModelState.IsValid)
        {
            return await RenderAsync(new SettingsViewModel { Password = form }, "password", cancellationToken);
        }

        var result = await profiles.ChangePasswordAsync(
            CurrentUserId,
            new ChangePasswordRequest(form.CurrentPassword, form.NewPassword),
            cancellationToken);

        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return await RenderAsync(new SettingsViewModel { Password = form }, "password", cancellationToken);
        }

        // Changing the password rotates the security stamp. Refresh this cookie so the person
        // is not signed out of the session they just used.
        if (await userManager.GetUserAsync(User) is { } user)
        {
            await signInManager.RefreshSignInAsync(user);
        }

        TempData["Toast.Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("handles/verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyHandle(
        string platform,
        string? handle,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SyncPlatform>(platform, ignoreCase: true, out var parsed))
        {
            return BadRequest(new { ok = false, message = "Pick LeetCode or Codeforces." });
        }

        var normalized = handle?.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return BadRequest(new { ok = false, message = "Type a handle first." });
        }

        // Advisory only: a failed check never blocks saving the profile (PLAN §7.6).
        var check = parsed == SyncPlatform.LeetCode
            ? await leetCode.CheckHandleAsync(normalized, cancellationToken)
            : await codeforces.CheckHandleAsync(normalized, cancellationToken);

        if (check.Failed || check.Value is null)
        {
            var status = check.ErrorCode == "rate_limited"
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status400BadRequest;

            return StatusCode(status, new { ok = false, message = check.Message });
        }

        var mark = await profiles.MarkHandleVerifiedAsync(
            CurrentUserId,
            parsed,
            normalized,
            cancellationToken);

        var label = parsed == SyncPlatform.LeetCode ? "LeetCode" : "Codeforces";
        var name = string.IsNullOrWhiteSpace(check.Value.DisplayName)
            ? check.Value.Handle
            : $"{check.Value.Handle} ({check.Value.DisplayName})";

        return Ok(new
        {
            ok = true,
            handle = check.Value.Handle,
            displayName = check.Value.DisplayName,
            message = $"{label} confirmed {name}. {mark.Message}".Trim()
        });
    }

    private Guid CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id)
                ? id
                : throw new InvalidOperationException("The Member policy ran without a user id claim.");
        }
    }

    private async Task<IActionResult> RenderAsync(
        SettingsViewModel submitted,
        string form,
        CancellationToken cancellationToken = default)
    {
        ViewData["SettingsForm"] = form;
        var model = await BuildAsync(submitted, cancellationToken);
        return model is null ? Challenge() : View(nameof(Index), model);
    }

    private async Task<SettingsViewModel?> BuildAsync(
        SettingsViewModel? submitted = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await profiles.GetAsync(CurrentUserId);
        if (snapshot is null)
        {
            return null;
        }

        var form = submitted?.Profile ?? new ProfileFormViewModel
        {
            DisplayName = snapshot.DisplayName,
            LeetCodeHandle = snapshot.LeetCodeHandle,
            CodeforcesHandle = snapshot.CodeforcesHandle
        };

        form.LeetCodeVerifiedAtUtc = snapshot.LeetCodeVerifiedAtUtc;
        form.CodeforcesVerifiedAtUtc = snapshot.CodeforcesVerifiedAtUtc;

        // The dashboard totals the open month; reuse the same source so the two never disagree.
        var openMonth = await standings.GetOpenMonthAsync(cancellationToken);
        var monthPoints = openMonth is null
            ? (int?)null
            : await points.GetTotalAsync(CurrentUserId, openMonth.Id, cancellationToken);

        return new SettingsViewModel
        {
            Profile = form,
            Password = submitted?.Password ?? new ChangePasswordViewModel(),
            Email = snapshot.Email,
            Status = snapshot.Status,
            Initials = UserStatusView.Initials(snapshot.DisplayName),
            MemberSinceLabel = timeZone.FormatLocal(snapshot.CreatedAtUtc, "d MMMM yyyy"),
            MonthLabel = openMonth?.Label ?? timeZone.CurrentMonthLabel(),
            CurrentMonthPointsLabel = monthPoints?.ToString() ?? "—",
            IsBoard = snapshot.IsBoard
        };
    }
}
