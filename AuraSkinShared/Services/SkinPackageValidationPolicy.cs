using System;
using System.IO;
using AuraSkin.Shared.Models;

namespace AuraSkin.Shared.Services;

internal static class SkinPackageValidationPolicy
{
    public static bool TryValidateManifest(SkinPackageManifest? package, out string error)
    {
        if (package == null)
        {
            error = "Skin package manifest is missing.";
            return false;
        }

        if (package.SchemaVersion != 1)
        {
            error = "Skin package schemaVersion must be 1.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(package.PackageId))
        {
            error = "Skin package packageId is required.";
            return false;
        }

        if (package.PackageVersion < 1)
        {
            error = "Skin package packageVersion must be at least 1.";
            return false;
        }

        if (package.Resources == null || package.Resources.Count == 0)
        {
            error = "Skin package must declare at least one resource.";
            return false;
        }

        error = "";
        return true;
    }

    public static bool TryResolveSourceDirectory(
        string packageDirectory,
        string? source,
        out string relativeSource,
        out string sourceDirectory,
        out string error)
    {
        relativeSource = "";
        sourceDirectory = "";

        var rawSource = (source ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(packageDirectory)
            || string.IsNullOrWhiteSpace(rawSource)
            || Path.IsPathRooted(rawSource))
        {
            error = "Skin package resource source must be relative.";
            return false;
        }

        try
        {
            relativeSource = rawSource.Replace('\\', '/').TrimStart('/');
            var fullPackageDirectory = Path.GetFullPath(packageDirectory);
            sourceDirectory = Path.GetFullPath(Path.Combine(
                fullPackageDirectory,
                relativeSource.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideDirectory(sourceDirectory, fullPackageDirectory))
            {
                error = "Skin package source escapes its package: " + relativeSource;
                return false;
            }

            if (!Directory.Exists(sourceDirectory))
            {
                error = "Skin package source is missing: " + relativeSource;
                return false;
            }

            error = "";
            return true;
        }
        catch
        {
            relativeSource = "";
            sourceDirectory = "";
            error = "Skin package resource source is invalid.";
            return false;
        }
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
