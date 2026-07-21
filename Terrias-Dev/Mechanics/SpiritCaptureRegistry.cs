using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class SpiritCaptureRegistry
{
    private static readonly object SyncRoot = new();
    private static SpiritCaptureRegistryDocument document = BuiltInDocument();

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.SpiritCaptureRegistryFile);
            if (!File.Exists(path))
            {
                document = BuiltInDocument();
                TerriasLog.Warn("[SpiritCaptureRegistry] missing registry; using guarded terminal settlement.");
                return;
            }

            try
            {
                var loaded = AuraSharedJson.Deserialize<SpiritCaptureRegistryDocument>(File.ReadAllText(path))
                    ?? new SpiritCaptureRegistryDocument();
                if (loaded.SchemaVersion != 1)
                {
                    throw new InvalidDataException("unsupported schemaVersion=" + loaded.SchemaVersion + "; expected 1");
                }

                document = Normalize(loaded);
                TerriasLog.Info("[SpiritCaptureRegistry] loaded profiles=" + document.Profiles.Count + " from " + path);
            }
            catch (Exception ex)
            {
                document = BuiltInDocument();
                TerriasLog.Warn("[SpiritCaptureRegistry] failed to load registry; using guarded terminal settlement: " + ex.Message);
            }
        }
    }

    public static SpiritCaptureProfile ProfileFor(string enemyId, string variantId)
    {
        return ResolveProfile(enemyId, variantId).Profile;
    }

    public static SpiritProfileResolution<SpiritCaptureProfile> ResolveProfile(string enemyId, string variantId)
    {
        lock (SyncRoot)
        {
            return SpiritProfileIdentityResolver.Resolve(
                document.Profiles,
                profile => profile.EnemyId,
                profile => profile.VariantId,
                enemyId,
                variantId);
        }
    }

    private static SpiritCaptureRegistryDocument Normalize(SpiritCaptureRegistryDocument loaded)
    {
        var profiles = new List<SpiritCaptureProfile>();
        foreach (var profile in loaded.Profiles ?? new List<SpiritCaptureProfile>())
        {
            var enemyId = string.IsNullOrWhiteSpace(profile.EnemyId) ? "*" : profile.EnemyId.Trim();
            var variantId = string.IsNullOrWhiteSpace(profile.VariantId) ? "*" : profile.VariantId.Trim();
            profiles.RemoveAll(existing => Same(existing.EnemyId, enemyId) && Same(existing.VariantId, variantId));
            profiles.Add(new SpiritCaptureProfile
            {
                EnemyId = enemyId,
                VariantId = variantId,
                ResolutionMode = NormalizeMode(profile.ResolutionMode),
                SuppressedSuccessorIds = (profile.SuppressedSuccessorIds ?? new List<string>())
                    .Select(id => (id ?? "").Trim().Replace("*", ""))
                    .Where(id => id.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                RunNativeDeath = profile.RunNativeDeath,
                AllowRewards = profile.AllowRewards
            });
        }

        if (!profiles.Any(profile => profile.EnemyId == "*" && profile.VariantId == "*"))
        {
            profiles.Add(DefaultProfile());
        }

        return new SpiritCaptureRegistryDocument { SchemaVersion = 1, Profiles = profiles };
    }

    private static string NormalizeMode(string value)
    {
        return value switch
        {
            "NativeTerminal" => "NativeTerminal",
            "AdaptedTerminal" => "AdaptedTerminal",
            _ => "GuardedTerminal"
        };
    }

    private static SpiritCaptureRegistryDocument BuiltInDocument()
    {
        return new SpiritCaptureRegistryDocument { SchemaVersion = 1, Profiles = new List<SpiritCaptureProfile> { DefaultProfile() } };
    }

    private static SpiritCaptureProfile DefaultProfile()
    {
        return new SpiritCaptureProfile { EnemyId = "*", VariantId = "*", ResolutionMode = "GuardedTerminal", RunNativeDeath = true, AllowRewards = true };
    }

    private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
}
