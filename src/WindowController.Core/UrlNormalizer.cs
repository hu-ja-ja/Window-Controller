using System.Text.RegularExpressions;

namespace WindowController.Core;

/// <summary>
/// URL normalization for matching (equivalent to AHK _NormalizeUrlForMatch).
/// </summary>
public static partial class UrlNormalizer
{
    /// <summary>
    /// Normalize a URL for matching purposes.
    /// Strips query/fragment, lowercases scheme+host.
    /// </summary>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        var trimmed = Regex.Replace(url.Trim(), @"\s+", " ");

        // Remove query and fragment
        trimmed = Regex.Replace(trimmed, @"[#?].*$", "");

        // scheme://host/path
        var httpMatch = HttpUrlRegex().Match(trimmed);
        if (httpMatch.Success)
        {
            var scheme = httpMatch.Groups[1].Value.ToLowerInvariant();
            var host = httpMatch.Groups[2].Value.ToLowerInvariant();
            var path = httpMatch.Groups[3].Success ? httpMatch.Groups[3].Value : "/";
            return $"{scheme}://{host}{path}";
        }

        // about:xxx
        var aboutMatch = AboutRegex().Match(trimmed);
        if (aboutMatch.Success)
            return aboutMatch.Groups[1].Value.ToLowerInvariant();

        // file:
        var fileMatch = FileRegex().Match(trimmed);
        if (fileMatch.Success)
            return "file:" + fileMatch.Groups[1].Value.ToLowerInvariant();

        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Extract host from a normalized URL key.
    /// </summary>
    public static string GetHost(string? urlKey)
    {
        if (string.IsNullOrEmpty(urlKey))
            return "";

        var m = HostRegex().Match(urlKey);
        return m.Success ? m.Groups[2].Value.ToLowerInvariant() : "";
    }

    [GeneratedRegex(@"^(https?|ws|wss|ftp)://([^/]+)(/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex HttpUrlRegex();

    [GeneratedRegex(@"^(about:\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AboutRegex();

    [GeneratedRegex(@"^file:/{0,3}(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileRegex();

    [GeneratedRegex(@"^(https?|ws|wss|ftp)://([^/]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HostRegex();
}
