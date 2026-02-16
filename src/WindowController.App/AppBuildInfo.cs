using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WindowController.App;

internal static class AppBuildInfo
{
    private static Assembly Assembly => typeof(AppBuildInfo).Assembly;

    public static string InformationalVersion
        => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "不明";

    public static string FileVersion
        => Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? "不明";

    public static string AssemblyVersion
        => Assembly.GetName().Version?.ToString() ?? "不明";

    public static IReadOnlyDictionary<string, string> Metadata
        => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => !string.IsNullOrWhiteSpace(a.Key))
            .GroupBy(a => a.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value ?? "");

    public static string GetMetadata(string key, string fallback = "不明")
        => Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
}
