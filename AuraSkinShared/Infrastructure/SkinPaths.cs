using System;
using System.IO;
using AuraShared.Core;
using Witch.Mod;

namespace AuraSkin.Shared.Infrastructure;

public static class SkinPaths
{
    public static string SettingsDirectory { get; private set; } = "";

    public static string SettingsPath => Path.Combine(SettingsDirectory, "selections.json");

    public static string SkinRootDirectory => AuraSharedPaths.SkinDirectory;

    public static string RegistryDirectory => Path.Combine(AuraSharedPaths.RegistriesRootDirectory, "Skin");

    public static void RegisterOwner(ModConfig? config, string ownerModId)
    {
        AuraSharedRuntime.Initialize(config, ownerModId);
        SettingsDirectory = AuraSharedPaths.SharedSystemConfigDirectory(AuraSharedSystems.Skin);
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(SkinRootDirectory);
        Directory.CreateDirectory(RegistryDirectory);

        SkinLog.Info("owner=" + ownerModId
                     + ", sharedSkin=" + SkinRootDirectory
                     + ", registry=" + RegistryDirectory);
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

    private static bool IsInside(string path, string directory)
    {
        return AuraSharedPaths.IsInsideDirectory(path, directory);
    }
}
