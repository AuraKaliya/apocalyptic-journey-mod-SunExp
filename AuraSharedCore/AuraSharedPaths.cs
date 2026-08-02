using System;
using System.IO;
using UnityEngine;
using Witch.Mod;

namespace AuraShared.Core;

public sealed class AuraSharedOptions
{
    public string DataRootDirectoryName { get; set; } = "ModsData";

    public string SharedDirectoryName { get; set; } = "AuraShared";

    public bool CreateStandardDirectories { get; set; } = true;
}

public static class AuraSharedPaths
{
    public const string DefaultDataRootDirectoryName = "ModsData";
    public const string DefaultSharedDirectoryName = "AuraShared";

    private static string packageDirectory = "";
    private static string modsDirectory = "";
    private static string modsDataDirectory = "";
    private static string rootDirectory = "";

    public static string PackageDirectory => packageDirectory;

    public static string ModsDirectory => modsDirectory;

    public static string ModsDataDirectory => modsDataDirectory;

    public static string RootDirectory => rootDirectory;

    public static string ConfigRootDirectory => Path.Combine(RootDirectory, "Config");

    public static string SharedConfigDirectory => Path.Combine(ConfigRootDirectory, "Shared");

    public static string OwnerConfigRootDirectory => Path.Combine(ConfigRootDirectory, "Owners");

    public static string RuntimeConfigDirectory => Path.Combine(ConfigRootDirectory, "Runtime");

    public static string DataRootDirectory => Path.Combine(RootDirectory, "Data");

    public static string OwnerDataRootDirectory => Path.Combine(DataRootDirectory, "Owners");

    public static string AudioDirectory => Path.Combine(RootDirectory, "Audio");

    public static string CgDirectory => Path.Combine(RootDirectory, "CG");

    public static string SkinDirectory => Path.Combine(RootDirectory, AuraSharedSystems.Skin);

    public static string LogsRootDirectory => Path.Combine(RootDirectory, "Logs");

    public static string OperationsLogDirectory => Path.Combine(LogsRootDirectory, "Operations");

    public static string RegistriesRootDirectory => Path.Combine(RootDirectory, "Registries");

    public static string BackupsDirectory => Path.Combine(RootDirectory, "Backups");

    public static string CacheDirectory => Path.Combine(RootDirectory, "Cache");

    public static string TransactionsDirectory => Path.Combine(RootDirectory, "Transactions");

    public static string Initialize(ModConfig? modConfig, AuraSharedOptions? options = null)
    {
        options ??= new AuraSharedOptions();
        packageDirectory = FullPathOrEmpty(modConfig?.DirectoryName ?? packageDirectory);
        modsDirectory = ResolveModsDirectory(packageDirectory);
        modsDataDirectory = ResolveModsDataDirectory(packageDirectory, options);
        rootDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(modsDataDirectory) ? Environment.CurrentDirectory : modsDataDirectory,
            SafeSegment(options.SharedDirectoryName, DefaultSharedDirectoryName));

        if (options.CreateStandardDirectories)
        {
            EnsureStandardDirectories();
        }

