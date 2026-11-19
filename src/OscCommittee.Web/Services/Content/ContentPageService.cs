using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Content;

/// <summary>One page as a list sees it: no body, just the facts a card needs.</summary>
public sealed record ContentPageRow(
    Guid Id,
    string Title,
    bool IsPublished,
    DateTimeOffset UpdatedAtUtc,
    string? UpdatedByName);

/// <summary>One page with its Markdown body, for the member page and the board editor.</summary>
public sealed record ContentPageDetail(
    Guid Id,
    string Title,
    string BodyMarkdown,
    bool IsPublished,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? CreatedByName,
    string? UpdatedByName);

/// <summary>The editable fields: title, Markdown body and the publish flag.</summary>
public sealed record ContentPageRequest(
    string Title,
    string? BodyMarkdown,
    bool IsPublished);

/// <summary>Board-authored pages (PLAN §7.5 and §8.9): title, Markdown body and publish flag.</summary>
public interface IContentPageService
{
    /// <summary>Published pages in title order; the member list and nothing else.</summary>
    Task<IReadOnlyList<ContentPageRow>> GetPublishedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContentPageRow>> GetBoardListAsync(CancellationToken cancellationToken = default);

    Task<ContentPageDetail?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Only published pages resolve here; drafts return null so the controller can 404.</summary>
    Task<ContentPageDetail?> FindPublishedAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Guid>> CreateAsync(
        ContentPageRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        Guid id,
        ContentPageRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, Guid boardUserId, CancellationToken cancellationToken = default);
}

public sealed class ContentPageService(
    AppDbContext db,
    TimeProvider timeProvider) : IContentPageService
{
    public const string TitleMessage = "Use a title between 3 and 160 characters.";
    public const string BodyMessage = "The page body is too large. Keep it under 200,000 characters.";
    public const string SaveMessage = "The topic could not be saved. Try again.";

    public async Task<IReadOnlyList<ContentPageRow>> GetPublishedAsync(
        CancellationToken cancellationToken = default) =>
        await RowsAsync(publishedOnly: true, cancellationToken);

    public async Task<IReadOnlyList<ContentPageRow>> GetBoardListAsync(
        CancellationToken cancellationToken = default) =>
        await RowsAsync(publishedOnly: false, cancellationToken);

    public Task<ContentPageDetail?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        DetailAsync(page => page.Id == id, cancellationToken);

    public async Task<ContentPageDetail?> FindPublishedAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var page = await FindAsync(id, cancellationToken);
        return page is { IsPublished: true } ? page : null;
    }

    public async Task<Result<Guid>> CreateAsync(
        ContentPageRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var built = BuildEntity(request);
        if (built.Failure is { } failure)
        {
            return Result.Fail<Guid>(failure.ErrorCode ?? "invalid", failure.Message);
        }

        var now = timeProvider.GetUtcNow();
        var page = new ContentPage
        {
            Title = built.Title,
            BodyMarkdown = built.Body,
            IsPublished = request.IsPublished,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = boardUserId,
            UpdatedByUserId = boardUserId
        };

        db.ContentPages.Add(page);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail<Guid>("save_failed", SaveMessage);
        }

        return Result.Ok(
            page.Id,
            page.IsPublished
                ? $"\"{page.Title}\" is published at /content/{page.Id}."
                : $"\"{page.Title}\" was saved as a draft.");
    }

    public async Task<Result> UpdateAsync(
        Guid id,
        ContentPageRequest request,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var page = await db.ContentPages.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (page is null)
        {
            return Result.Fail("not_found", "That page no longer exists.");
        }

        var built = BuildEntity(request);
        if (built.Failure is { } failure)
        {
            return failure;
        }

        page.Title = built.Title;
        page.BodyMarkdown = built.Body;
        page.IsPublished = request.IsPublished;
        page.UpdatedAtUtc = timeProvider.GetUtcNow();
        page.UpdatedByUserId = boardUserId;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Fail("save_failed", SaveMessage);
        }

        return Result.Ok($"\"{page.Title}\" is saved.");
    }

    public async Task<Result> DeleteAsync(
        Guid id,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var page = await db.ContentPages.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (page is null)
        {
            return Result.Fail("not_found", "That page no longer exists.");
        }

        var title = page.Title;

        db.ContentPages.Remove(page);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok($"\"{title}\" was deleted.");
    }

    private async Task<IReadOnlyList<ContentPageRow>> RowsAsync(
        bool publishedOnly,
        CancellationToken cancellationToken)
    {
        var query = db.ContentPages.AsNoTracking();

        if (publishedOnly)
        {
            query = query.Where(page => page.IsPublished);
        }

        var rows = await query
            .OrderBy(page => page.Title)
            .ThenBy(page => page.Id)
            .Select(page => new
            {
                page.Id,
                page.Title,
                page.IsPublished,
                page.UpdatedAtUtc,
                UpdatedByName = db.Users
                    .Where(user => user.Id == page.UpdatedByUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ContentPageRow(
                row.Id,
                row.Title,
                row.IsPublished,
                row.UpdatedAtUtc,
                row.UpdatedByName))
            .ToList();
    }

    private async Task<ContentPageDetail?> DetailAsync(
        System.Linq.Expressions.Expression<Func<ContentPage, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var row = await db.ContentPages
            .AsNoTracking()
            .Where(predicate)
            .Select(page => new
            {
                page.Id,
                page.Title,
                page.BodyMarkdown,
                page.IsPublished,
                page.CreatedAtUtc,
                page.UpdatedAtUtc,
                CreatedByName = db.Users
                    .Where(user => user.Id == page.CreatedByUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault(),
                UpdatedByName = db.Users
                    .Where(user => user.Id == page.UpdatedByUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ContentPageDetail(
                row.Id,
                row.Title,
                row.BodyMarkdown,
                row.IsPublished,
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.CreatedByName,
                row.UpdatedByName);
    }

    /// <summary>
    /// Validates and normalises the request once. <see cref="BuiltPage.Failure"/> carries the
    /// first field error; otherwise the normalised values are ready to save.
    /// </summary>
    private static BuiltPage BuildEntity(ContentPageRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var body = string.IsNullOrWhiteSpace(request.BodyMarkdown)
            ? string.Empty
            : request.BodyMarkdown.Replace("\r\n", "\n").Trim();

        Result? failure = null;

        if (title.Length is < ContentRules.MinTitleLength or > ContentRules.MaxTitleLength)
        {
            failure = Result.Fail("invalid_title", TitleMessage);
        }
        else if (body.Length > ContentRules.MaxBodyChars)
        {
            failure = Result.Fail("body_too_large", BodyMessage);
        }

        return failure is not null
            ? new BuiltPage(string.Empty, string.Empty, failure)
            : new BuiltPage(title, body, null);
    }

    private sealed record BuiltPage(
        string Title,
        string Body,
        Result? Failure);
}