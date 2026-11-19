using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Content;
using OscCommittee.Web.ViewModels.Content;

namespace OscCommittee.Web.Controllers;

/// <summary>Member-facing content pages (PLAN §7.5): the published list and one page at a time.</summary>
[Route("content")]
[Authorize(Policy = "Member")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContentController(
    IContentPageService content,
    IMarkdownService markdown,
    ITimeZoneService timeZone) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Topics";

        var rows = await content.GetPublishedAsync(cancellationToken);

        return View(new ContentIndexViewModel
        {
            Pages = rows
                .Select(row => new ContentCardViewModel
                {
                    Id = row.Id,
                    Title = row.Title,
                    UpdatedLabel = timeZone.FormatLocal(row.UpdatedAtUtc, "d MMM yyyy"),
                    Href = Url.Content($"~/content/{row.Id}")
                })
                .ToList()
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        var page = await content.FindPublishedAsync(id, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        ViewData["Title"] = page.Title;

        var rendered = markdown.ToContent(page.BodyMarkdown);

        return View("Page", new ContentPageViewModel
        {
            Title = page.Title,
            BodyHtml = rendered.Html,
            Headings = rendered.Headings,
            UpdatedLabel = timeZone.FormatLocal(page.UpdatedAtUtc, "d MMM yyyy")
        });
    }
}