        return RootDirectory;
    }

    public static void EnsureStandardDirectories()
    {
        CreateDirectorySafe(RootDirectory);
        CreateDirectorySafe(ConfigRootDirectory);
        CreateDirectorySafe(SharedConfigDirectory);
        CreateDirectorySafe(OwnerConfigRootDirectory);
        CreateDirectorySafe(RuntimeConfigDirectory);
        CreateDirectorySafe(DataRootDirectory);
        CreateDirectorySafe(OwnerDataRootDirectory);
        CreateDirectorySafe(AudioDirectory);
        CreateDirectorySafe(CgDirectory);
        CreateDirectorySafe(SkinDirectory);
        CreateDirectorySafe(LogsRootDirectory);
        CreateDirectorySafe(OperationsLogDirectory);
        CreateDirectorySafe(RegistriesRootDirectory);
        CreateDirectorySafe(BackupsDirectory);
        CreateDirectorySafe(CacheDirectory);
        CreateDirectorySafe(TransactionsDirectory);
    }

    public static string OwnerConfigDirectory(string ownerModId)
    {
        return Path.Combine(OwnerConfigRootDirectory, SafeSegment(ownerModId, "UnknownOwner"));
    }

    public static string SharedSystemConfigDirectory(string system)
    {
        return Path.Combine(SharedConfigDirectory, SafeSegment(system, "General"));
    }

    public static string OwnerSystemConfigDirectory(string ownerModId, string system)
    {
        return Path.Combine(OwnerConfigDirectory(ownerModId), SafeSegment(system, "General"));
    }

    public static string RuntimeSystemConfigDirectory(string system)
    {
        return Path.Combine(RuntimeConfigDirectory, SafeSegment(system, "General"));
    }

    public static string OwnerDataDirectory(string ownerModId)
    {
        return Path.Combine(OwnerDataRootDirectory, SafeSegment(ownerModId, "UnknownOwner"));
    }

    public static string OwnerSystemDataDirectory(string ownerModId, string system)
    {
        return Path.Combine(OwnerDataDirectory(ownerModId), SafeSegment(system, "General"));
    }

    public static string OwnerLogsDirectory(string ownerModId)
    {
        return Path.Combine(LogsRootDirectory, SafeSegment(ownerModId, "UnknownOwner"));
    }

    public static string OwnerSystemLogsDirectory(string ownerModId, string system)
    {
        return Path.Combine(OwnerLogsDirectory(ownerModId), SafeSegment(system, "General"));
    }

    public static string OwnerBackupsDirectory(string ownerModId)
    {
        return Path.Combine(BackupsDirectory, SafeSegment(ownerModId, "UnknownOwner"));
    }

    public static string RegistryDirectory(string system)
    {
        return Path.Combine(RegistriesRootDirectory, SafeSegment(system, "General"));
    }

    public static string TransactionPath(string transactionId)
    {
        return Path.Combine(TransactionsDirectory, SafeSegment(transactionId, "unknown") + ".json");
    }

    public static string AudioPath(params string[] segments)
    {
        return Combine(AudioDirectory, segments);
    }

    public static string CgPath(params string[] segments)
    {
        return Combine(CgDirectory, segments);
    }

    public static string ResolveSharedPath(string relativeOrAbsolute)
    {
        var candidate = NormalizeRelativePath(relativeOrAbsolute);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return RootDirectory;
        }

        var systemPath = candidate.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(systemPath)
            ? Path.GetFullPath(systemPath)
            : Path.GetFullPath(Path.Combine(RootDirectory, systemPath));
    }

    public static string ToSharedRelativePath(string absoluteOrRelative)
    {
        var candidate = NormalizeRelativePath(absoluteOrRelative);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "";
        }

        var systemPath = candidate.Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(systemPath))
        {
            return candidate;
        }

        var fullPath = Path.GetFullPath(systemPath);
        return IsInsideDirectory(fullPath, RootDirectory)
            ? MakeRelative(RootDirectory, fullPath).Replace(Path.DirectorySeparatorChar, '/')
            : candidate;
    }

    public static bool IsInsideDirectory(string path, string directory)
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

    public static bool StartsWithSegment(string value, string segment)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var normalizedValue = NormalizeRelativePath(value);
        var normalizedSegment = NormalizeRelativePath(segment).TrimEnd('/');
        return normalizedValue.Equals(normalizedSegment, StringComparison.OrdinalIgnoreCase)
               || normalizedValue.StartsWith(normalizedSegment + "/", StringComparison.OrdinalIgnoreCase);
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

    public static string MakeRelative(string root, string path)
    {
        var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
        var pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string ResolveModsDataDirectory(string packageDir, AuraSharedOptions options)
    {
        var dataRootName = SafeSegment(options.DataRootDirectoryName, DefaultDataRootDirectoryName);
        if (!string.IsNullOrWhiteSpace(modsDirectory))
        {
            var parent = Directory.GetParent(modsDirectory);
            if (parent != null)
            {
                return Path.Combine(parent.FullName, dataRootName);
            }
        }

        var appDataPath = FullPathOrEmpty(Application.dataPath);
        if (!string.IsNullOrWhiteSpace(appDataPath))
        {
            return Path.Combine(appDataPath, dataRootName);
        }

        var persistentPath = FullPathOrEmpty(Application.persistentDataPath);
        if (!string.IsNullOrWhiteSpace(persistentPath))
        {
            return Path.Combine(persistentPath, dataRootName);
        }

        return Path.Combine(
            string.IsNullOrWhiteSpace(packageDir) ? Environment.CurrentDirectory : packageDir,
            dataRootName);
    }

    private static string ResolveModsDirectory(string packageDir)
    {
        var appDataPath = FullPathOrEmpty(Application.dataPath);
        if (!string.IsNullOrWhiteSpace(appDataPath))
        {
            var gameMods = Path.Combine(appDataPath, "Mods");
            if (Directory.Exists(gameMods))
            {
                return gameMods;
            }
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

    private static string Combine(string root, string[] segments)
    {
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static string AppendDirectorySeparator(string value)
    {
        return value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
               || value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? value
            : value + Path.DirectorySeparatorChar;
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
            Debug.LogWarning("[AuraShared] Failed to create directory " + path + ": " + ex.Message);
        }
    }
}
