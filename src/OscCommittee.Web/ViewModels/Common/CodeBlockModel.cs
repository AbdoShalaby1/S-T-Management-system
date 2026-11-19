namespace OscCommittee.Web.ViewModels.Common;

/// <summary>Input for the shared Prism code block partial.</summary>
public sealed record CodeBlockModel(string Code, string? FileName = null, string Language = "csharp")
{
    /// <summary>Stable element id so the copy button can target this block.</summary>
    public string ElementId { get; init; } = $"code-{Guid.NewGuid():N}";
}