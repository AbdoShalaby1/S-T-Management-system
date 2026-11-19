using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OscCommittee.Web.Data;
using OscCommittee.Web.Services.Tasks;

namespace OscCommittee.Tests.Integration;

/// <summary>The starting-code zip is member-downloadable only while the task is visible.</summary>
public sealed class TaskStarterZipTests(OscAppFixture fixture) : IClassFixture<OscAppFixture>
{
    private readonly OscAppFactory _app = fixture.App;

    [Fact]
    public async Task A_member_can_download_the_starting_code()
    {
        var member = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("starter"),
            "Starter Member");
        var taskId = await _app.CreateTaskAsync("Starter " + Guid.NewGuid().ToString("N")[..8]);
        await AttachStarterAsync(taskId, "starter.zip");

        using var client = await _app.SignedInClientAsync(member.Email!, OscAppFactory.MemberPassword);

        using var page = await client.GetAsync($"/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Starting code", await page.Content.ReadAsStringAsync());

        using var download = await client.GetAsync($"/tasks/{taskId}/starter/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task A_hidden_task_starting_code_is_not_available_to_members()
    {
        var member = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("hidden-starter"),
            "Hidden Starter Member");
        var taskId = await _app.CreateTaskAsync(
            "Hidden starter " + Guid.NewGuid().ToString("N")[..8],
            hidden: true);
        await AttachStarterAsync(taskId, "secret.zip");

        using var client = await _app.SignedInClientAsync(member.Email!, OscAppFactory.MemberPassword);

        using var page = await client.GetAsync($"/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);

        using var download = await client.GetAsync($"/tasks/{taskId}/starter/download");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        using var board = await _app.SignedInClientAsync(OscAppFactory.BoardEmail, OscAppFactory.BoardPassword);
        using var boardDownload = await board.GetAsync($"/board/tasks/{taskId}/starter/download");
        Assert.Equal(HttpStatusCode.OK, boardDownload.StatusCode);
    }

    private async Task AttachStarterAsync(Guid taskId, string fileName)
    {
        var bytes = CreateZip();

        using var scope = _app.Services.CreateScope();
        var starters = scope.ServiceProvider.GetRequiredService<IStarterZipService>();
        var saved = await starters.SaveAsync(
            new FormFile(new MemoryStream(bytes), 0, bytes.Length, "StarterZip", fileName));

        Assert.False(saved.Failed);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.CSharpTasks.FirstAsync(row => row.Id == taskId);
        task.StarterZipPath = saved.Value!.FilePath;
        task.StarterZipFileName = saved.Value.FileName;
        task.StarterZipSizeBytes = saved.Value.SizeBytes;
        await db.SaveChangesAsync();
    }

    private static byte[] CreateZip()
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Program.cs");
            using var stream = entry.Open();
            stream.Write("class Program { }"u8);
        }

        return buffer.ToArray();
    }
}