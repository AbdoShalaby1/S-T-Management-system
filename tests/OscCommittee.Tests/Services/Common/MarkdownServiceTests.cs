using OscCommittee.Web.Services.Common;

namespace OscCommittee.Tests.Services.Common;

public sealed class MarkdownServiceTests
{
    private readonly MarkdownService _markdown = new();

    [Fact]
    public void HeadingsAreExtractedForTheTableOfContents()
    {
        var content = _markdown.ToContent("## Goal\n\nText.\n\n### Requirements\n\n- One");

        Assert.Equal(2, content.Headings.Count);
        Assert.Equal("Goal", content.Headings[0].Text);
        Assert.Equal("goal", content.Headings[0].Id);
        Assert.Equal(3, content.Headings[1].Level);
        Assert.Equal("requirements", content.Headings[1].Id);
    }

    [Fact]
    public void HeadingTextDropsInlineMarkup()
    {
        var content = _markdown.ToContent("## Read the **file** now");

        Assert.Equal("Read the file now", content.Headings[0].Text);
    }

    [Fact]
    public void RawHtmlNeverReachesTheOutput()
    {
        var content = _markdown.ToContent("<script>alert(1)</script>\n\nSafe text.");

        Assert.DoesNotContain("<script", content.Html);
        Assert.Contains("Safe text.", content.Html);
    }

    [Fact]
    public void EmptyMarkdownRendersNothing()
    {
        Assert.Equal(string.Empty, _markdown.ToHtml("   "));
        Assert.Empty(_markdown.ToContent(null).Headings);
        Assert.Equal(string.Empty, _markdown.ToContent(null).Html);
    }
}