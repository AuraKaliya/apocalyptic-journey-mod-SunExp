using System;
using System.Collections.Generic;
using System.IO;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedPackageEngine
{
    public static AuraSharedInstallResponse Install(string callerId, AuraSharedInstallRequest request)
    {
        try
        {
            var requestJson = AuraSharedJson.Serialize(request);
            var responseJson = AuraSharedRuntime.InvokeComponent(null, callerId, "InstallResourceJson", requestJson) as string;
            return string.IsNullOrWhiteSpace(responseJson)
                ? new AuraSharedInstallResponse { Success = false, Message = "Shared package engine returned no response." }
                : AuraSharedJson.Deserialize<AuraSharedInstallResponse>(responseJson!)
                  ?? new AuraSharedInstallResponse { Success = false, Message = "Shared package response is invalid." };
        }
        catch (Exception ex)
        {
            return new AuraSharedInstallResponse { Success = false, Message = ex.Message };
        }
    }

    public static AuraSharedInstallResponse[] InstallManifest(
        ModConfig modConfig,
        string ownerModId,
        string manifestRelativePath = "SharedResources/package.json")
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        try
        {
            var modRoot = Path.GetFullPath(modConfig.DirectoryName);
            var manifestPath = Path.GetFullPath(Path.Combine(
                modRoot,
                AuraSharedPaths.NormalizeRelativePath(manifestRelativePath).Replace('/', Path.DirectorySeparatorChar)));
            if (!AuraSharedPaths.IsInsideDirectory(manifestPath, modRoot) || !File.Exists(manifestPath))
            {
                return new[] { new AuraSharedInstallResponse { Success = false, Message = "Package manifest is missing or outside its Mod." } };
            }

            var manifest = AuraSharedJson.Deserialize<AuraSharedPackageManifest>(File.ReadAllText(manifestPath));
            if (manifest == null
                || manifest.SchemaVersion != 1
                || string.IsNullOrWhiteSpace(manifest.PackageId)
                || manifest.PackageVersion < 1
                || manifest.Resources == null
                || manifest.Resources.Count == 0)
            {
                return new[] { new AuraSharedInstallResponse { Success = false, Message = "Package manifest is invalid: " + manifestPath } };
            }

            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? modRoot;
            var responses = new List<AuraSharedInstallResponse>();
            foreach (var resource in manifest.Resources)
            {
                var source = Path.GetFullPath(Path.Combine(
                    manifestDirectory,
                    AuraSharedPaths.NormalizeRelativePath(resource.Source).Replace('/', Path.DirectorySeparatorChar)));
                if (!AuraSharedPaths.IsInsideDirectory(source, manifestDirectory))
                {
                    responses.Add(new AuraSharedInstallResponse { Success = false, Message = "Package source escapes manifest directory: " + resource.Source });
                    continue;
                }

                responses.Add(Install(ownerModId, new AuraSharedInstallRequest
                {
                    OwnerModId = ownerModId,
                    System = resource.System,
                    LogicalId = resource.ResourceId,
                    PackageId = manifest.PackageId,
                    PackageVersion = manifest.PackageVersion,
                    Kind = resource.Kind,
                    SourcePath = source,
                    DestinationRelativePath = resource.Destination
                }));
            }

            return responses.ToArray();
        }
        catch (Exception ex)
        {
            return new[] { new AuraSharedInstallResponse { Success = false, Message = ex.Message } };
        }
    }

    public static IReadOnlyList<AuraSharedInstalledResource> GetResources(string callerId, string system)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(null, callerId, "GetInstalledResourcesJson", system) as string;
            return string.IsNullOrWhiteSpace(json)
                ? Array.Empty<AuraSharedInstalledResource>()
                : AuraSharedJson.Deserialize<AuraSharedInstalledResource[]>(json!) ?? Array.Empty<AuraSharedInstalledResource>();
        }
        catch
        {
            return Array.Empty<AuraSharedInstalledResource>();
        }
    }
}
