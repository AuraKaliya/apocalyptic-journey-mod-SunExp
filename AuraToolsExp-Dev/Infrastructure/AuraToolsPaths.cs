using System;
using System.IO;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsPaths
{
    private const string LegacyResourceDirectoryName = "ModResource";
    private static string packageDirectory = "";
    private static string dataRootDirectory = "";

    public static string PackageDirectory => packageDirectory;

    public static string DataRootDirectory => string.IsNullOrWhiteSpace(dataRootDirectory)
        ? packageDirectory
        : dataRootDirectory;

    public static string ConfigDirectory => Path.Combine(DataRootDirectory, AuraToolsIds.ConfigDirectoryName);

    public static string ResourceDirectory => Path.Combine(DataRootDirectory, AuraToolsIds.ResourceDirectoryName);

    public static string LogsDirectory => Path.Combine(DataRootDirectory, AuraToolsIds.LogsDirectoryName);

    public static string LegacyConfigDirectory => Path.Combine(PackageDirectory, AuraToolsIds.ConfigDirectoryName);

    public static string LegacyResourceDirectory => Path.Combine(PackageDirectory, LegacyResourceDirectoryName);

    public static void Initialize(ModConfig config)
    {
        packageDirectory = FullPathOrEmpty(config.DirectoryName);
        dataRootDirectory = ResolveDataRootDirectory(packageDirectory);
        EnsureBaseDirectories();
    }

    public static void EnsureBaseDirectories()
    {
        CreateDirectorySafe(DataRootDirectory);
        CreateDirectorySafe(ConfigDirectory);
        CreateDirectorySafe(ResourceDirectory);
        CreateDirectorySafe(LogsDirectory);
    }

    public static string ResourcePath(params string[] segments)
    {
        var path = ResourceDirectory;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    public static string ResolveConfiguredPath(string relativeOrAbsolute)
    {
        var candidate = NormalizePathInput(relativeOrAbsolute);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return DataRootDirectory;
        }

        var systemPath = candidate.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(systemPath))
        {
            return Path.GetFullPath(systemPath);
        }

        var dataPath = Path.GetFullPath(Path.Combine(DataRootDirectory, systemPath));
        if (File.Exists(dataPath)
            || Directory.Exists(dataPath)
            || StartsWithSegment(candidate, AuraToolsIds.ResourceDirectoryName)
            || StartsWithSegment(candidate, AuraToolsIds.ConfigDirectoryName)
            || StartsWithSegment(candidate, AuraToolsIds.LogsDirectoryName))
        {
            return dataPath;
        }

        var packagePath = Path.GetFullPath(Path.Combine(PackageDirectory, systemPath));
        return File.Exists(packagePath) || Directory.Exists(packagePath) ? packagePath : dataPath;
    }

    public static string ToDataRelativePath(string absoluteOrRelative)
    {
        var candidate = NormalizePathInput(absoluteOrRelative);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "";
        }

        var systemPath = candidate.Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(systemPath))
        {
            return candidate.Replace(Path.DirectorySeparatorChar, '/');
        }

        var fullPath = Path.GetFullPath(systemPath);
        return IsInsideDirectoryCore(fullPath, DataRootDirectory)
            ? MakeRelative(DataRootDirectory, fullPath).Replace(Path.DirectorySeparatorChar, '/')
            : candidate;
    }

    public static bool IsInsideDataRoot(string path)
    {
        return IsInsideDirectoryCore(path, DataRootDirectory);
    }

    public static bool IsInsidePackageDirectory(string path)
    {
        return IsInsideDirectoryCore(path, PackageDirectory);
    }

    public static bool IsInsideDirectory(string path, string directory)
    {
        return IsInsideDirectoryCore(path, directory);
    }

    public static bool IsLegacyResourcePath(string relativeOrAbsolute)
    {
        var candidate = NormalizePathInput(relativeOrAbsolute);
        return StartsWithSegment(candidate, LegacyResourceDirectoryName);
    }

    public static string ConvertLegacyResourceRelativePath(string legacyPath)
    {
        var candidate = NormalizePathInput(legacyPath);
        if (!StartsWithSegment(candidate, LegacyResourceDirectoryName))
        {
            return candidate;
        }

        var rest = candidate.Substring(LegacyResourceDirectoryName.Length).TrimStart('/', '\\');
        return (AuraToolsIds.ResourceDirectoryName + "/" + rest).Replace('\\', '/');
    }

    public static bool IsSamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            var fullLeft = Path.GetFullPath(left)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRight = Path.GetFullPath(right)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullLeft, fullRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveDataRootDirectory(string packageDir)
    {
        var modsRoot = ResolveModsRoot(packageDir);
        if (!string.IsNullOrWhiteSpace(modsRoot))
        {
            var parent = Directory.GetParent(modsRoot);
            if (parent != null)
            {
                return Path.Combine(parent.FullName, AuraToolsIds.DataRootDirectoryName, AuraToolsIds.ModId);
            }
        }

        var appDataPath = FullPathOrEmpty(Application.dataPath);
        if (!string.IsNullOrWhiteSpace(appDataPath))
        {
            return Path.Combine(appDataPath, AuraToolsIds.DataRootDirectoryName, AuraToolsIds.ModId);
        }

        var persistentPath = FullPathOrEmpty(Application.persistentDataPath);
        return !string.IsNullOrWhiteSpace(persistentPath)
            ? Path.Combine(persistentPath, AuraToolsIds.DataRootDirectoryName, AuraToolsIds.ModId)
            : Path.Combine(Environment.CurrentDirectory, AuraToolsIds.DataRootDirectoryName, AuraToolsIds.ModId);
    }

    private static string ResolveModsRoot(string packageDir)
    {
        var appDataPath = FullPathOrEmpty(Application.dataPath);
        if (!string.IsNullOrWhiteSpace(appDataPath))
        {
            return Path.Combine(appDataPath, "Mods");
        }

        if (!string.IsNullOrWhiteSpace(packageDir))
        {
            var current = new DirectoryInfo(packageDir);
            while (current != null)
            {
                if (string.Equals(current.Name, "Mods", StringComparison.OrdinalIgnoreCase))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return "";
    }

    private static bool IsInsideDirectoryCore(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase)
                   || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string MakeRelative(string root, string path)
    {
        var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
        var pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string value)
    {
        return value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? value
            : value + Path.DirectorySeparatorChar;
    }

    private static bool StartsWithSegment(string value, string segment)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals(segment, StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(segment + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathInput(string value)
    {
        return (value ?? "").Trim().Trim('"').Replace('\\', '/');
    }

    private static string FullPathOrEmpty(string value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? "" : Path.GetFullPath(value);
        }
        catch
        {
            return "";
        }
    }

    private static void CreateDirectorySafe(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                Directory.CreateDirectory(path);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Failed to create directory " + path + ": " + ex.Message);
        }
    }
}
