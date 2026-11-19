using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.ViewModels.Account;

namespace OscCommittee.Web.Controllers;

[Route("account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(
    IAccountService accounts,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ITimeZoneService timeZone) : Controller
{
    [HttpGet("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true && await userManager.GetUserAsync(User) is { } signedIn)
        {
            return await RedirectSignedInAsync(signedIn);
        }

        ViewData["Title"] = "Sign in";
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Sign in";
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accounts.SignInAsync(new SignInRequest(model.Email, model.Password), cancellationToken);
        if (result.Failed || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        var user = result.Value;
        await signInManager.SignInAsync(user, model.RememberMe);
        TempData["Toast.Success"] = $"Welcome back, {user.DisplayName}.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return await RedirectSignedInAsync(user);
    }

    [HttpGet("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register()
    {
        if (User.Identity?.IsAuthenticated == true && await userManager.GetUserAsync(User) is { } signedIn)
        {
            return await RedirectSignedInAsync(signedIn);
        }

        ViewData["Title"] = "Request access";
        return View(new RegisterViewModel());
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Request access";

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accounts.RegisterAsync(
            new RegisterRequest(model.DisplayName, model.Email, model.Password),
            cancellationToken);

        if (result.Failed || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        await signInManager.SignInAsync(result.Value, isPersistent: true);
        TempData["Toast.Success"] = "Account created. A board member will review it.";

        return RedirectToAction(nameof(Pending));
    }

    [HttpGet("pending")]
    [Authorize]
    public async Task<IActionResult> Pending()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (user.Status == UserStatus.Active)
        {
            return await RedirectSignedInAsync(user);
        }

        if (user.Status is UserStatus.Rejected or UserStatus.Deactivated)
        {
            await signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        ViewData["Title"] = "Waiting for approval";
        return View(new PendingViewModel
        {
            DisplayName = user.DisplayName,
            Email = user.Email ?? string.Empty,
            Status = user.Status,
            RequestedOnLabel = timeZone.FormatLocal(user.CreatedAtUtc, "d MMMM yyyy")
        });
    }

    [HttpPost("logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        TempData["Toast.Success"] = "You are signed out.";
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Active members land on the dashboard, board members on the board, everyone else on pending.</summary>
    private async Task<IActionResult> RedirectSignedInAsync(ApplicationUser user)
    {
        if (user.Status == UserStatus.Pending)
        {
            return RedirectToAction(nameof(Pending));
        }

        if (user.Status != UserStatus.Active)
        {
            return RedirectToAction(nameof(Login));
        }

        return await accounts.IsBoardAsync(user)
            ? RedirectToAction("Index", "Board")
            : RedirectToAction("Index", "Home");
    }
}
