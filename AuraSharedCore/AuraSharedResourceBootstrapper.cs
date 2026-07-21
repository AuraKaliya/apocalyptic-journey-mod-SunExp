using System;
using System.Linq;
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
        var responses = registration.Items.Select(item => new AuraSharedInstallResponse
        {
            Success = item.Success,
            Changed = item.Changed,
            Status = item.Status,
            InstalledPath = item.CanonicalPath,
            Message = item.Message
        }).ToList();
        if (!registration.Success)
        {
            responses.Add(new AuraSharedInstallResponse
            {
                Success = false,
                Status = registration.Status,
                Message = "Shared registration failed: failureCode=" + registration.FailureCode
                          + ", expected=" + registration.ExpectedItemCount
                          + ", processed=" + registration.ProcessedItemCount
                          + ", failedPathLength=" + registration.FailedPathLength
                          + ", failedPath=" + registration.FailedPath
                          + ", message=" + registration.Message
            });
        }
        return AuraSharedBootstrapResult.FromResponses(responses);
    }
}
