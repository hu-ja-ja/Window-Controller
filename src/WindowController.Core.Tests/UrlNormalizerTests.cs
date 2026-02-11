using WindowController.Core;

namespace WindowController.Core.Tests;

public class UrlNormalizerTests
{
    // ────────────────── Normalize ──────────────────

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_EmptyOrNull_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_HttpUrl_LowercasesSchemeAndHost()
    {
        var result = UrlNormalizer.Normalize("HTTPS://Example.COM/Path");
        Assert.Equal("https://example.com/Path", result);
    }

    [Fact]
    public void Normalize_StripsQueryString()
    {
        var result = UrlNormalizer.Normalize("https://example.com/page?id=1&tab=2");
        Assert.Equal("https://example.com/page", result);
    }

    [Fact]
    public void Normalize_StripsFragment()
    {
        var result = UrlNormalizer.Normalize("https://example.com/page#section");
        Assert.Equal("https://example.com/page", result);
    }

    [Fact]
    public void Normalize_StripsQueryAndFragment()
    {
        var result = UrlNormalizer.Normalize("https://example.com/page?q=1#top");
        Assert.Equal("https://example.com/page", result);
    }

    [Fact]
    public void Normalize_NoPath_AddsSlash()
    {
        var result = UrlNormalizer.Normalize("https://example.com");
        Assert.Equal("https://example.com/", result);
    }

    [Fact]
    public void Normalize_AboutUrl()
    {
        var result = UrlNormalizer.Normalize("about:blank");
        Assert.Equal("about:blank", result);
    }

    [Fact]
    public void Normalize_FileUrl()
    {
        var result = UrlNormalizer.Normalize("file:///C:/docs/readme.txt");
        Assert.StartsWith("file:", result);
    }

    [Fact]
    public void Normalize_PreservesPath()
    {
        var result = UrlNormalizer.Normalize("https://github.com/user/Repo");
        Assert.Equal("https://github.com/user/Repo", result);
    }

    // ────────────────── GetHost ──────────────────

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void GetHost_EmptyOrNull_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.GetHost(input));
    }

    [Fact]
    public void GetHost_HttpsUrl_ReturnsHost()
    {
        var result = UrlNormalizer.GetHost("https://example.com/page");
        Assert.Equal("example.com", result);
    }

    [Fact]
    public void GetHost_AboutUrl_ReturnsEmpty()
    {
        var result = UrlNormalizer.GetHost("about:blank");
        Assert.Equal("", result);
    }
}
