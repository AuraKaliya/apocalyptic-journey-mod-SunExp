using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;

namespace AuraCg.Shared;

public static class AuraCgCatalogQueryService
{
    public static HashSet<string> GetActiveResourceKeys(
        string callerId,
        string featureId,
        string scopeType = "",
        string scopeId = "")
    {
        var snapshot = AuraSharedResourceProtocol.QueryCatalog(callerId, new AuraSharedCatalogQueryV3
        {
            ModuleId = AuraSharedSystems.Cg,
            FeatureId = featureId,
            ScopeType = scopeType,
            ScopeId = scopeId
        });
        return snapshot.Entries
            .Where(entry => entry.Active && entry.Available)
            .SelectMany(entry => new[]
            {
                ResourceKey(entry.OwnerModId, entry.Resource.ResourceId),
                PathKey(entry.OwnerModId, entry.CanonicalPath)
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsActive(ISet<string>? activeRegistrationKeys, AuraCgRegistryEntry? entry)
    {
        if (activeRegistrationKeys == null || entry == null)
        {
            return false;
        }

        return activeRegistrationKeys.Contains(ResourceKey(entry.OwnerModId, entry.CgId))
               || activeRegistrationKeys.Contains(PathKey(entry.OwnerModId, entry.Media?.Resource ?? ""))
               || activeRegistrationKeys.Contains(PathKey(entry.OwnerModId, entry.Media?.FallbackImage ?? ""));
    }

    private static string ResourceKey(string ownerModId, string resourceId)
    {
        return (ownerModId ?? "").Trim() + ":" + (resourceId ?? "").Trim();
    }

    private static string PathKey(string ownerModId, string relativePath)
    {
        return (ownerModId ?? "").Trim()
               + "|path:"
               + AuraSharedPaths.NormalizeRelativePath(relativePath ?? "");
    }
}
