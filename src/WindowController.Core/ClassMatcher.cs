using System.Text.RegularExpressions;

namespace WindowController.Core;

/// <summary>
/// Class name matching logic (handles Avalonia-*, HwndWrapper[*, etc.)
/// </summary>
public static class ClassMatcher
{
    /// <summary>
    /// Normalize a class name for storage/matching.
    /// Avalonia GUIDs and HwndWrapper names change per-session.
    /// </summary>
    public static string NormalizeForMatch(string className)
    {
        if (string.IsNullOrEmpty(className))
            return "";

        // Avalonia-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx -> Avalonia-*
        if (Regex.IsMatch(className, @"^Avalonia-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase))
            return "Avalonia-*";

        // HwndWrapper[...] -> HwndWrapper[*
        if (className.StartsWith("HwndWrapper[", StringComparison.Ordinal))
            return "HwndWrapper[*";

        return className;
    }

    /// <summary>
    /// Check if an actual class name matches the expected (possibly wildcarded) pattern.
    /// </summary>
    public static bool Matches(string actual, string expected)
    {
        if (string.IsNullOrEmpty(expected))
            return true;

        // Avalonia family
        if (expected.StartsWith("Avalonia-", StringComparison.OrdinalIgnoreCase))
            return actual.StartsWith("Avalonia-", StringComparison.OrdinalIgnoreCase);

        // Wildcard suffix
        if (expected.EndsWith('*'))
        {
            var prefix = expected[..^1];
            return actual.StartsWith(prefix, StringComparison.Ordinal);
        }

        return string.Equals(actual, expected, StringComparison.Ordinal);
    }
}
