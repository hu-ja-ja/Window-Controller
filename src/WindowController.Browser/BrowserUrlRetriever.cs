using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Serilog;

namespace WindowController.Browser;

/// <summary>
/// Retrieves browser URL via UI Automation (no clipboard, no focus stealing).
/// Supports Chromium-based browsers and Firefox/Floorp.
/// </summary>
public class BrowserUrlRetriever
{
    private readonly ILogger _log;

    public BrowserUrlRetriever(ILogger logger)
    {
        _log = logger;
    }

    /// <summary>
    /// Try to get the URL from a browser window using UI Automation.
    /// Returns empty string if not obtainable. Never throws.
    /// </summary>
    public string TryGetUrl(nint hwnd, string exe)
    {
        try
        {
            var exeLower = exe.ToLowerInvariant();
            var kind = Core.BrowserIdentifier.GetBrowserKind(exeLower);
            if (string.IsNullOrEmpty(kind))
                return "";

            using var automation = new UIA3Automation();
            var window = automation.FromHandle(hwnd);
            if (window == null)
                return "";

            if (kind == "chromium")
                return TryGetChromiumUrl(window, automation);
            if (kind == "firefox")
                return TryGetFirefoxUrl(window, automation);

            return "";
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "BrowserUrlRetriever.TryGetUrl failed for hwnd {Hwnd} exe {Exe}", hwnd, exe);
            return "";
        }
    }

    private string TryGetChromiumUrl(AutomationElement window, UIA3Automation automation)
    {
        try
        {
            // Chromium: Look for the address bar (Edit control with specific automation ID or name)
            var cf = automation.ConditionFactory;

            // Try by ControlType=Edit and Name containing "address" or AutomationId
            var edits = window.FindAllDescendants(cf.ByControlType(ControlType.Edit));
            foreach (var edit in edits)
            {
                try
                {
                    var name = edit.Name ?? "";
                    var automationId = edit.AutomationId ?? "";

                    // Skip search boxes etc. - Chromium address bar patterns
                    if (automationId == "addressBarInput" ||
                        automationId == "view_id_86" ||
                        name.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("アドレス", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("URL", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = GetEditValue(edit);
                        if (!string.IsNullOrEmpty(value) && LooksLikeUrl(value))
                            return value;
                    }
                }
                catch { /* skip problematic elements */ }
            }

            // Fallback: try all edits and pick one that looks like a URL
            foreach (var edit in edits)
            {
                try
                {
                    var value = GetEditValue(edit);
                    if (!string.IsNullOrEmpty(value) && LooksLikeUrl(value))
                        return value;
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "TryGetChromiumUrl failed");
        }
        return "";
    }

    private string TryGetFirefoxUrl(AutomationElement window, UIA3Automation automation)
    {
        try
        {
            var cf = automation.ConditionFactory;

            // Firefox: address bar has AutomationId "urlbar-input" or ControlType=Edit
            var edits = window.FindAllDescendants(cf.ByControlType(ControlType.Edit));
            foreach (var edit in edits)
            {
                try
                {
                    var automationId = edit.AutomationId ?? "";
                    var name = edit.Name ?? "";

                    // Firefox/Floorp address bar
                    if (automationId == "urlbar-input" ||
                        name.Contains("URL", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("アドレス", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Search with", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = GetEditValue(edit);
                        if (!string.IsNullOrEmpty(value) && LooksLikeUrl(value))
                            return value;
                    }
                }
                catch { /* skip */ }
            }

            // Fallback: any edit with a URL-like value
            foreach (var edit in edits)
            {
                try
                {
                    var value = GetEditValue(edit);
                    if (!string.IsNullOrEmpty(value) && LooksLikeUrl(value))
                        return value;
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "TryGetFirefoxUrl failed");
        }
        return "";
    }

    private static string GetEditValue(AutomationElement edit)
    {
        // Try Value pattern first
        if (edit.Patterns.Value.IsSupported)
        {
            var val = edit.Patterns.Value.Pattern.Value.Value;
            if (!string.IsNullOrEmpty(val))
                return val.Trim();
        }

        // Try Name as fallback
        var name = edit.Name;
        if (!string.IsNullOrEmpty(name) && LooksLikeUrl(name))
            return name.Trim();

        return "";
    }

    private static bool LooksLikeUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        // Common URL schemes
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("brave://", StringComparison.OrdinalIgnoreCase))
            return true;

        // domain.tld/path pattern (no scheme but looks like URL)
        if (trimmed.Contains('.') && !trimmed.Contains(' ') && trimmed.Length < 2048)
            return true;

        return false;
    }
}
