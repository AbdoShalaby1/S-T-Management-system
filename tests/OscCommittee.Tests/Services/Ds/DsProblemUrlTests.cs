using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Ds;

namespace OscCommittee.Tests.Services.Ds;

public sealed class DsProblemUrlTests
{
    [Theory]
    [InlineData("https://leetcode.com/problems/two-sum/", "two-sum")]
    [InlineData("https://leetcode.com/problems/two-sum", "two-sum")]
    [InlineData("https://www.leetcode.com/problems/two-sum/description/", "two-sum")]
    [InlineData("leetcode.com/problems/3sum/", "3sum")]
    [InlineData("  https://leetcode.com/problems/valid-parentheses/?envType=study-plan  ", "valid-parentheses")]
    public void LeetCode_urls_resolve_to_the_slug(string raw, string slug)
    {
        var result = DsProblemUrl.Parse(raw);

        Assert.True(result.Succeeded);
        Assert.Equal(Platform.LeetCode, result.Value!.Platform);
        Assert.Equal(slug, result.Value.ExternalId);
        Assert.Equal($"https://leetcode.com/problems/{slug}/", result.Value.CanonicalUrl);
    }

    [Theory]
    [InlineData("https://codeforces.com/problemset/problem/4/A", "4:A")]
    [InlineData("https://codeforces.com/problemset/problem/1811/F1/", "1811:F1")]
    [InlineData("https://codeforces.com/contest/4/problem/a", "4:A")]
    [InlineData("codeforces.com/contest/1900/problem/B2", "1900:B2")]
    [InlineData("https://m.codeforces.com/problemset/problem/1/A", "1:A")]
    public void Codeforces_urls_resolve_to_contest_and_index(string raw, string externalId)
    {
        var result = DsProblemUrl.Parse(raw);

        Assert.True(result.Succeeded);
        Assert.Equal(Platform.Codeforces, result.Value!.Platform);
        Assert.Equal(externalId, result.Value.ExternalId);
        Assert.StartsWith("https://codeforces.com/problemset/problem/", result.Value.CanonicalUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://example.com/problems/two-sum/")]
    [InlineData("https://leetcode.com/problemset/all/")]
    [InlineData("https://leetcode.com/problems/")]
    [InlineData("https://codeforces.com/problemset")]
    [InlineData("https://codeforces.com/contest/abc/problem/A")]
    [InlineData("https://codeforces.com/problemset/problem/0/A")]
    public void Unsupported_urls_fail_with_a_readable_message(string raw)
    {
        var result = DsProblemUrl.Parse(raw);

        Assert.True(result.Failed);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
    }

    [Fact]
    public void Canonical_rebuilds_both_platforms()
    {
        Assert.Equal(
            "https://leetcode.com/problems/two-sum/",
            DsProblemUrl.Canonical(Platform.LeetCode, "Two-Sum"));

        Assert.Equal(
            "https://codeforces.com/problemset/problem/4/A",
            DsProblemUrl.Canonical(Platform.Codeforces, "4:a"));

        Assert.Equal(string.Empty, DsProblemUrl.Canonical(Platform.Codeforces, "nonsense"));
    }
}