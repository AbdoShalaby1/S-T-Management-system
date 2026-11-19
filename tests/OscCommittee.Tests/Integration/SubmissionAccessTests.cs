using System.Net;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// PLAN §15: a member only ever sees their own attempts; the board can read and download any.
/// </summary>
public sealed class SubmissionAccessTests(OscAppFixture fixture) : IClassFixture<OscAppFixture>
{
    private readonly OscAppFactory _app = fixture.App;

    [Fact]
    public async Task A_member_cannot_open_another_members_submission()
    {
        var owner = await _app.CreateUserAsync(WebTestExtensions.UniqueEmail("owner"), "Owner Member");
        var other = await _app.CreateUserAsync(WebTestExtensions.UniqueEmail("other"), "Other Member");
        var title = "Access test " + Guid.NewGuid().ToString("N")[..8];
        var taskId = await _app.CreateTaskAsync(title);
        var submissionId = await _app.CreateSubmissionAsync(taskId, owner.Id);

        using var otherClient = await _app.SignedInClientAsync(other.Email!, OscAppFactory.MemberPassword);

        using var view = await otherClient.GetAsync($"/tasks/submissions/{submissionId}");
        Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);

        using var status = await otherClient.GetAsync($"/tasks/submissions/{submissionId}/status");
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

        using var ownerClient = await _app.SignedInClientAsync(owner.Email!, OscAppFactory.MemberPassword);
        using var own = await ownerClient.GetAsync($"/tasks/submissions/{submissionId}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains(title, await own.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_the_board_can_download_a_stored_folder()
    {
        var owner = await _app.CreateUserAsync(WebTestExtensions.UniqueEmail("folder"), "Folder Owner");
        var other = await _app.CreateUserAsync(WebTestExtensions.UniqueEmail("blocked"), "Blocked Member");
        var taskId = await _app.CreateTaskAsync("Download test " + Guid.NewGuid().ToString("N")[..8]);
        var submissionId = await _app.CreateSubmissionAsync(taskId, owner.Id);

        using var member = await _app.SignedInClientAsync(other.Email!, OscAppFactory.MemberPassword);
        using var refused = await member.GetAsync($"/board/submissions/{submissionId}/download");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var board = await _app.SignedInClientAsync(OscAppFactory.BoardEmail, OscAppFactory.BoardPassword);

        using var review = await board.GetAsync($"/board/submissions/{submissionId}");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);

        using var download = await board.GetAsync($"/board/submissions/{submissionId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).Length > 0);
    }
}