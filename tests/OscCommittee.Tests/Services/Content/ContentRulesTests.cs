using OscCommittee.Web.Services.Content;

namespace OscCommittee.Tests.Services.Content;

public sealed class ContentRulesTests
{
    [Theory]
    [InlineData("Header Image.PNG", "header-image")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\temp\\diagram.png", "diagram")]
    [InlineData("!!!.png", "image")]
    [InlineData(null, "image")]
    public void SafeFileStemKeepsOnlySafeCharacters(string? fileName, string expected) =>
        Assert.Equal(expected, ContentRules.SafeFileStem(fileName));

    [Fact]
    public void SafeFileStemCapsTheLength() =>
        Assert.Equal(60, ContentRules.SafeFileStem(new string('a', 120) + ".png").Length);

    [Fact]
    public void LooksLikeImageChecksTheSignatureNotJustTheExtension()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
        var gif = "GIF89a"u8.ToArray();
        var webp = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0 };

        Assert.True(ContentRules.LooksLikeImage(".png", png));
        Assert.True(ContentRules.LooksLikeImage(".jpg", jpeg));
        Assert.True(ContentRules.LooksLikeImage(".jpeg", jpeg));
        Assert.True(ContentRules.LooksLikeImage(".gif", gif));
        Assert.True(ContentRules.LooksLikeImage(".webp", webp));

        Assert.False(ContentRules.LooksLikeImage(".png", jpeg));
        Assert.False(ContentRules.LooksLikeImage(".png", "not an image"u8));
        Assert.False(ContentRules.LooksLikeImage(".bmp", png));
        Assert.False(ContentRules.LooksLikeImage(".png", []));
    }
}