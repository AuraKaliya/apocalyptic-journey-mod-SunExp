using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class FamiliarRosterService
{
    public const int CurrentVersion = 3;
    public const int MaxLevel = 10;
    public const int BattleWinExperience = 3;
    public const int FinalBlessingLevel = 8;
    public const int RebirthLevel = 10;

    public static readonly IReadOnlyList<int> GrowthMilestones = new[] { 2, 4, 6 };

    public static bool Normalize(FamiliarRosterDocument document, IReadOnlyList<FamiliarSpeciesSpec> species)
    {
        var migratingLegacyDocument = document.Version < CurrentVersion;
        var changed = document.Version != CurrentVersion;
        document.Version = CurrentVersion;
        document.Instances ??= new List<FamiliarInstance>();

        var source = document.Instances.ToList();
        var normalized = new List<FamiliarInstance>();
        var consumed = new HashSet<FamiliarInstance>();
        foreach (var spec in species
                     .Where(item => !string.IsNullOrWhiteSpace(item.FullSpeciesId))
                     .GroupBy(item => item.FullSpeciesId, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var candidates = source
                .Where(item => Matches(item, spec))
                .Where(item => !item.LegacyDeleted)
                .OrderByDescending(item => item.Level)
                .ThenByDescending(item => item.Experience)
                .ThenByDescending(item => item.Aptitude)
                .ThenByDescending(item => item.LegacyIsBody)
                .ToList();
            var body = candidates.FirstOrDefault() ?? NewBody(spec);
            changed |= candidates.Count != 1 || !source.Contains(body);
            foreach (var candidate in source.Where(item => Matches(item, spec)))
            {
                consumed.Add(candidate);
            }

            changed |= NormalizeInstance(body, spec, migratingLegacyDocument);
            normalized.Add(body);
        }

        // Keep one profile for temporarily unavailable external Partners. Their registered
        // metadata can return on a later launch without losing progression.
        foreach (var group in source
                     .Where(item => !consumed.Contains(item) && !item.LegacyDeleted)
                     .GroupBy(ProfileIdentity, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                changed = true;
                continue;
            }

            var body = group
                .OrderByDescending(item => item.Level)
                .ThenByDescending(item => item.Experience)
                .ThenByDescending(item => item.Aptitude)
                .First();
            changed |= group.Count() != 1;
            changed |= NormalizeInstance(body, null, migratingLegacyDocument);
            normalized.Add(body);
        }

        var deduplicated = normalized
            .GroupBy(ProfileIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.FullSpeciesId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (document.Instances.Count != deduplicated.Count
            || document.Instances.Where((item, index) => index >= deduplicated.Count || !ReferenceEquals(item, deduplicated[index])).Any())
        {
            changed = true;
        }

        document.Instances = deduplicated;
        document.LegacySelectedInstanceId = "";
        document.LegacyNextSerialBySpecies = new Dictionary<string, int>(StringComparer.Ordinal);
        return changed;
    }

    public static FamiliarInstance? Find(FamiliarRosterDocument document, string instanceId)
    {
        var id = (instanceId ?? "").Trim();
        if (id.Length == 0)
        {
            return null;
        }

        return document.Instances.FirstOrDefault(instance =>
            string.Equals(instance.InstanceId, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.FullSpeciesId, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.SpeciesId, FamiliarId.NormalizeSpeciesId(id), StringComparison.OrdinalIgnoreCase));
    }

    public static bool Rename(FamiliarRosterDocument document, string instanceId, string name)
    {
        var instance = Find(document, instanceId);
        var clean = CleanName(name);
        if (instance == null || clean.Length == 0 || string.Equals(instance.Name, clean, StringComparison.Ordinal))
        {
            return false;
        }

        instance.Name = clean;
        return true;
    }

    public static FamiliarExperienceResult GrantExperience(FamiliarInstance instance, int amount)
    {
        var oldLevel = Math.Max(1, instance.Level);
        var oldExperience = Math.Max(0, instance.Experience);
        var gained = Math.Max(0, amount);
        instance.Level = oldLevel;
        instance.Experience = oldExperience + gained;
        while (instance.Level < MaxLevel)
        {
            var needed = ExperienceForNextLevel(instance.Level);
            if (instance.Experience < needed)
            {
                break;
            }

            instance.Experience -= needed;
            instance.Level++;
        }

        if (instance.Level >= MaxLevel)
        {
            instance.Level = MaxLevel;
            instance.Experience = 0;
        }

        EnsureNextPendingChoice(instance);
        return new FamiliarExperienceResult(instance, oldLevel, oldExperience, gained);
    }

    public static bool ChooseBlessing(FamiliarRosterDocument document, string instanceId, string choiceId, string blessingId)
    {
        var instance = Find(document, instanceId);
        if (instance == null)
        {
            return false;
        }

        var choice = instance.PendingBlessingChoices.FirstOrDefault(item =>
            string.Equals(item.ChoiceId, (choiceId ?? "").Trim(), StringComparison.Ordinal));
        var cleanBlessingId = (blessingId ?? "").Trim();
        if (choice == null || !choice.BlessingIds.Contains(cleanBlessingId, StringComparer.Ordinal))
        {
            return false;
        }

        var blessing = FamiliarBlessingRegistry.Find(cleanBlessingId);
        if (blessing == null || !FamiliarBlessingRegistry.Allows(blessing, instance))
        {
            return false;
        }

        if (string.Equals(choice.Kind, FamiliarChoiceKind.Final, StringComparison.Ordinal))
        {
            if (!FamiliarBlessingRegistry.IsFinal(blessing) || !string.IsNullOrWhiteSpace(instance.FinalBlessingId))
            {
                return false;
            }

            instance.FinalBlessingId = cleanBlessingId;
        }
        else
        {
            if (!FamiliarBlessingRegistry.IsGrowth(blessing)
                || instance.GrowthBlessingIds.Contains(cleanBlessingId, StringComparer.Ordinal))
            {
                return false;
            }

            instance.GrowthBlessingIds.Add(cleanBlessingId);
        }

        instance.PendingBlessingChoices.Clear();
        EnsureNextPendingChoice(instance);
        return true;
    }

    public static bool CanRebirth(FamiliarInstance? instance)
    {
        return instance != null
               && instance.Level >= RebirthLevel
               && instance.PendingBlessingChoices.Count == 0
               && instance.GrowthBlessingIds.Count >= GrowthMilestones.Count
               && !string.IsNullOrWhiteSpace(instance.FinalBlessingId);
    }

    public static FamiliarRebirthResult? Rebirth(FamiliarRosterDocument document, string instanceId)
    {
        var instance = Find(document, instanceId);
        if (!CanRebirth(instance))
        {
            return null;
        }

        var oldAptitude = instance!.Aptitude;
        instance.RebirthCount++;
        instance.Level = 1;
        instance.Experience = 0;
        instance.Aptitude = FamiliarBlessingRoller.RollAptitude(instance.FullSpeciesId, instance.RebirthCount);
        instance.GrowthBlessingIds.Clear();
        instance.FinalBlessingId = "";
        instance.PendingBlessingChoices.Clear();
        instance.BlessingRollIndex = 0;
        return new FamiliarRebirthResult(instance, oldAptitude, FamiliarBlessingRoller.AptitudeFloor(instance.RebirthCount));
    }

    public static int ExperienceForNextLevel(int level)
    {
        var safeLevel = Math.Max(1, Math.Min(MaxLevel, level));
        return safeLevel >= MaxLevel ? 0 : 20 + (safeLevel - 1) * 15;
    }

    private static FamiliarInstance NewBody(FamiliarSpeciesSpec spec)
    {
        var body = new FamiliarInstance
        {
            InstanceId = spec.FullSpeciesId,
            SpeciesId = spec.SpeciesId,
            FullSpeciesId = spec.FullSpeciesId,
            Name = spec.DisplayName,
            Level = 1,
            CreatedVersion = CurrentVersion
        };
        body.Aptitude = FamiliarBlessingRoller.DefaultAptitude(body);
        return body;
    }

    private static bool NormalizeInstance(FamiliarInstance instance, FamiliarSpeciesSpec? spec, bool migratingLegacyDocument)
    {
        var changed = false;
        var speciesId = spec?.SpeciesId ?? FamiliarId.NormalizeSpeciesId(instance.SpeciesId.Length > 0 ? instance.SpeciesId : instance.FullSpeciesId);
        var fullSpeciesId = spec?.FullSpeciesId
                            ?? FamiliarId.NormalizeFullSpeciesId(instance.FullSpeciesId.Length > 0 ? instance.FullSpeciesId : instance.InstanceId, speciesId);
        if (!string.Equals(instance.SpeciesId, speciesId, StringComparison.Ordinal))
        {
            instance.SpeciesId = speciesId;
            changed = true;
        }

        if (!string.Equals(instance.FullSpeciesId, fullSpeciesId, StringComparison.Ordinal))
        {
            instance.FullSpeciesId = fullSpeciesId;
            changed = true;
        }

        if (!string.Equals(instance.InstanceId, fullSpeciesId, StringComparison.Ordinal))
        {
            instance.InstanceId = fullSpeciesId;
            changed = true;
        }

        var name = CleanName(instance.Name).Length > 0 ? CleanName(instance.Name) : spec?.DisplayName ?? speciesId;
        if (!string.Equals(instance.Name, name, StringComparison.Ordinal))
        {
            instance.Name = name;
            changed = true;
        }

        var level = Math.Max(1, Math.Min(MaxLevel, instance.Level));
        if (instance.Level != level)
        {
            instance.Level = level;
            changed = true;
        }

        var experience = instance.Level >= MaxLevel ? 0 : Math.Max(0, instance.Experience);
        if (instance.Experience != experience)
        {
            instance.Experience = experience;
            changed = true;
        }

        instance.RebirthCount = Math.Max(0, instance.RebirthCount);
        var aptitude = FamiliarBlessingRoller.NormalizeAptitude(instance.Aptitude);
        if (aptitude == 0 && (migratingLegacyDocument || instance.CreatedVersion < CurrentVersion))
        {
            aptitude = FamiliarBlessingRoller.RollAptitude(fullSpeciesId, instance.RebirthCount);
        }

        if (instance.Aptitude != aptitude)
        {
            instance.Aptitude = aptitude;
            changed = true;
        }

        var legacy = (instance.LegacyBlessings ?? new List<string>())
            .Concat(instance.GrowthBlessingIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var growthLimit = GrowthMilestones.Count(milestone => instance.Level >= milestone);
        var growth = legacy
            .Where(id => FamiliarBlessingRegistry.Find(id) is { } blessing && FamiliarBlessingRegistry.IsGrowth(blessing))
            .Take(growthLimit)
            .ToList();
        var final = (instance.FinalBlessingId ?? "").Trim();
        if (instance.Level < FinalBlessingLevel || FamiliarBlessingRegistry.Find(final) is not { } currentFinal || !FamiliarBlessingRegistry.IsFinal(currentFinal))
        {
            final = instance.Level >= FinalBlessingLevel
                ? legacy.FirstOrDefault(id => FamiliarBlessingRegistry.Find(id) is { } blessing && FamiliarBlessingRegistry.IsFinal(blessing)) ?? ""
                : "";
        }

        if (!instance.GrowthBlessingIds.SequenceEqual(growth, StringComparer.Ordinal))
        {
            instance.GrowthBlessingIds = growth;
            changed = true;
        }

        if (!string.Equals(instance.FinalBlessingId, final, StringComparison.Ordinal))
        {
            instance.FinalBlessingId = final;
            changed = true;
        }
        instance.LegacyBlessings = new List<string>();
        instance.LegacyIsBody = false;
        instance.LegacyDeleted = false;
        instance.BlessingRollIndex = Math.Max(0, instance.BlessingRollIndex);
        instance.CreatedVersion = CurrentVersion;

        var pendingBefore = instance.PendingBlessingChoices?.Select(item => item.Clone()).ToList() ?? new List<FamiliarBlessingChoice>();
        instance.PendingBlessingChoices = NormalizePendingChoice(instance);
        EnsureNextPendingChoice(instance);
        if (!SameChoices(pendingBefore, instance.PendingBlessingChoices))
        {
            changed = true;
        }

        return changed;
    }

    private static void EnsureNextPendingChoice(FamiliarInstance instance)
    {
        var nextMilestone = NextMilestone(instance);
        if (nextMilestone == 0)
        {
            instance.PendingBlessingChoices.Clear();
            return;
        }

        var kind = nextMilestone == FinalBlessingLevel ? FamiliarChoiceKind.Final : FamiliarChoiceKind.Growth;
        var current = instance.PendingBlessingChoices.FirstOrDefault();
        if (current != null && current.Level == nextMilestone && string.Equals(current.Kind, kind, StringComparison.Ordinal))
        {
            instance.PendingBlessingChoices = new List<FamiliarBlessingChoice> { current };
            return;
        }

        instance.PendingBlessingChoices.Clear();
        var choice = FamiliarBlessingRoller.CreateChoice(instance, nextMilestone);
        if (choice != null)
        {
            instance.PendingBlessingChoices.Add(choice);
            instance.BlessingRollIndex++;
        }
    }

    private static int NextMilestone(FamiliarInstance instance)
    {
        if (instance.GrowthBlessingIds.Count < GrowthMilestones.Count)
        {
            var milestone = GrowthMilestones[instance.GrowthBlessingIds.Count];
            return instance.Level >= milestone ? milestone : 0;
        }

        return instance.Level >= FinalBlessingLevel && string.IsNullOrWhiteSpace(instance.FinalBlessingId)
            ? FinalBlessingLevel
            : 0;
    }

    private static List<FamiliarBlessingChoice> NormalizePendingChoice(FamiliarInstance instance)
    {
        var choice = (instance.PendingBlessingChoices ?? new List<FamiliarBlessingChoice>()).FirstOrDefault();
        if (choice == null)
        {
            return new List<FamiliarBlessingChoice>();
        }

        choice.ChoiceId = string.IsNullOrWhiteSpace(choice.ChoiceId) ? "choice-migrated" : choice.ChoiceId.Trim();
        choice.Level = Math.Max(1, Math.Min(FinalBlessingLevel, choice.Level));
        choice.Tier = Math.Max(1, Math.Min(5, choice.Tier));
        choice.Kind = choice.Level >= FinalBlessingLevel ? FamiliarChoiceKind.Final : FamiliarChoiceKind.Growth;
        var ids = (choice.BlessingIds ?? new List<string>())
            .Where(id => FamiliarBlessingRegistry.Find(id) is { } blessing
                         && FamiliarBlessingRegistry.Allows(blessing, instance)
                         && (choice.Kind == FamiliarChoiceKind.Final
                             ? FamiliarBlessingRegistry.IsFinal(blessing)
                             : FamiliarBlessingRegistry.IsGrowth(blessing)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            return new List<FamiliarBlessingChoice>();
        }

        choice.BlessingIds = ids;
        return new List<FamiliarBlessingChoice> { choice };
    }

    private static bool Matches(FamiliarInstance instance, FamiliarSpeciesSpec spec)
    {
        return FamiliarId.Matches(instance.FullSpeciesId, spec)
               || FamiliarId.Matches(instance.InstanceId, spec)
               || FamiliarId.Matches(instance.SpeciesId, spec);
    }

    private static string ProfileIdentity(FamiliarInstance instance)
    {
        return FamiliarId.NormalizeFullSpeciesId(
            instance.FullSpeciesId.Length > 0 ? instance.FullSpeciesId : instance.InstanceId,
            instance.SpeciesId);
    }

    private static bool SameChoices(IReadOnlyList<FamiliarBlessingChoice> left, IReadOnlyList<FamiliarBlessingChoice> right)
    {
        return left.Count == right.Count && !left.Where((choice, index) =>
            !string.Equals(choice.ChoiceId, right[index].ChoiceId, StringComparison.Ordinal)
            || choice.Level != right[index].Level
            || !string.Equals(choice.Kind, right[index].Kind, StringComparison.Ordinal)
            || !choice.BlessingIds.SequenceEqual(right[index].BlessingIds, StringComparer.Ordinal)).Any();
    }

    private static string CleanName(string name)
    {
        var clean = (name ?? "").Trim();
        return clean.Length > 24 ? clean.Substring(0, 24) : clean;
    }
}
