using System.Text;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OscCommittee.Web.Services.Common;

public sealed class MarkdownService : IMarkdownService
{
    // Build once: the pipeline is immutable and shared across requests.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdown.ToHtml(markdown, Pipeline);

    public MarkdownContent ToContent(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return MarkdownContent.Empty;
        }

        var document = Markdown.Parse(markdown, Pipeline);

        var headings = document
            .Descendants<HeadingBlock>()
            .Select(heading => new MarkdownHeading(
                heading.Level,
                PlainText(heading),
                heading.GetAttributes().Id ?? string.Empty))
            .Where(heading => !string.IsNullOrWhiteSpace(heading.Text) && !string.IsNullOrWhiteSpace(heading.Id))
            .ToList();

        return new MarkdownContent(Markdown.ToHtml(document, Pipeline), headings);
    }

    /// <summary>Heading text as a reader sees it: inline literals only, no markup.</summary>
    private static string PlainText(HeadingBlock heading)
    {
        if (heading.Inline is null)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var literal in heading.Inline.Descendants<LiteralInline>())
        {
            text.Append(literal.Content);
        }

        return text.ToString().Trim();
    }
}