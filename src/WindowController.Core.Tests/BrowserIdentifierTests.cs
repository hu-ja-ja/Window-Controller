using WindowController.Core;
using WindowController.Core.Models;

namespace WindowController.Core.Tests;

public class BrowserIdentifierTests
{
    // ────────────────── GetBrowserKind ──────────────────

    [Theory]
    [InlineData("chrome.exe", "chromium")]
    [InlineData("msedge.exe", "chromium")]
    [InlineData("brave.exe", "chromium")]
    [InlineData("vivaldi.exe", "chromium")]
    [InlineData("firefox.exe", "firefox")]
    [InlineData("floorp.exe", "firefox")]
    [InlineData("notepad.exe", "")]
    [InlineData("", "")]
    public void GetBrowserKind_ReturnsCorrectKind(string exe, string expected)
    {
        Assert.Equal(expected, BrowserIdentifier.GetBrowserKind(exe));
    }

    // ────────────────── IsBrowser ──────────────────

    [Theory]
    [InlineData("chrome.exe", true)]
    [InlineData("notepad.exe", false)]
    public void IsBrowser_Correct(string exe, bool expected)
    {
        Assert.Equal(expected, BrowserIdentifier.IsBrowser(exe));
    }

    // ────────────────── ExtractIdentity ──────────────────

    [Fact]
    public void ExtractIdentity_EmptyCommandLine_ReturnsNull()
    {
        Assert.Null(BrowserIdentifier.ExtractIdentity("chrome.exe", ""));
    }

    [Fact]
    public void ExtractIdentity_Chromium_UserDataDir()
    {
        var cmd = @"""C:\Chrome\chrome.exe"" --user-data-dir=""C:\Users\me\data"" --profile-directory=""Profile 1""";
        var result = BrowserIdentifier.ExtractIdentity("chrome.exe", cmd);

        Assert.NotNull(result);
        Assert.Equal("chromium", result.Kind);
        Assert.Contains("Users", result.UserDataDir!);
        Assert.Equal("Profile 1", result.ProfileDirectory);
    }

    [Fact]
    public void ExtractIdentity_Chromium_UserDataDirEquals()
    {
        var cmd = @"""C:\Chrome\chrome.exe"" --user-data-dir=C:\data --profile-directory=Default";
        var result = BrowserIdentifier.ExtractIdentity("chrome.exe", cmd);

        Assert.NotNull(result);
        Assert.Equal("Default", result.ProfileDirectory);
    }

    [Fact]
    public void ExtractIdentity_Firefox_Profile()
    {
        var cmd = @"""C:\Firefox\firefox.exe"" -P ""work"" -profile ""C:\profiles\work""";
        var result = BrowserIdentifier.ExtractIdentity("firefox.exe", cmd);

        Assert.NotNull(result);
        Assert.Equal("firefox", result.Kind);
        Assert.Equal("work", result.ProfileName);
    }

    [Fact]
    public void ExtractIdentity_NonBrowser_ReturnsNull()
    {
        var result = BrowserIdentifier.ExtractIdentity("notepad.exe", "notepad.exe test.txt");
        Assert.Null(result);
    }
}
