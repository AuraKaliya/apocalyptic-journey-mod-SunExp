using System;
using System.IO;
using AuraShared.Core;
using AudioArbiter.Shared;
using Witch.Mod;

namespace AuraAudio.Shared;

public static class AuraAudioRuntime
{
    public const string DefaultRegistryPath = "audio.registry.json";
    public const string DefaultPackageManifestPath = "SharedResources/package.json";

    public static AuraAudioInitializeResult Initialize(
        ModConfig modConfig,
        string ownerModId,
        string registryRelativePath = DefaultRegistryPath,
        string packageManifestRelativePath = DefaultPackageManifestPath)
    {
        var result = new AuraAudioInitializeResult();
        try
        {
            if (modConfig == null)
            {
                result.AddError("Mod config is null.");
                return result;
            }

            if (string.IsNullOrWhiteSpace(ownerModId))
            {
                result.AddError("Owner mod id is empty.");
                return result;
            }

            AuraSharedRuntime.Initialize(modConfig, ownerModId);
            result.SharedRoot = AuraSharedPaths.RootDirectory;
            result.PackageInstalled = TryInstallPackage(modConfig, ownerModId, packageManifestRelativePath, result);

            AudioArbiterRuntime.Initialize(modConfig, ownerModId);
            var registry = string.IsNullOrWhiteSpace(registryRelativePath) ? DefaultRegistryPath : registryRelativePath;
            if (!ManifestExists(modConfig, registry))
            {
                result.RegistryRegistered = false;
            }
            else
            {
                result.RegistryRegistered = AudioArbiterRuntime.RegisterManifest(modConfig, ownerModId, registry);
                if (!result.RegistryRegistered)
                {
                    result.AddError("Audio registry registration failed: " + registry);
                }
            }
        }
        catch (Exception ex)
        {
            result.AddError(ex.Message);
        }

        return result;
    }

    private static bool ManifestExists(ModConfig modConfig, string relativePath)
    {
        var normalized = AuraSharedPaths.NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return File.Exists(Path.Combine(modConfig.DirectoryName, normalized.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool TryInstallPackage(
        ModConfig modConfig,
        string ownerModId,
        string packageManifestRelativePath,
        AuraAudioInitializeResult result)
    {
        var normalized = AuraSharedPaths.NormalizeRelativePath(packageManifestRelativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var manifestPath = Path.Combine(modConfig.DirectoryName, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        var responses = AuraSharedPackageEngine.InstallManifest(modConfig, ownerModId, normalized);
        var success = true;
        foreach (var response in responses)
        {
            if (response.Success)
            {
                continue;
            }

            success = false;
            result.AddError("Shared audio package rejected: " + response.Message);
        }

        return success;
    }
}

public sealed class AuraAudioInitializeResult
{
    private readonly System.Collections.Generic.List<string> errors = new();

    public bool Success => errors.Count == 0;

    public bool PackageInstalled { get; set; }

    public bool RegistryRegistered { get; set; }

    public string SharedRoot { get; set; } = "";

    public System.Collections.Generic.IReadOnlyList<string> Errors => errors;

    public string ErrorMessage => string.Join("; ", errors);

    public void AddError(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            errors.Add(message.Trim());
        }
    }
}
