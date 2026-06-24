using System;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedResourceBootstrapper
{
    public const string DefaultManifestPath = "SharedResources/package.json";

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

        var responses = AuraSharedPackageEngine.InstallManifest(
            modConfig,
            ownerModId.Trim(),
            string.IsNullOrWhiteSpace(manifestRelativePath)
                ? DefaultManifestPath
                : manifestRelativePath);
        return AuraSharedBootstrapResult.FromResponses(responses);
    }
}
