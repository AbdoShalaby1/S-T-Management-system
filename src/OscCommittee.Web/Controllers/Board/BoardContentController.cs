using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Content;
using OscCommittee.Web.ViewModels.Content;

namespace OscCommittee.Web.Controllers.Board;

/// <summary>
/// Board authoring for content pages (PLAN §8.9): list, Markdown editor with live preview,
/// publish toggle, image upload and a confirmed delete.
/// </summary>
[Route("board/content")]
public sealed class BoardContentController(
    IContentPageService content,
    IContentImageService images,
    IMarkdownService markdown,
    ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Topics";

        var rows = await content.GetBoardListAsync(cancellationToken);

        return View(new BoardContentListViewModel
        {
            PublishedCount = rows.Count(row => row.IsPublished),
            DraftCount = rows.Count(row => !row.IsPublished),
            Pages = rows
                .Select(row => new BoardContentRowViewModel
                {
                    Id = row.Id,
                    Title = row.Title,
                    IsPublished = row.IsPublished,
                    UpdatedLabel = timeZone.FormatLocal(row.UpdatedAtUtc, "d MMM yyyy · HH:mm"),
                    UpdatedByName = row.UpdatedByName,
                    PublicHref = $"/content/{row.Id}"
                })
                .ToList()
        });
    }

    [HttpGet("new")]
    public IActionResult New()
    {
        ViewData["Title"] = "New topic";

        return RenderForm(new ContentFormViewModel());
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        ContentFormViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return RenderForm(form);
        }

        var result = await content.CreateAsync(
            form.ToRequest(),
            CurrentUserId,
            cancellationToken);

        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return RenderForm(form);
        }

        Toast(result);
        return RedirectToAction(nameof(Edit), new { id = result.Value });
    }

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var page = await content.FindAsync(id, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        var form = new ContentFormViewModel
        {
            Id = page.Id,
            Title = page.Title,
            BodyMarkdown = page.BodyMarkdown,
            IsPublished = page.IsPublished,
            PublicHref = $"/content/{page.Id}",
            UpdatedLabel = timeZone.FormatLocal(page.UpdatedAtUtc, "d MMM yyyy · HH:mm"),
            UpdatedByName = page.UpdatedByName,
            Images = images.List(page.Id),
            LastImageUrl = TempData["Content.ImageUrl"] as string
        };

        return RenderForm(form);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        ContentFormViewModel form,
        CancellationToken cancellationToken)
    {
        form.Id = id;

        if (!ModelState.IsValid)
        {
            return RenderForm(form);
        }

        var result = await content.UpdateAsync(
            id,
            form.ToRequest(),
            CurrentUserId,
            cancellationToken);

        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return RenderForm(form);
        }

        Toast(result);
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        Toast(await content.DeleteAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Server-rendered Markdown for the editor's preview tab. JSON only.</summary>
    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public IActionResult Preview([FromForm(Name = "markdown")] string? text)
    {
        var rendered = markdown.ToContent(text);

        return Ok(new
        {
            ok = true,
            html = rendered.Html,
            headings = rendered.Headings.Select(heading => new
            {
                heading.Level,
                heading.Text,
                heading.Id
            })
        });
    }

    /// <summary>
    /// Stores one image under <c>wwwroot/content/{pageId}/</c>. The editor calls this with fetch
    /// and inserts the returned Markdown; a plain post still works and returns to the editor.
    /// </summary>
    [HttpPost("images")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ContentImageService.MaxBytes + 512 * 1024)]
    public async Task<IActionResult> UploadImage(
        Guid pageId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var page = pageId == Guid.Empty
            ? null
            : await content.FindAsync(pageId, cancellationToken);

        if (page is null)
        {
            var missing = Result.Fail<ContentImage>(
                "invalid_page",
                ContentImageService.InvalidPageMessage);

            if (WantsJson())
            {
                return Ok(new { ok = false, message = missing.Message, data = (object?)null });
            }

            Toast(missing);
            return RedirectToAction(nameof(Index));
        }

        var result = await images.SaveAsync(pageId, file, cancellationToken);

        if (WantsJson())
        {
            return Ok(new
            {
                ok = result.Succeeded,
                message = result.Message,
                data = result.Value is { } image
                    ? new { image.Url, image.FileName, image.Bytes }
                    : null
            });
        }

        Toast(result);

        if (result.Succeeded && result.Value is { } uploaded)
        {
            TempData["Content.ImageUrl"] = uploaded.Url;
        }

        return RedirectToAction(nameof(Edit), new { id = pageId });
    }

    private IActionResult RenderForm(ContentFormViewModel form)
    {
        ViewData["Title"] = form.IsEdit ? "Edit topic" : "New topic";

        if (form.IsEdit)
        {
            form.PublicHref = $"/content/{form.Id}";
            form.Images = images.List(form.Id);
        }
        else
        {
            form.PublicHref = string.Empty;
            form.Images = [];
        }

        return View("Form", form);
    }

    private bool WantsJson() =>
        Request.Headers.Accept.Any(value =>
            value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        || string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
}