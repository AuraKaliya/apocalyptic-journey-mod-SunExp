using System;
using System.IO;
using UnityEngine;
using Witch.Mod;

namespace SkinExp.Dll.Infrastructure;

public static class SkinPaths
{
    public static string PackageDirectory { get; private set; } = "";

    public static string ModsRootDirectory { get; private set; } = "";

    public static string SettingsDirectory { get; private set; } = "";

    public static string SettingsPath => Path.Combine(SettingsDirectory, "selections.json");

    public static void Initialize(ModConfig config)
    {
        PackageDirectory = FullPath(config.DirectoryName);
        ModsRootDirectory = ResolveModsRoot(PackageDirectory);
        var persistent = FullPath(Application.persistentDataPath);
        SettingsDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(persistent) ? Environment.CurrentDirectory : persistent,
            "SkinExp");
        Directory.CreateDirectory(SettingsDirectory);
        SkinLog.Info("package=" + PackageDirectory + ", modsRoot=" + ModsRootDirectory);
    }

    public static string ResolveManifestAsset(string manifestPath, string configuredPath, bool directory)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(configuredPath))
        {
            return "";
        }

        try
        {
            var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? "";
            var candidate = configuredPath.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(manifestDirectory, candidate));

            if (!IsInside(fullPath, manifestDirectory))
            {
                SkinLog.Warn("Rejected asset path outside skin package: " + configuredPath);
                return "";
            }

            if (directory)
            {
                return Directory.Exists(fullPath) ? fullPath : "";
            }

            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
            {
                if (File.Exists(fullPath + extension))
                {
                    return fullPath + extension;
                }
            }
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to resolve skin asset " + configuredPath + ": " + ex.Message);
        }

        return "";
    }

    public static string ToRawResourcePath(string absolutePath)
    {
        return string.IsNullOrWhiteSpace(absolutePath)
            ? ""
            : "Raw:" + Path.GetFullPath(absolutePath).Replace('\\', '/');
    }

    private static string ResolveModsRoot(string packageDirectory)
    {
        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            var current = new DirectoryInfo(packageDirectory);
            while (current != null)
            {
                if (string.Equals(current.Name, "Mods", StringComparison.OrdinalIgnoreCase))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        var dataPath = FullPath(Application.dataPath);
        var gameMods = string.IsNullOrWhiteSpace(dataPath) ? "" : Path.Combine(dataPath, "Mods");
        if (!string.IsNullOrWhiteSpace(gameMods) && Directory.Exists(gameMods))
        {
            return gameMods;
        }

        return Directory.GetParent(packageDirectory)?.FullName ?? packageDirectory;
    }

    private static bool IsInside(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string FullPath(string value)
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
}
