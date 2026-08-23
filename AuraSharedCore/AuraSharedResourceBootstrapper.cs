using System;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedResourceBootstrapper
{
    public const string DefaultManifestPath = AuraSharedResourceProtocol.DefaultManifestPath;

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
        var registration = AuraSharedResourceProtocol.RegisterManifest(modConfig, ownerModId.Trim(), requested);
        return AuraSharedBootstrapResult.FromRegistration(registration);
    }
}
