using System.Net;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// PLAN §9: the board reads the same standings as members, without the member-only progress strip
/// and without ever being ranked itself.
/// </summary>
public sealed class StandingsAccessTests(OscAppFixture fixture) : IClassFixture<OscAppFixture>
{
    private readonly OscAppFactory _app = fixture.App;

    [Fact]
    public async Task The_board_can_open_standings_without_being_ranked()
    {
        var member = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("standings"),
            "Standings Member");
        await _app.CreateTaskAsync("Standings test " + Guid.NewGuid().ToString("N")[..8]);

        using var board = await _app.SignedInClientAsync(OscAppFactory.BoardEmail, OscAppFactory.BoardPassword);

        using var summary = await board.GetAsync("/standings");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);

        var summaryHtml = await summary.Content.ReadAsStringAsync();
        Assert.Contains(member.DisplayName, summaryHtml);

        // The context strip is the viewer's own progress, so the board does not get one.
        Assert.DoesNotContain("aria-label=\"Current work\"", summaryHtml);
        Assert.DoesNotContain("Board Member", TableBody(summaryHtml));

        using var detail = await board.GetAsync(
            $"/standings?view=detail&tab=all&member={member.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var detailHtml = await detail.Content.ReadAsStringAsync();
        Assert.Contains(member.DisplayName, detailHtml);
        Assert.Contains("Month by month", detailHtml);
    }

    [Fact]
    public async Task A_member_still_sees_their_own_progress_strip()
    {
        var member = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("progress"),
            "Progress Member");
        await _app.CreateTaskAsync("Progress test " + Guid.NewGuid().ToString("N")[..8]);

        using var client = await _app.SignedInClientAsync(member.Email!, OscAppFactory.MemberPassword);

        using var summary = await client.GetAsync("/standings");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);

        var html = await summary.Content.ReadAsStringAsync();
        Assert.Contains("aria-label=\"Current work\"", html);
        Assert.DoesNotContain("Include deactivated", html);
        Assert.DoesNotContain("aria-label=\"Standings view\"", html);
    }

    [Fact]
    public async Task A_member_gets_their_own_points_page_and_cannot_open_someone_elses()
    {
        var member = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("points"),
            "Points Member");
        var other = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("other-points"),
            "Other Points Member");
        await _app.CreateTaskAsync("Points test " + Guid.NewGuid().ToString("N")[..8]);

        using var client = await _app.SignedInClientAsync(member.Email!, OscAppFactory.MemberPassword);

        using var points = await client.GetAsync("/points");
        Assert.Equal(HttpStatusCode.OK, points.StatusCode);

        var pointsHtml = await points.Content.ReadAsStringAsync();
        Assert.Contains(member.DisplayName, pointsHtml);
        Assert.Contains("Month by month", pointsHtml);

        // The member's own page has no member picker; it is always them.
        Assert.DoesNotContain("id=\"standings-member\"", pointsHtml);
        Assert.DoesNotContain(other.DisplayName, pointsHtml);

        using var summary = await client.GetAsync("/standings");
        var summaryHtml = await summary.Content.ReadAsStringAsync();

        // Only their own row links anywhere; other members are plain text.
        Assert.Contains("href=\"/points\"", summaryHtml);
        Assert.DoesNotContain("view=detail", summaryHtml);

        using var someoneElse = await client.GetAsync(
            $"/standings?view=detail&tab=all&member={other.Id}");
        Assert.Equal(HttpStatusCode.OK, someoneElse.StatusCode);

        // The redirect lands on their own points page, never the other member's ledger.
        var ownPage = await someoneElse.Content.ReadAsStringAsync();
        Assert.Contains("Month by month", ownPage);
        Assert.DoesNotContain("id=\"standings-member\"", ownPage);
        Assert.DoesNotContain(other.DisplayName, ownPage);
    }

    [Fact]
    public async Task The_board_detail_keeps_the_member_picker_and_points_redirects_the_board()
    {
        var member = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("picker"),
            "Picker Member");
        await _app.CreateTaskAsync("Picker test " + Guid.NewGuid().ToString("N")[..8]);

        using var board = await _app.SignedInClientAsync(OscAppFactory.BoardEmail, OscAppFactory.BoardPassword);

        using var search = await board.GetAsync($"/standings?view=detail&tab=all&member={member.Id}");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        var html = await search.Content.ReadAsStringAsync();
        Assert.Contains("id=\"standings-member\"", html);
        Assert.Contains(member.DisplayName, html);

        using var points = await board.GetAsync("/points");
        Assert.Equal(HttpStatusCode.OK, points.StatusCode);

        var boardView = await points.Content.ReadAsStringAsync();
        Assert.Contains("Ranked by total points", boardView);
        Assert.DoesNotContain("Month by month", boardView);
    }

    private static string TableBody(string html)
    {
        var start = html.IndexOf("<tbody>", StringComparison.Ordinal);
        var end = html.IndexOf("</tbody>", start, StringComparison.Ordinal);
        return start < 0 || end < start ? html : html[start..end];
    }
}