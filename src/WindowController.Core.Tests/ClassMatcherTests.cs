using WindowController.Core;

namespace WindowController.Core.Tests;

public class ClassMatcherTests
{
    // ────────────────── NormalizeForMatch ──────────────────

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeForMatch_EmptyOrNull_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, ClassMatcher.NormalizeForMatch(input ?? ""));
    }

    [Fact]
    public void NormalizeForMatch_AvaloniaGuid_ReturnsWildcard()
    {
        var result = ClassMatcher.NormalizeForMatch("Avalonia-12345678-1234-1234-1234-123456789abc");
        Assert.Equal("Avalonia-*", result);
    }

    [Fact]
    public void NormalizeForMatch_HwndWrapper_ReturnsWildcard()
    {
        var result = ClassMatcher.NormalizeForMatch("HwndWrapper[SomeApp;;12345]");
        Assert.Equal("HwndWrapper[*", result);
    }

    [Fact]
    public void NormalizeForMatch_ChromeWidgetWin_ReturnsAsIs()
    {
        var result = ClassMatcher.NormalizeForMatch("Chrome_WidgetWin_1");
        Assert.Equal("Chrome_WidgetWin_1", result);
    }

    // ────────────────── Matches ──────────────────

    [Theory]
    [InlineData("Chrome_WidgetWin_1", "", true)]      // empty expected = always match
    [InlineData("Chrome_WidgetWin_1", "Chrome_WidgetWin_1", true)]
    [InlineData("Chrome_WidgetWin_1", "Chrome_WidgetWin_2", false)]
    public void Matches_ExactOrEmpty(string actual, string expected, bool shouldMatch)
    {
        Assert.Equal(shouldMatch, ClassMatcher.Matches(actual, expected));
    }

    [Fact]
    public void Matches_AvaloniaFamily()
    {
        Assert.True(ClassMatcher.Matches(
            "Avalonia-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "Avalonia-*"));
    }

    [Fact]
    public void Matches_HwndWrapperFamily()
    {
        Assert.True(ClassMatcher.Matches(
            "HwndWrapper[MyApp;;67890]",
            "HwndWrapper[*"));
    }

    [Fact]
    public void Matches_CaseSensitive()
    {
        // Class names are case-sensitive in Windows
        Assert.False(ClassMatcher.Matches("chrome_widgetwin_1", "Chrome_WidgetWin_1"));
    }
}
