using System;
using System.Linq;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedResourceBootstrapper
{
    public const string DefaultManifestPath = "SharedResources/package.json";
    public const string V3ManifestPath = AuraSharedResourceProtocol.DefaultManifestPath;

    public static AuraSharedBootstrapResult Bootstrap(
        ModConfig modConfig,
        string ownerModId,
        string manifestRelativePath = DefaultManifestPath)
    {
        if (modConfig == null)
        {
            return AuraSharedBootstrapResult.Failed("Mod config is null.");
        }

        if (string.IsNullOrWhiteSpace(ownerModId))
        {
            return AuraSharedBootstrapResult.Failed("Owner Mod id is empty.");
        }

        var requested = string.IsNullOrWhiteSpace(manifestRelativePath)
            ? DefaultManifestPath
            : manifestRelativePath;
        var v3Path = System.IO.Path.Combine(
            modConfig.DirectoryName,
            V3ManifestPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (string.Equals(requested, DefaultManifestPath, StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(v3Path))
        {
            var registration = AuraSharedResourceProtocol.RegisterManifest(
                modConfig,
                ownerModId.Trim(),
                V3ManifestPath);
            var v3Responses = registration.Items.Select(item => new AuraSharedInstallResponse
            {
                Success = item.Success
                          || string.Equals(item.Status, AuraSharedRegistrationStatuses.Unavailable, StringComparison.OrdinalIgnoreCase),
                Changed = item.Changed,
                Status = item.Status,
                InstalledPath = item.CanonicalPath,
                Message = item.Message
            });
            return AuraSharedBootstrapResult.FromResponses(v3Responses);
        }

        var responses = AuraSharedPackageEngine.InstallManifest(modConfig, ownerModId.Trim(), requested);
        return AuraSharedBootstrapResult.FromResponses(responses);
    }
}
