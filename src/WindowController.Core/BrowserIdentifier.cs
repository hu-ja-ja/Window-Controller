using System.Text.RegularExpressions;
using WindowController.Core.Models;

namespace WindowController.Core;

/// <summary>
/// Identify browser kind and extract profile identity from command line.
/// </summary>
public static partial class BrowserIdentifier
{
    public static string GetBrowserKind(string exeLower) => exeLower switch
    {
        "chrome.exe" or "msedge.exe" or "brave.exe" or "vivaldi.exe" => "chromium",
        "firefox.exe" or "floorp.exe" => "firefox",
        _ => ""
    };

    public static bool IsBrowser(string exeLower) => GetBrowserKind(exeLower) != "";

    public static BrowserIdentity? ExtractIdentity(string exeLower, string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
            return null;

        var kind = GetBrowserKind(exeLower);
        if (kind == "chromium")
        {
            var ud = TryGetCmdArg(commandLine, "--user-data-dir");
            var pd = TryGetCmdArg(commandLine, "--profile-directory");
            if (!string.IsNullOrEmpty(ud) || !string.IsNullOrEmpty(pd))
            {
                return new BrowserIdentity
                {
                    Kind = "chromium",
                    UserDataDir = string.IsNullOrEmpty(ud) ? null : PathNormalizer.Normalize(ud),
                    ProfileDirectory = string.IsNullOrEmpty(pd) ? null : pd
                };
            }
        }
        else if (kind == "firefox")
        {
            var profDir = TryGetCmdArg(commandLine, "-profile");
            var profName = TryGetCmdArg(commandLine, "-P");
            if (!string.IsNullOrEmpty(profDir) || !string.IsNullOrEmpty(profName))
            {
                return new BrowserIdentity
                {
                    Kind = "firefox",
                    ProfileDir = string.IsNullOrEmpty(profDir) ? null : PathNormalizer.Normalize(profDir),
                    ProfileName = string.IsNullOrEmpty(profName) ? null : profName
                };
            }
        }

        return null;
    }

    private static string TryGetCmdArg(string cmd, string key)
    {
        if (string.IsNullOrEmpty(cmd))
            return "";

        // --key="value" or --key=value
        var pattern1 = $@"(?:^|\s)({Regex.Escape(key)})=(""[^""]+""|\S+)";
        var m1 = Regex.Match(cmd, pattern1, RegexOptions.IgnoreCase);
        if (m1.Success)
            return m1.Groups[2].Value.Trim('"');

        // --key "value" or --key value
        var pattern2 = $@"(?:^|\s)({Regex.Escape(key)})\s+(""[^""]+""|\S+)";
        var m2 = Regex.Match(cmd, pattern2, RegexOptions.IgnoreCase);
        if (m2.Success)
            return m2.Groups[2].Value.Trim('"');

        return "";
    }
}
