using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// PLAN §15: the server rejects oversized folders, too many files and uploads without code,
/// with copy that tells the member what to do next.
/// </summary>
public sealed class UploadLimitTests(OscAppFixture fixture) : IClassFixture<OscAppFixture>
{
    private readonly OscAppFactory _app = fixture.App;

    [Fact]
    public async Task A_file_over_the_per_file_limit_is_rejected_with_copy()
    {
        var member = await _app.CreateUserAsync(WebTestExtensions.UniqueEmail("big"), "Big Uploader");
        var taskId = await _app.CreateTaskAsync("Upload limit " + Guid.NewGuid().ToString("N")[..8]);
        using var client = await _app.SignedInClientAsync(member.Email!, OscAppFactory.MemberPassword);

        var bytes = new byte[(2 * 1024 * 1024) + 1024];
        Random.Shared.NextBytes(bytes);

        using var response = await PostAsync(client, taskId, ("Big.cs", bytes));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("is over 2 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_folder_without_any_cs_file_is_rejected()
    {
        var member = await _app.CreateUserAsync(WebTestExtensions.UniqueEmail("nocode"), "No Code");
        var taskId = await _app.CreateTaskAsync("Upload code " + Guid.NewGuid().ToString("N")[..8]);
        using var client = await _app.SignedInClientAsync(member.Email!, OscAppFactory.MemberPassword);

        using var response = await PostAsync(
            client,
            taskId,
            ("README.txt", Encoding.UTF8.GetBytes("Just notes.")),
            ("bin/output.dll", [1, 2, 3]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("had no .cs files", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task More_files_than_the_limit_are_rejected()
    {
        var member = await _app.CreateUserAsync(WebTestExtensions.UniqueEmail("many"), "Many Files");
        var taskId = await _app.CreateTaskAsync("Upload count " + Guid.NewGuid().ToString("N")[..8]);
        using var client = await _app.SignedInClientAsync(member.Email!, OscAppFactory.MemberPassword);

        var files = Enumerable.Range(0, 301)
            .Select(index => ($"File{index}.cs", Encoding.UTF8.GetBytes($"// {index}\n")))
            .ToArray();

        using var response = await PostAsync(client, taskId, files);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("more than 300 files", await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid taskId,
        params (string Name, byte[] Bytes)[] files)
    {
        var token = await client.GetAntiforgeryTokenAsync($"/tasks/{taskId}/submit");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");

        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", name);
        }

        return await client.PostAsync($"/tasks/{taskId}/submit", form);
    }
}