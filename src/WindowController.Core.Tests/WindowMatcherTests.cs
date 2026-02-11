using WindowController.Core;
using WindowController.Core.Models;

namespace WindowController.Core.Tests;

public class WindowMatcherTests
{
    private static WindowEntry MakeEntry(
        string exe = "notepad.exe",
        string cls = "Notepad",
        string title = "Untitled",
        string path = @"C:\Windows\notepad.exe",
        string url = "",
        string urlKey = "",
        BrowserIdentity? browser = null)
    {
        return new WindowEntry
        {
            Match = new MatchInfo
            {
                Exe = exe,
                Class = cls,
                Title = title,
                Url = url,
                UrlKey = urlKey,
                Browser = browser
            },
            Path = path,
            Rect = new Rect { X = 0, Y = 0, W = 800, H = 600 },
            MinMax = 0
        };
    }

    private static WindowCandidate MakeCandidate(
        nint hwnd,
        string exe = "notepad.exe",
        string cls = "Notepad",
        string title = "Untitled",
        string path = @"C:\Windows\notepad.exe",
        string url = "",
        string commandLine = "")
    {
        return new WindowCandidate
        {
            Hwnd = hwnd,
            Exe = exe,
            Class = cls,
            Title = title,
            Path = path,
            Url = url,
            CommandLine = commandLine
        };
    }

    // ────────────────── Basic matching ──────────────────

    [Fact]
    public void FindBest_NoExe_ReturnsNull()
    {
        var entry = MakeEntry(exe: "");
        var candidates = new List<WindowCandidate> { MakeCandidate(1) };
        Assert.Null(WindowMatcher.FindBest(entry, candidates));
    }

    [Fact]
    public void FindBest_NoCandidates_ReturnsNull()
    {
        var entry = MakeEntry();
        Assert.Null(WindowMatcher.FindBest(entry, new List<WindowCandidate>()));
    }

    [Fact]
    public void FindBest_ExeMismatch_ReturnsNull()
    {
        var entry = MakeEntry(exe: "notepad.exe");
        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, exe: "calc.exe", cls: "Notepad")
        };
        Assert.Null(WindowMatcher.FindBest(entry, candidates));
    }

    [Fact]
    public void FindBest_ClassMismatch_ReturnsNull()
    {
        var entry = MakeEntry(exe: "notepad.exe", cls: "Notepad");
        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, exe: "notepad.exe", cls: "DifferentClass")
        };
        Assert.Null(WindowMatcher.FindBest(entry, candidates));
    }

    [Fact]
    public void FindBest_SingleMatch_ReturnsIt()
    {
        var entry = MakeEntry();
        var candidates = new List<WindowCandidate> { MakeCandidate(42) };

        var result = WindowMatcher.FindBest(entry, candidates);
        Assert.NotNull(result);
        Assert.Equal((nint)42, result.Hwnd);
        Assert.False(result.IsAmbiguous);
    }

    // ────────────────── Scoring ──────────────────

    [Fact]
    public void FindBest_PathMatch_PrefersHigherScore()
    {
        var entry = MakeEntry(
            path: @"C:\Windows\notepad.exe",
            title: "test.txt");

        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, title: "other.txt", path: @"C:\Windows\notepad.exe"),
            MakeCandidate(2, title: "test.txt", path: @"C:\other\notepad.exe"),
        };

        var result = WindowMatcher.FindBest(entry, candidates);
        Assert.NotNull(result);
        // Path match (60) > Title exact match (30)
        Assert.Equal((nint)1, result.Hwnd);
    }

    [Fact]
    public void FindBest_ExactTitleBeatPartialTitle()
    {
        var entry = MakeEntry(title: "readme.md", path: "");

        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, title: "some readme.md content", path: ""),
            MakeCandidate(2, title: "readme.md", path: ""),
        };

        var result = WindowMatcher.FindBest(entry, candidates);
        Assert.NotNull(result);
        Assert.Equal((nint)2, result.Hwnd);
    }

    // ────────────────── Ambiguity ──────────────────

    [Fact]
    public void FindBest_Ambiguous_MarkedAsAmbiguous()
    {
        var entry = MakeEntry(title: "", path: "");

        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, title: "A", path: ""),
            MakeCandidate(2, title: "B", path: ""),
        };

        var result = WindowMatcher.FindBest(entry, candidates);
        Assert.NotNull(result);
        Assert.True(result.IsAmbiguous);
    }

    [Fact]
    public void FindBest_ForSync_AmbiguousReturnsNull()
    {
        var entry = MakeEntry(title: "", path: "");

        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, title: "A", path: ""),
            MakeCandidate(2, title: "B", path: ""),
        };

        var result = WindowMatcher.FindBest(entry, candidates, forSync: true);
        Assert.Null(result);
    }

    // ────────────────── URL matching ──────────────────

    [Fact]
    public void FindBest_UrlExactMatch_HighScore()
    {
        var entry = MakeEntry(
            exe: "chrome.exe",
            cls: "Chrome_WidgetWin_1",
            url: "https://github.com/user/repo",
            urlKey: "https://github.com/user/repo",
            path: "");

        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, exe: "chrome.exe", cls: "Chrome_WidgetWin_1",
                          url: "https://example.com", path: ""),
            MakeCandidate(2, exe: "chrome.exe", cls: "Chrome_WidgetWin_1",
                          url: "https://github.com/user/repo", path: ""),
        };

        var result = WindowMatcher.FindBest(entry, candidates);
        Assert.NotNull(result);
        Assert.Equal((nint)2, result.Hwnd);
    }

    [Fact]
    public void FindBest_UrlHostMatch_PartialScore()
    {
        var entry = MakeEntry(
            exe: "chrome.exe",
            cls: "Chrome_WidgetWin_1",
            url: "https://github.com/user/repo",
            urlKey: "https://github.com/user/repo",
            path: "");

        var candidates = new List<WindowCandidate>
        {
            MakeCandidate(1, exe: "chrome.exe", cls: "Chrome_WidgetWin_1",
                          url: "https://example.com", path: ""),
            MakeCandidate(2, exe: "chrome.exe", cls: "Chrome_WidgetWin_1",
                          url: "https://github.com/other/page", path: ""),
        };

        var result = WindowMatcher.FindBest(entry, candidates);
        Assert.NotNull(result);
        // Host match gives partial score → candidate 2 wins
        Assert.Equal((nint)2, result.Hwnd);
    }
}
