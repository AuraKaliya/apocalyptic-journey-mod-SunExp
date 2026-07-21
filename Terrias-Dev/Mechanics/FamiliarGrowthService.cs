using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class FamiliarGrowthService
{
    private static readonly object SyncRoot = new();
    private static IFamiliarProfileStore? store;
    private static string currentPartnerId = "";
    private static FamiliarInstance? runSnapshot;

    public static void Configure(IFamiliarProfileStore profileStore)
    {
        lock (SyncRoot)
        {
            store = profileStore;
            runSnapshot = null;
            var document = LoadAndNormalize(out var changed);
            if (changed)
            {
                Save(document);
            }
        }
    }

    public static FamiliarRosterDocument Snapshot()
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out var changed);
            if (changed)
            {
                Save(document);
            }

            return Clone(document);
        }
    }

    public static IReadOnlyList<FamiliarSpeciesSpec> Species()
    {
        return FamiliarSpeciesCatalog.AllSpecies();
    }

    public static string CurrentPartnerId()
    {
        lock (SyncRoot)
        {
            return currentPartnerId;
        }
    }

    public static FamiliarInstance? RefreshCurrentPartner(string partnerId)
    {
        lock (SyncRoot)
        {
            var spec = FamiliarSpeciesCatalog.Find(partnerId);
            currentPartnerId = spec?.FullSpeciesId ?? "";
            return BodyCore(currentPartnerId);
        }
    }

    public static FamiliarInstance? BeginRun(string partnerId)
    {
        lock (SyncRoot)
        {
            var spec = FamiliarSpeciesCatalog.Find(partnerId);
            currentPartnerId = spec?.FullSpeciesId ?? "";
            runSnapshot = BodyCore(currentPartnerId);
            return runSnapshot?.Clone();
        }
    }

    public static FamiliarInstance? Active()
    {
        lock (SyncRoot)
        {
            return runSnapshot?.Clone() ?? BodyCore(currentPartnerId);
        }
    }

    public static FamiliarInstance? Body(string partnerId)
    {
        lock (SyncRoot)
        {
            var spec = FamiliarSpeciesCatalog.Find(partnerId);
            return BodyCore(spec?.FullSpeciesId ?? partnerId);
        }
    }

    public static bool Rename(string partnerId, string name)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            if (!FamiliarRosterService.Rename(document, partnerId, name))
            {
                return false;
            }

            Save(document);
            return true;
        }
    }

    public static FamiliarExperienceResult? GrantExperience(string partnerId, int amount)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            var instance = FamiliarRosterService.Find(document, partnerId);
            if (instance == null)
            {
                return null;
            }

            var result = FamiliarRosterService.GrantExperience(instance, amount);
            Save(document);
            return result;
        }
    }

    public static FamiliarExperienceResult? GrantActiveExperience(int amount)
    {
        lock (SyncRoot)
        {
            var id = runSnapshot?.FullSpeciesId ?? currentPartnerId;
            return id.Length == 0 ? null : GrantExperience(id, amount);
        }
    }

    public static bool ChooseBlessing(string partnerId, string choiceId, string blessingId)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            if (!FamiliarRosterService.ChooseBlessing(document, partnerId, choiceId, blessingId))
            {
                return false;
            }

            Save(document);
            return true;
        }
    }

    public static bool CanRebirth(string partnerId)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out var changed);
            if (changed)
            {
                Save(document);
            }

            return FamiliarRosterService.CanRebirth(FamiliarRosterService.Find(document, partnerId));
        }
    }

    public static FamiliarRebirthResult? Rebirth(string partnerId)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            var result = FamiliarRosterService.Rebirth(document, partnerId);
            if (result == null)
            {
                return null;
            }

            Save(document);
            return result;
        }
    }

    public static bool ActiveHasBlessing(string blessingId)
    {
        var active = Active();
        return active != null && active.AllBlessingIds().Contains(blessingId ?? "", StringComparer.Ordinal);
    }

    public static bool ActiveHasTag(string tag)
    {
        var active = Active();
        return active != null && FamiliarBlessingRegistry.HasTag(active, tag);
    }

    public static bool ActiveHasEffect(string effectKind)
    {
        var active = Active();
        return active != null && FamiliarBlessingRegistry.HasEffect(active, effectKind);
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> BlessingsFor(FamiliarInstance instance)
    {
        if (instance == null)
        {
            return Array.Empty<FamiliarBlessingDefinition>();
        }

        var ids = new HashSet<string>(instance.AllBlessingIds(), StringComparer.Ordinal);
        return FamiliarBlessingRegistry.All()
            .Where(blessing => ids.Contains(blessing.Id))
            .OrderBy(blessing => blessing.RequiredLevel)
            .ThenBy(blessing => blessing.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static FamiliarInstance? BodyCore(string partnerId)
    {
        if (string.IsNullOrWhiteSpace(partnerId))
        {
            return null;
        }

        var document = LoadAndNormalize(out var changed);
        if (changed)
        {
            Save(document);
        }

        return FamiliarRosterService.Find(document, partnerId)?.Clone();
    }

    private static FamiliarRosterDocument LoadAndNormalize(out bool changed)
    {
        var document = store?.Load() ?? new FamiliarRosterDocument();
        changed = FamiliarRosterService.Normalize(document, FamiliarSpeciesCatalog.AllSpecies());
        return document;
    }

    private static void Save(FamiliarRosterDocument document)
    {
        store?.Save(document);
    }

    private static FamiliarRosterDocument Clone(FamiliarRosterDocument source)
    {
        return new FamiliarRosterDocument
        {
            Version = source.Version,
            Instances = source.Instances.Select(instance => instance.Clone()).ToList()
        };
    }
}
