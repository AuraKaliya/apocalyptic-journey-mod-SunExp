using System.IO;

namespace AuraShared.Core;

public static class AuraSharedPaths
{
    public static string NormalizeRelativePath(string value)
    {
        return (value ?? "").Trim().Trim('"').Replace('\\', '/').TrimStart('/');
    }

    public static string SafeSegment(string value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    }
}
