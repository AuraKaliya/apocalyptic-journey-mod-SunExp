using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class FamiliarGrowthService
{
    private static readonly object SyncRoot = new();
    private static IFamiliarProfileStore? store;

    public static void Configure(IFamiliarProfileStore profileStore)
    {
        lock (SyncRoot)
        {
            store = profileStore;
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
            return Clone(LoadAndNormalize(out var changed), saveIfChanged: changed);
        }
    }

    public static IReadOnlyList<FamiliarSpeciesSpec> Species()
    {
        return FamiliarSpeciesCatalog.AllSpecies();
    }

    public static FamiliarInstance? Selected()
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out var changed);
            if (changed)
            {
                Save(document);
            }

            return FamiliarRosterService.Selected(document)?.Clone();
        }
    }

    public static FamiliarInstance? Create(string speciesId)
    {
        lock (SyncRoot)
        {
            var species = FamiliarSpeciesCatalog.Find(speciesId);
            if (species == null)
            {
                return null;
            }

            var document = LoadAndNormalize(out _);
            var instance = FamiliarRosterService.Create(document, species);
            Save(document);
            return instance.Clone();
        }
    }

    public static bool Delete(string instanceId)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            if (!FamiliarRosterService.Delete(document, instanceId))
            {
                return false;
            }

            Save(document);
            return true;
        }
    }

    public static bool Rename(string instanceId, string name)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            if (!FamiliarRosterService.Rename(document, instanceId, name))
            {
                return false;
            }

            Save(document);
            return true;
        }
    }

    public static bool Select(string instanceId)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            if (!FamiliarRosterService.Select(document, instanceId))
            {
                return false;
            }

            Save(document);
            return true;
        }
    }

    public static FamiliarExperienceResult? GrantExperience(string instanceId, int amount)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            var instance = FamiliarRosterService.Find(document, instanceId);
            if (instance == null)
            {
                return null;
            }

            var result = FamiliarRosterService.GrantExperience(instance, amount);
            Save(document);
            return result;
        }
    }

    public static FamiliarExperienceResult? GrantSelectedExperience(int amount)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            var instance = FamiliarRosterService.Selected(document);
            if (instance == null)
            {
                return null;
            }

            var result = FamiliarRosterService.GrantExperience(instance, amount);
            Save(document);
            return result;
        }
    }

    public static bool ChooseBlessing(string instanceId, string choiceId, string blessingId)
    {
        lock (SyncRoot)
        {
            var document = LoadAndNormalize(out _);
            if (!FamiliarRosterService.ChooseBlessing(document, instanceId, choiceId, blessingId))
            {
                return false;
            }

            Save(document);
            return true;
        }
    }

    public static bool SelectedHasBlessing(string blessingId)
    {
        var selected = Selected();
        return selected != null && selected.Blessings.Contains(blessingId ?? "", StringComparer.Ordinal);
    }

    public static bool SelectedHasTag(string tag)
    {
        var selected = Selected();
        return selected != null && FamiliarBlessingRegistry.HasTag(selected, tag);
    }

    public static bool SelectedHasEffect(string effectKind)
    {
        var selected = Selected();
        return selected != null && FamiliarBlessingRegistry.HasEffect(selected, effectKind);
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> BlessingsFor(FamiliarInstance instance)
    {
        if (instance == null || instance.Blessings == null || instance.Blessings.Count == 0)
        {
            return Array.Empty<FamiliarBlessingDefinition>();
        }

        var ids = new HashSet<string>(instance.Blessings, StringComparer.Ordinal);
        return FamiliarBlessingRegistry.All()
            .Where(blessing => ids.Contains(blessing.Id))
            .OrderBy(blessing => blessing.RequiredLevel)
            .ThenBy(blessing => blessing.Id, StringComparer.Ordinal)
            .ToArray();
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

    private static FamiliarRosterDocument Clone(FamiliarRosterDocument source, bool saveIfChanged)
    {
        if (saveIfChanged)
        {
            Save(source);
        }

        return new FamiliarRosterDocument
        {
            Version = source.Version,
            SelectedInstanceId = source.SelectedInstanceId,
            NextSerialBySpecies = new Dictionary<string, int>(source.NextSerialBySpecies, StringComparer.Ordinal),
            Instances = source.Instances.Select(instance => instance.Clone()).ToList()
        };
    }
}
