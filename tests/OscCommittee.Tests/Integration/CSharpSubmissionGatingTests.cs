using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// One normal upload per member. The only exception is a syntax error: the marker is deleted when
/// the corrected folder arrives, so the replacement is attempt #1 and no history is kept.
/// </summary>
public sealed class CSharpSubmissionGatingTests
{
    [Fact]
    public async Task A_member_cannot_submit_the_same_task_twice()
    {
        await using var app = new OscAppFactory();
        await app.InitializeAsync();

        var member = await app.CreateUserAsync(WebTestExtensions.UniqueEmail("once"), "Once Only");
        var taskId = await app.CreateTaskAsync("One upload " + Guid.NewGuid().ToString("N")[..8]);

        await app.CreateSubmissionAsync(taskId, member.Id);

        var second = await SubmitAsync(app, taskId, member.Id);

        Assert.True(second.Failed);
        Assert.Equal("already_submitted", second.ErrorCode);
    }

    [Fact]
    public async Task A_syntax_error_marker_is_replaced_by_the_corrected_upload()
    {
        await using var app = new OscAppFactory();
        await app.InitializeAsync();

        var member = await app.CreateUserAsync(WebTestExtensions.UniqueEmail("retry"), "Retry Member");
        var taskId = await app.CreateTaskAsync("Retry " + Guid.NewGuid().ToString("N")[..8]);

        var broken = await app.CreateSubmissionAsync(taskId, member.Id);
        await MarkAsSyntaxErrorAsync(app, broken);

        var corrected = await SubmitAsync(app, taskId, member.Id);

        Assert.True(corrected.Succeeded, corrected.Message);
        Assert.Equal(1, corrected.Value!.AttemptNo);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.TaskId == taskId && submission.UserId == member.Id)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.NotEqual(broken, row.Id);
        Assert.Equal(1, row.AttemptNo);
        Assert.Equal(SubmissionStatus.Uploaded, row.Status);
    }

    private static async Task MarkAsSyntaxErrorAsync(OscAppFactory app, Guid submissionId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.CSharpSubmissions.SingleAsync(submission => submission.Id == submissionId);
        row.Status = SubmissionStatus.TryAgain;
        row.AiScore = 0;
        row.Score = null;
        await db.SaveChangesAsync();
    }

    private static async Task<Result<SubmissionReceipt>> SubmitAsync(
        OscAppFactory app,
        Guid taskId,
        Guid userId)
    {
        using var scope = app.Services.CreateScope();
        var submissions = scope.ServiceProvider.GetRequiredService<ICSharpSubmissionService>();
        var bytes = Encoding.UTF8.GetBytes("using System;\n\nConsole.WriteLine(\"Hello again\");\n");

        return await submissions.SubmitAsync(
            taskId,
            userId,
            [new SubmissionInputFile("Program.cs", bytes.Length, _ => Task.FromResult<Stream>(new MemoryStream(bytes)))]);
    }
}