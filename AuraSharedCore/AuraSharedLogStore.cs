using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AuraShared.Core;

public static class AuraSharedLogStore
{
    public static string OwnerDirectory(string ownerModId)
    {
        var directory = AuraSharedPaths.OwnerLogsDirectory(ownerModId);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string OwnerLogPath(string ownerModId, string fileName)
    {
        var safeName = Path.GetFileName((fileName ?? "").Trim());
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "latest.log";
        }

        return Path.Combine(OwnerDirectory(ownerModId), safeName);
    }

    public static IReadOnlyList<string> Enumerate(string ownerModId = "", string searchPattern = "*.log")
    {
        var root = string.IsNullOrWhiteSpace(ownerModId)
            ? AuraSharedPaths.LogsRootDirectory
            : OwnerDirectory(ownerModId);
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
