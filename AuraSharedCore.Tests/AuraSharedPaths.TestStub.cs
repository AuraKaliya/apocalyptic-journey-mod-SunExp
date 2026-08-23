using System.IO;

namespace AuraShared.Core;

public static class AuraSharedPaths
{
    public static string RootDirectory { get; set; } = Path.GetTempPath();

    public static bool IsInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path ?? "");
        var fullDirectory = Path.GetFullPath(directory ?? "")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

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
