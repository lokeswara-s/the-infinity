using System.Text.RegularExpressions;

namespace TheInfinity.Core.Models;

public static partial class WindowIdentity
{
    public static string BuildKey(string? executablePath, string? processName, string? windowClass, string? title)
    {
        var exe = NormalizePath(executablePath);
        if (string.IsNullOrEmpty(exe))
            exe = (processName ?? "").Trim().ToLowerInvariant();
        var cls = (windowClass ?? "").Trim().ToLowerInvariant();
        return $"{exe}|{cls}|{NormalizeTitle(title)}";
    }

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";
        return Whitespace().Replace(title.Trim().ToLowerInvariant(), " ");
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFullPath(path).Trim().ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
