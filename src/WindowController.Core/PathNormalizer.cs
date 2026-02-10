namespace WindowController.Core;

/// <summary>
/// Normalize Windows paths (fix double-backslash issues from old data).
/// </summary>
public static class PathNormalizer
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        // Drive path: C:\\\\foo -> C:\foo
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        {
            while (path.Contains("\\\\"))
                path = path.Replace("\\\\", "\\");
            return path;
        }

        // UNC: \\\\server\\share -> \\server\share
        if (path.StartsWith("\\\\"))
        {
            var tail = path[2..];
            while (tail.Contains("\\\\"))
                tail = tail.Replace("\\\\", "\\");
            return "\\\\" + tail;
        }

        return path;
    }
}
