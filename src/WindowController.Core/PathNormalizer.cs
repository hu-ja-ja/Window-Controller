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
            return NormalizeBackslashes(path);
        }

        // UNC: \\\\server\\share -> \\server\share
        if (path.StartsWith("\\\\"))
        {
            var tail = path.TrimStart('\\');
            return "\\\\" + NormalizeBackslashes(tail);
        }

        return path;
    }

    /// <summary>
    /// Replace consecutive backslashes with a single backslash in one pass.
    /// </summary>
    private static string NormalizeBackslashes(string value)
    {
        // Fast-path: no double backslash present
        if (!value.Contains("\\\\"))
            return value;

        var sb = new System.Text.StringBuilder(value.Length);
        bool prevWasBackslash = false;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                if (!prevWasBackslash)
                    sb.Append(ch);
                prevWasBackslash = true;
            }
            else
            {
                sb.Append(ch);
                prevWasBackslash = false;
            }
        }
        return sb.ToString();
    }
}
