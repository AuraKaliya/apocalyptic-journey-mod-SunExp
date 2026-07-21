using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using Witch.Core;
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

    public static AuraEffectiveRoleSnapshot GetEffectiveSnapshot()
    {
        var gameData = AuraGameDataHostApi.AcquireSnapshot();
        var registry = GetSnapshot();
        if (!gameData.Version.NativeReady)
        {
            return new AuraEffectiveRoleSnapshot(
                registry.Revision,
                gameData.Version.Epoch,
                nativeReady: false,
                Array.Empty<AuraRoleRegistryEntry>());
        }

        var runtimeEntries = AuraGameDataHostApi.Query(DataType.Career, includeAllCandidates: true).Items
            .Where(item => item.Enabled
                && !item.Retired
                && string.Equals(item.SourceKind, AuraGameDataSourceKinds.Native, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => AuraSharedIdentity.NormalizeRoleId(item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item => new AuraRoleRegistryEntry
            {
                RoleId = item.Id,
                OwnerModId = item.OwnerModId,
                DisplayName = Field(item.Fields, "Name", item.Id),
                PackBelong = Field(item.Fields, "PackBelong"),
                Icon = Field(item.Fields, "Icon"),
                Priority = item.Priority,
                Aliases = item.Aliases.Concat(new[] { item.Id }).ToList(),
                Tags = new List<string> { "runtime-available", "native-career" },
                Enabled = true
            })
            .ToList();

        var effective = AuraEffectiveRoleCatalog.Merge(runtimeEntries, registry.Entries);
        return new AuraEffectiveRoleSnapshot(
            registry.Revision,
            gameData.Version.Epoch,
            nativeReady: true,
            effective);
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

    private static string Field(IReadOnlyDictionary<string, string> fields, string key, string fallback = "")
    {
        return fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }
}
