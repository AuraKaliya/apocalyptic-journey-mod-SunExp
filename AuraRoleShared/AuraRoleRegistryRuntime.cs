using System;
using System.Collections.Generic;
using System.IO;
using AuraShared.Core;
using Witch.Mod;

namespace AuraRole.Shared;

public static class AuraRoleRegistryRuntime
{
    public const string RegistryAuthorityId = "AuraRoleShared";
    public const string RegistryFileName = "role.registry.json";
    public const int CurrentSchemaVersion = 1;
    public static readonly string CurrentSessionId = Guid.NewGuid().ToString("N");
    private static readonly object CacheGate = new();
    private static AuraRoleRegistryDocument? cachedDocument;
    private static long cachedRevision = -1;

    public static event Action<long>? Changed;

    public static AuraRoleRegistrySnapshot GetSnapshot()
    {
        var document = ReadDocument();
        return new AuraRoleRegistrySnapshot(Math.Max(0, cachedRevision), document.BuildActiveEntries(CurrentSessionId));
    }

    public static bool RegisterManifest(
        ModConfig? modConfig,
        string ownerModId,
        string manifestRelativePath = "SharedResources/role.registry.json")
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        var root = modConfig?.DirectoryName ?? AuraSharedPaths.PackageDirectory;
        var path = string.IsNullOrWhiteSpace(manifestRelativePath)
            ? ""
            : Path.Combine(root, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AuraSharedLog.DebugLog("AuraRoleShared", "Role manifest missing for " + ownerModId + ": " + path, false);
            return false;
        }

        try
        {
            var manifest = AuraSharedJson.Deserialize<AuraRoleManifest>(File.ReadAllText(path));
            if (manifest == null)
            {
                return false;
            }

            manifest.Normalize(ownerModId);
            return PublishContribution(new AuraRoleRegistryContribution
            {
                ContributorModId = manifest.OwnerModId,
                ContributionId = manifest.ContributionId,
                SessionId = CurrentSessionId,
                Persistent = true,
                Entries = manifest.Entries
            });
        }
        catch (Exception ex)
        {
            AuraSharedLog.Warn("AuraRoleShared", "Role manifest load failed. owner=" + ownerModId + ", failure=" + ex.Message);
            return false;
        }
    }

    public static bool PublishRuntimeRoles(
        string contributorModId,
        string contributionId,
        IEnumerable<AuraRoleRegistryEntry> entries)
    {
        return PublishContribution(new AuraRoleRegistryContribution
        {
            ContributorModId = contributorModId,
            ContributionId = contributionId,
            SessionId = CurrentSessionId,
            Persistent = false,
            Entries = new List<AuraRoleRegistryEntry>(entries ?? Array.Empty<AuraRoleRegistryEntry>())
        });
    }

    public static bool PublishContribution(AuraRoleRegistryContribution contribution)
    {
        if (contribution == null || string.IsNullOrWhiteSpace(contribution.ContributorModId))
        {
            return false;
        }

        contribution.Normalize();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = AuraSharedConfigStore.ReadShared(
                RegistryAuthorityId,
                AuraSharedSystems.Role,
                RegistryFileName,
                new AuraRoleRegistryDocument());
            var document = snapshot.Value ?? new AuraRoleRegistryDocument();
            document.Normalize();
            if (!document.ReplaceContribution(contribution))
            {
                Cache(document, snapshot.Found ? snapshot.Revision : 0);
                return true;
            }

            var result = AuraSharedConfigStore.WriteShared(
                RegistryAuthorityId,
                AuraSharedSystems.Role,
                RegistryFileName,
                document,
                snapshot.Found ? snapshot.Revision : 0,
                CurrentSchemaVersion);
            if (result.Success)
            {
                Cache(document, result.Revision);
                NotifyChanged(result.Revision);
                AuraSharedLog.Info("AuraRoleShared", "Role contribution registered. contributor="
                    + contribution.ContributorModId
                    + ", contribution=" + contribution.ContributionId
                    + ", persistent=" + contribution.Persistent
                    + ", entries=" + contribution.Entries.Count
                    + ", revision=" + result.Revision);
                return true;
            }

            if (!result.Conflict)
            {
                AuraSharedLog.Warn("AuraRoleShared", "Role contribution write failed: " + result.Message);
                return false;
            }
        }

        AuraSharedLog.Warn("AuraRoleShared", "Role contribution conflicted repeatedly. contributor="
            + contribution.ContributorModId + ", contribution=" + contribution.ContributionId);
        return false;
    }

    public static void InvalidateCache()
    {
        lock (CacheGate)
        {
            cachedDocument = null;
            cachedRevision = -1;
        }
    }

    private static AuraRoleRegistryDocument ReadDocument()
    {
        lock (CacheGate)
        {
            if (cachedDocument != null)
            {
                return cachedDocument;
            }
        }

        var snapshot = AuraSharedConfigStore.ReadShared(
            RegistryAuthorityId,
            AuraSharedSystems.Role,
            RegistryFileName,
            new AuraRoleRegistryDocument());
        var document = snapshot.Value ?? new AuraRoleRegistryDocument();
        document.Normalize();
        Cache(document, snapshot.Found ? snapshot.Revision : 0);
        return document;
    }

    private static void Cache(AuraRoleRegistryDocument document, long revision)
    {
        lock (CacheGate)
        {
            cachedDocument = document;
            cachedRevision = Math.Max(0, revision);
        }
    }

    private static void NotifyChanged(long revision)
    {
        try
        {
            Changed?.Invoke(Math.Max(0, revision));
        }
        catch
        {
        }
    }
}
