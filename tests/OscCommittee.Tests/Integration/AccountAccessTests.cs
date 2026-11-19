using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// PLAN §15 and §18: approval gates access, a deactivated account cannot sign in, and the board
/// area is closed to ordinary members.
/// </summary>
public sealed class AccountAccessTests(OscAppFixture fixture) : IClassFixture<OscAppFixture>
{
    private readonly OscAppFactory _app = fixture.App;

    [Fact]
    public async Task Register_then_pending_then_approved_then_access()
    {
        var email = WebTestExtensions.UniqueEmail("newcomer");
        using var member = _app.CreateClient();

        using var registered = await member.RegisterAsync("New Comer", email, OscAppFactory.MemberPassword);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Contains("Waiting for approval", await registered.Content.ReadAsStringAsync());

        // Still pending: the dashboard is replaced by the waiting screen.
        using var blocked = await member.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("Waiting for approval", await blocked.Content.ReadAsStringAsync());

        // The board sees the request and approves it.
        using var board = _app.CreateClient();
        using var login = await board.LoginAsync(OscAppFactory.BoardEmail, OscAppFactory.BoardPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var approvalsToken = await board.GetAntiforgeryTokenAsync("/board/approvals");
        var memberId = await FindUserIdAsync(email);
        using var approved = await board.PostFormAsync(
            $"/board/approvals/{memberId}/approve",
            new Dictionary<string, string> { ["__RequestVerificationToken"] = approvalsToken });

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.DoesNotContain(
            "Waiting for approval",
            await approved.Content.ReadAsStringAsync());

        // The member's existing cookie now passes the Member policy; no re-login needed.
        using var dashboard = await member.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Waiting for approval", dashboardHtml);
        Assert.Contains("Dashboard", dashboardHtml);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_sign_in()
    {
        var email = WebTestExtensions.UniqueEmail("deactivated");
        await _app.CreateUserAsync(email, "Gone Member", UserStatus.Deactivated);

        using var client = _app.CreateClient();
        using var response = await client.LoginAsync(email, OscAppFactory.MemberPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Email or password is incorrect.", await response.Content.ReadAsStringAsync());

        // No session was created.
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Sign in", await home.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_active_member_is_refused_by_the_board_area()
    {
        var email = WebTestExtensions.UniqueEmail("member");
        await _app.CreateUserAsync(email, "Ordinary Member");

        using var member = await _app.SignedInClientAsync(email, OscAppFactory.MemberPassword);
        using var response = await member.GetAsync("/board");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Access denied", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_board_account_reaches_the_board_dashboard()
    {
        using var board = await _app.SignedInClientAsync(OscAppFactory.BoardEmail, OscAppFactory.BoardPassword);
        using var response = await board.GetAsync("/board");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Approvals", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_board_account_cannot_open_settings_and_gets_no_link_to_it()
    {
        using var board = await _app.SignedInClientAsync(OscAppFactory.BoardEmail, OscAppFactory.BoardPassword);

        using var settings = await board.GetAsync("/settings");
        Assert.Equal(HttpStatusCode.Forbidden, settings.StatusCode);
        Assert.Contains("Access denied", await settings.Content.ReadAsStringAsync());

        using var dashboard = await board.GetAsync("/board");
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.DoesNotContain("href=\"/settings\"", html);
        Assert.DoesNotContain("user-menu-button", html);
    }

    [Fact]
    public async Task A_member_can_still_open_settings()
    {
        var email = WebTestExtensions.UniqueEmail("settings");
        await _app.CreateUserAsync(email, "Settings Member");

        using var member = await _app.SignedInClientAsync(email, OscAppFactory.MemberPassword);
        using var settings = await member.GetAsync("/settings");

        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Contains("Settings", await settings.Content.ReadAsStringAsync());
    }

    private async Task<Guid> FindUserIdAsync(string email)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);

        Assert.NotNull(user);
        return user.Id;
    }
}