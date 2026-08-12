using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class SpiritTrainingRegistry
{
    private static readonly object SyncRoot = new();
    private static SpiritTrainingRegistryDocument document = new();
    private static Dictionary<string, CompanionIntentDefinition> commonIntents = new(StringComparer.Ordinal);
    private static Dictionary<string, SpiritPassiveDefinition> passives = new(StringComparer.Ordinal);
    private static Dictionary<string, SpiritSpeciesTrainingProfile> profiles = new(StringComparer.Ordinal);
    private static string registryHash = "00000000";

    public static string RegistryHash
    {
        get { lock (SyncRoot) return registryHash; }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.SpiritTrainingRegistryFile);
            if (!File.Exists(path))
            {
                SetDocument(new SpiritTrainingRegistryDocument());
                TerriasLog.Warn("[SpiritTrainingRegistry] missing registry; training uses safe empty pools.");
                return;
            }

            try
            {
                var loaded = AuraSharedJson.Deserialize<SpiritTrainingRegistryDocument>(File.ReadAllText(path))
                             ?? new SpiritTrainingRegistryDocument();
                if (loaded.SchemaVersion != 1)
                {
                    throw new InvalidDataException("unsupported schemaVersion=" + loaded.SchemaVersion + "; expected 1");
                }

                SetDocument(Normalize(loaded));
                TerriasLog.Info("[SpiritTrainingRegistry] registryState=ready commonIntents="
                                + commonIntents.Count + ", passives=" + passives.Count
                                + ", profiles=" + profiles.Count + ", hash=" + registryHash + ".");
            }
            catch (Exception ex)
            {
                SetDocument(new SpiritTrainingRegistryDocument());
                TerriasLog.Warn("[SpiritTrainingRegistry] failed to load registry: " + ex.Message);
            }
        }
    }

    public static IReadOnlyList<CompanionIntentDefinition> CommonIntents()
    {
        lock (SyncRoot) return commonIntents.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> CommonIntentIds(string pool)
    {
        lock (SyncRoot)
        {
            return commonIntents.Values
                .Where(value => string.Equals(value.Pool, pool ?? "", StringComparison.Ordinal))
                .Select(value => value.Id)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static IReadOnlyList<string> CommonPassiveIds(string pool)
    {
        lock (SyncRoot)
        {
            return passives.Values
                .Where(value => string.Equals(value.Pool, pool ?? "", StringComparison.Ordinal))
                .Select(value => value.Id)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static SpiritPassiveDefinition? FindPassive(string id)
    {
        lock (SyncRoot) return passives.TryGetValue(id ?? "", out var value) ? value : null;
    }

    public static SpiritSpeciesTrainingProfile ProfileFor(string speciesId, string profileId)
    {
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(profileId) && profiles.TryGetValue("profile:" + profileId, out var exact))
            {
                return exact;
            }
            if (!string.IsNullOrWhiteSpace(speciesId) && profiles.TryGetValue("species:" + speciesId, out var species))
            {
                return species;
            }
            return new SpiritSpeciesTrainingProfile
            {
                SpeciesId = speciesId ?? "",
                ProfileId = profileId ?? "",
                InitialPassiveId = SpeciesPassiveId(speciesId ?? "")
            };
        }
    }

    public static string SpeciesPassiveId(string speciesId)
    {
        var value = new string((speciesId ?? "unknown").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return "spirit.passive.species." + (value.Length == 0 ? "unknown" : value) + ".inherent";
    }

    public static string IntentDisplayName(string intentId)
    {
        var intent = SpiritIntentRegistry.Find(intentId);
        if (intent == null) return intentId ?? "";
        if (!string.IsNullOrWhiteSpace(intent.DisplayName)) return intent.DisplayName;
        var card = (intent.EnemyCardId ?? "").Replace("Terrias_terrias_", "").Replace("enemycard_", "");
        return card.Length == 0 ? intent.Id : card;
    }

    public static string IntentDescription(string intentId)
    {
        return SpiritIntentRegistry.Find(intentId)?.Description ?? "";
    }

    public static string AbilityDisplayName(string abilityId)
    {
        return FindPassive(abilityId)?.DisplayName ?? IntentDisplayName(abilityId);
    }

    private static SpiritTrainingRegistryDocument Normalize(SpiritTrainingRegistryDocument source)
    {
        source.CommonIntents ??= new List<CompanionIntentDefinition>();
        source.Passives ??= new List<SpiritPassiveDefinition>();
        source.SpeciesProfiles ??= new List<SpiritSpeciesTrainingProfile>();
        source.CommonIntents = source.CommonIntents
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Id))
            .GroupBy(value => value.Id.Trim(), StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        foreach (var intent in source.CommonIntents)
        {
            intent.Id = intent.Id.Trim();
            intent.Pool = (intent.Pool ?? "Common.Basic").Trim();
            intent.DisplayName = (intent.DisplayName ?? "").Trim();
            intent.Description = (intent.Description ?? "").Trim();
        }

        source.Passives = source.Passives
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Id))
            .GroupBy(value => value.Id.Trim(), StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        foreach (var passive in source.Passives)
        {
            passive.Id = passive.Id.Trim();
            passive.Pool = (passive.Pool ?? "Species").Trim();
            passive.NumericBonusPercent = Math.Max(0, Math.Min(75, passive.NumericBonusPercent));
        }

        source.SpeciesProfiles = source.SpeciesProfiles
            .Where(value => value != null && (!string.IsNullOrWhiteSpace(value.ProfileId) || !string.IsNullOrWhiteSpace(value.SpeciesId)))
            .ToList();
        return source;
    }

    private static void SetDocument(SpiritTrainingRegistryDocument source)
    {
        document = source;
        commonIntents = source.CommonIntents.ToDictionary(value => value.Id, StringComparer.Ordinal);
        passives = source.Passives.ToDictionary(value => value.Id, StringComparer.Ordinal);
        profiles = new Dictionary<string, SpiritSpeciesTrainingProfile>(StringComparer.Ordinal);
        foreach (var profile in source.SpeciesProfiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.SpeciesId)) profiles["species:" + profile.SpeciesId] = profile;
            if (!string.IsNullOrWhiteSpace(profile.ProfileId)) profiles["profile:" + profile.ProfileId] = profile;
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in AuraSharedJson.Serialize(source))
            {
                hash = (hash ^ character) * 16777619;
            }
            registryHash = hash.ToString("x8");
        }
    }
}
