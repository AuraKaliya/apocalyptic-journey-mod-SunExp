using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AuraCg.Shared;

internal static class AuraCgMediaPathResolver
{
    public static string NormalizeRelativeResourcePath(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
    }

    public static string NormalizeBundleId(string value)
    {
        return NormalizeRelativeResourcePath(value);
    }

    public static IReadOnlyList<string> ResolveSequenceFramePaths(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.GetFiles(path)
                .Where(IsSupportedSequenceFrame)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return File.Exists(path) && IsSupportedSequenceFrame(path)
            ? new[] { path }
            : Array.Empty<string>();
    }

    public static bool IsBundleSequenceAsset(string assetName, string prefix)
    {
        var normalized = NormalizeRelativeResourcePath(assetName);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            var normalizedPrefix = NormalizeRelativeResourcePath(prefix).TrimEnd('/') + "/";
            if (!normalized.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
                && normalized.IndexOf("/" + normalizedPrefix, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return IsSupportedSequenceFrame(normalized);
    }

    public static bool IsSupportedSequenceFrame(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}
