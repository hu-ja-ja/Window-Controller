using WindowController.Core.Models;

namespace WindowController.Core;

/// <summary>
/// Represents a running window with its properties.
/// </summary>
public class WindowCandidate
{
    public nint Hwnd { get; init; }
    public string Exe { get; init; } = "";
    public string Class { get; init; } = "";
    public string Title { get; init; } = "";
    public string Path { get; init; } = "";
    public string Url { get; init; } = "";
    public string CommandLine { get; init; } = "";
}

/// <summary>
/// Result of window matching with confidence score.
/// </summary>
public record MatchResult(nint Hwnd, int Score, bool IsAmbiguous);

/// <summary>
/// Scores candidates against a window entry for matching.
/// </summary>
public static class WindowMatcher
{
    // ── Scoring constants ──
    private const int ScorePath = 60;
    private const int ScoreTitleExact = 30;
    private const int ScoreTitlePartial = 10;
    private const int ScoreBrowserUserDataDir = 70;
    private const int ScoreBrowserProfileDir = 50;
    private const int ScoreUrlExact = 60;
    private const int ScoreUrlHostOnly = 20;
    private const int AmbiguityGap = 10;
    private const int AmbiguityMinScore = 50;
    private const int SingleMatchScore = 100;

    /// <summary>
    /// Find the best matching window from candidates for a given entry.
    /// Returns null if no candidate matches the required criteria (exe+class).
    /// </summary>
    public static MatchResult? FindBest(
        WindowEntry entry,
        IReadOnlyList<WindowCandidate> candidates,
        bool forSync = false)
    {
        if (string.IsNullOrEmpty(entry.Match.Exe))
            return null;

        var exeLower = entry.Match.Exe.ToLowerInvariant();
        var wantUrlKey = !string.IsNullOrEmpty(entry.Match.UrlKey)
            ? entry.Match.UrlKey
            : UrlNormalizer.Normalize(entry.Match.Url);
        var wantHost = UrlNormalizer.GetHost(wantUrlKey);
        var browserKind = BrowserIdentifier.GetBrowserKind(exeLower);

        // Phase 1: exe + class filter (required)
        var filtered = new List<(WindowCandidate Cand, int Score)>();
        foreach (var c in candidates)
        {
            if (!string.Equals(c.Exe, entry.Match.Exe, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(entry.Match.Class) &&
                !ClassMatcher.Matches(c.Class, entry.Match.Class))
                continue;

            filtered.Add((c, 0));
        }

        if (filtered.Count == 0)
            return null;

        if (filtered.Count == 1 && !forSync)
            return new MatchResult(filtered[0].Cand.Hwnd, SingleMatchScore, false);

        // Phase 2: scoring
        var scored = new List<(WindowCandidate Cand, int Score)>();
        foreach (var (c, _) in filtered)
        {
            int score = 0;

            // Path match (strong)
            if (!string.IsNullOrEmpty(entry.Path) && !string.IsNullOrEmpty(c.Path) &&
                string.Equals(PathNormalizer.Normalize(c.Path), PathNormalizer.Normalize(entry.Path),
                    StringComparison.OrdinalIgnoreCase))
                score += ScorePath;

            // Title match
            if (!string.IsNullOrEmpty(entry.Match.Title))
            {
                if (c.Title == entry.Match.Title)
                    score += ScoreTitleExact;
                else if (c.Title.Contains(entry.Match.Title, StringComparison.Ordinal))
                    score += ScoreTitlePartial;
            }

            // Browser identity match
            if (entry.Match.Browser is { } wantBrowser && BrowserIdentifier.IsBrowser(exeLower))
            {
                var candIdent = BrowserIdentifier.ExtractIdentity(exeLower, c.CommandLine);
                if (candIdent != null)
                {
                    // Chromium
                    if (!string.IsNullOrEmpty(wantBrowser.UserDataDir) && !string.IsNullOrEmpty(candIdent.UserDataDir) &&
                        string.Equals(wantBrowser.UserDataDir, candIdent.UserDataDir, StringComparison.OrdinalIgnoreCase))
                        score += ScoreBrowserUserDataDir;
                    if (!string.IsNullOrEmpty(wantBrowser.ProfileDirectory) && !string.IsNullOrEmpty(candIdent.ProfileDirectory) &&
                        wantBrowser.ProfileDirectory == candIdent.ProfileDirectory)
                        score += ScoreBrowserProfileDir;

                    // Firefox
                    if (!string.IsNullOrEmpty(wantBrowser.ProfileDir) && !string.IsNullOrEmpty(candIdent.ProfileDir) &&
                        string.Equals(wantBrowser.ProfileDir, candIdent.ProfileDir, StringComparison.OrdinalIgnoreCase))
                        score += ScoreBrowserUserDataDir;
                    if (!string.IsNullOrEmpty(wantBrowser.ProfileName) && !string.IsNullOrEmpty(candIdent.ProfileName) &&
                        wantBrowser.ProfileName == candIdent.ProfileName)
                        score += ScoreBrowserProfileDir;
                }
            }

            // URL match (chromium only — non-intrusive. Firefox URL is not available non-intrusively)
            if (!string.IsNullOrEmpty(wantUrlKey) && !string.IsNullOrEmpty(c.Url))
            {
                var candUrlKey = UrlNormalizer.Normalize(c.Url);
                if (!string.IsNullOrEmpty(candUrlKey))
                {
                    if (candUrlKey == wantUrlKey)
                        score += ScoreUrlExact;
                    else if (!string.IsNullOrEmpty(wantHost) && UrlNormalizer.GetHost(candUrlKey) == wantHost)
                        score += ScoreUrlHostOnly;
                }
            }

            scored.Add((c, score));
        }

        // Sort by score descending
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = scored[0];

        // Ambiguity check: for sync, if top two are close and both have weak scores, mark ambiguous
        bool ambiguous = false;
        if (scored.Count > 1)
        {
            var second = scored[1];
            if (best.Score - second.Score <= AmbiguityGap && best.Score < AmbiguityMinScore)
                ambiguous = true;
        }

        if (forSync && ambiguous)
            return null; // Safe side: exclude ambiguous matches from sync

        return new MatchResult(best.Cand.Hwnd, best.Score, ambiguous);
    }
}
