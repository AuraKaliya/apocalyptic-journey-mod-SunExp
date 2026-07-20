using System;
using System.IO;
using AuraShared.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsPaths
{
    public const string ConfigSystem = "AuraTools";
    private static string packageDirectory = "";
    private static string dataRootDirectory = "";

    public static string PackageDirectory => packageDirectory;

    public static string DataRootDirectory => string.IsNullOrWhiteSpace(dataRootDirectory)
        ? packageDirectory
        : dataRootDirectory;

    public static string BundledConfigDirectory => Path.Combine(PackageDirectory, AuraToolsIds.ConfigDirectoryName);

    public static string ConfigDirectory => AuraSharedPaths.OwnerSystemConfigDirectory(AuraToolsIds.ModId, ConfigSystem);

    public static string ResourceDirectory => DataRootDirectory;

    public static string AudioDirectory => AuraSharedPaths.AudioDirectory;

    public static string CgDirectory => AuraSharedPaths.CgDirectory;

    public static string SkinDirectory => AuraSharedPaths.SkinDirectory;

    public static string LogsDirectory => AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId);

    public static void Initialize(ModConfig config)
    {
        packageDirectory = FullPathOrEmpty(config.DirectoryName);
        AuraSharedRuntime.Initialize(config, AuraToolsIds.ModId);
        dataRootDirectory = AuraSharedPaths.RootDirectory;
        EnsureBaseDirectories();
    }

    public static void EnsureBaseDirectories()
    {
        AuraSharedPaths.EnsureStandardDirectories();
        CreateDirectorySafe(ConfigDirectory);
        CreateDirectorySafe(AudioDirectory);
        CreateDirectorySafe(CgDirectory);
        CreateDirectorySafe(SkinDirectory);
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
        var raw = (relativeOrAbsolute ?? "").Trim().Trim('"');
        if (Path.IsPathRooted(raw))
        {
            return Path.GetFullPath(raw);
        }

        var candidate = NormalizePathInput(raw);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return DataRootDirectory;
        }

        var shared = AuraSharedResourceProtocol.Resolve(AuraToolsIds.ModId, candidate);
        if (shared.Active || shared.Success)
        {
            return shared.ResolvedPath;
        }

        var systemPath = candidate.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(DataRootDirectory, systemPath));
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
        return IsInsideDataRoot(fullPath)
            ? AuraSharedPaths.MakeRelative(DataRootDirectory, fullPath).Replace(Path.DirectorySeparatorChar, '/')
            : candidate;
    }

    public static bool IsInsideDataRoot(string path)
    {
        return AuraSharedPaths.IsInsideDirectory(path, DataRootDirectory);
    }

    public static bool IsInsidePackageDirectory(string path)
    {
        return AuraSharedPaths.IsInsideDirectory(path, PackageDirectory);
    }

    public static bool IsInsideDirectory(string path, string directory)
    {
        return AuraSharedPaths.IsInsideDirectory(path, directory);
    }

    public static bool IsSamePath(string left, string right)
    {
        return AuraSharedPaths.IsSamePath(left, right);
    }

    private static string NormalizePathInput(string value)
    {
        return AuraSharedPaths.NormalizeRelativePath(value);
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
            Directory.CreateDirectory(path);
        }
        catch
        {
        }
    }
}
