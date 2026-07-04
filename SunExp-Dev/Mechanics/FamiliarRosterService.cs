using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class FamiliarRosterService
{
    public const int CurrentVersion = 2;
    public const int MaxLevel = 10;
    public const int DefaultTrainingExperience = 10;
    public const int BattleWinExperience = 3;

    public static bool Normalize(FamiliarRosterDocument document, IReadOnlyList<FamiliarSpeciesSpec> species)
    {
        var changed = false;
        if (document.Version <= 0 || document.Version < CurrentVersion)
        {
            document.Version = CurrentVersion;
            changed = true;
        }

        document.NextSerialBySpecies ??= new Dictionary<string, int>(StringComparer.Ordinal);
        document.Instances ??= new List<FamiliarInstance>();

        foreach (var instance in document.Instances)
        {
            changed |= NormalizeInstance(instance, species);
        }

        foreach (var spec in species)
        {
            if (EnsureBody(document, spec))
            {
                changed = true;
            }

            var next = Math.Max(1, document.NextSerialBySpecies.TryGetValue(spec.SpeciesId, out var value) ? value : 1);
            var highest = document.Instances
                .Where(instance => string.Equals(instance.SpeciesId, spec.SpeciesId, StringComparison.Ordinal))
                .Select(instance => SerialFromInstanceId(instance.InstanceId))
                .DefaultIfEmpty(0)
                .Max();
            next = Math.Max(next, highest + 1);
            if (!document.NextSerialBySpecies.TryGetValue(spec.SpeciesId, out var existing) || existing != next)
            {
                document.NextSerialBySpecies[spec.SpeciesId] = next;
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(document.SelectedInstanceId)
            || Find(document, document.SelectedInstanceId) == null)
        {
            var firstBody = species
                .Select(spec => Find(document, spec.BodyInstanceId))
                .FirstOrDefault(instance => instance != null);
            if (firstBody != null)
            {
                document.SelectedInstanceId = firstBody.InstanceId;
                changed = true;
            }
        }

        return changed;
    }

    public static FamiliarInstance? Find(FamiliarRosterDocument document, string instanceId)
    {
        var id = (instanceId ?? "").Trim();
        return id.Length == 0
            ? null
            : document.Instances.FirstOrDefault(instance =>
                !instance.Deleted && string.Equals(instance.InstanceId, id, StringComparison.Ordinal));
    }

    public static FamiliarInstance? Selected(FamiliarRosterDocument document)
    {
        return Find(document, document.SelectedInstanceId);
    }

    public static FamiliarInstance Create(FamiliarRosterDocument document, FamiliarSpeciesSpec species)
    {
        Normalize(document, new[] { species });
        var serial = Math.Max(1, document.NextSerialBySpecies.TryGetValue(species.SpeciesId, out var value) ? value : 1);
        var instance = new FamiliarInstance
        {
            SpeciesId = species.SpeciesId,
            InstanceId = FamiliarId.InstanceId(species.SpeciesId, serial),
            Name = species.DisplayName + " " + serial.ToString("000"),
            Level = 1,
            Experience = 0,
            Aptitude = FamiliarBlessingRoller.DefaultAptitude(new FamiliarInstance
            {
                InstanceId = FamiliarId.InstanceId(species.SpeciesId, serial),
                SpeciesId = species.SpeciesId,
                IsBody = false
            }),
            IsBody = false,
            CreatedVersion = CurrentVersion,
            Blessings = new List<string>(),
            PendingBlessingChoices = new List<FamiliarBlessingChoice>()
        };

        document.Instances.Add(instance);
        document.NextSerialBySpecies[species.SpeciesId] = serial + 1;
        return instance;
    }

    public static bool Delete(FamiliarRosterDocument document, string instanceId)
    {
        var instance = Find(document, instanceId);
        if (instance == null || instance.IsBody)
        {
            return false;
        }

        instance.Deleted = true;
        if (string.Equals(document.SelectedInstanceId, instance.InstanceId, StringComparison.Ordinal))
        {
            document.SelectedInstanceId = document.Instances
                .FirstOrDefault(item => !item.Deleted && item.IsBody && string.Equals(item.SpeciesId, instance.SpeciesId, StringComparison.Ordinal))
                ?.InstanceId
                ?? document.Instances.FirstOrDefault(item => !item.Deleted)?.InstanceId
                ?? "";
        }

        return true;
    }

    public static bool Rename(FamiliarRosterDocument document, string instanceId, string name)
    {
        var instance = Find(document, instanceId);
        if (instance == null)
        {
            return false;
        }

        var clean = CleanName(name);
        if (clean.Length == 0 || string.Equals(instance.Name, clean, StringComparison.Ordinal))
        {
            return false;
        }

        instance.Name = clean;
        return true;
    }

    public static bool Select(FamiliarRosterDocument document, string instanceId)
    {
        var instance = Find(document, instanceId);
        if (instance == null)
        {
            return false;
        }

        document.SelectedInstanceId = instance.InstanceId;
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

        for (var level = oldLevel + 1; level <= instance.Level; level++)
        {
            EnqueueBlessingChoice(instance, level);
        }

        return new FamiliarExperienceResult(instance, oldLevel, oldExperience, gained);
    }

    public static bool ChooseBlessing(FamiliarRosterDocument document, string instanceId, string choiceId, string blessingId)
    {
        var instance = Find(document, instanceId);
        if (instance == null)
        {
            return false;
        }

        instance.PendingBlessingChoices ??= new List<FamiliarBlessingChoice>();
        var cleanChoiceId = (choiceId ?? "").Trim();
        var cleanBlessingId = (blessingId ?? "").Trim();
        var choice = instance.PendingBlessingChoices.FirstOrDefault(item =>
            string.Equals(item.ChoiceId, cleanChoiceId, StringComparison.Ordinal));
        if (choice == null || !choice.BlessingIds.Contains(cleanBlessingId, StringComparer.Ordinal))
        {
            return false;
        }

        var blessing = FamiliarBlessingRegistry.Find(cleanBlessingId);
        if (blessing == null || !FamiliarBlessingRegistry.Allows(blessing, instance.SpeciesId))
        {
            return false;
        }

        instance.Blessings ??= new List<string>();
        if (!instance.Blessings.Contains(cleanBlessingId, StringComparer.Ordinal))
        {
            instance.Blessings.Add(cleanBlessingId);
        }

        instance.PendingBlessingChoices.Remove(choice);
        return true;
    }

    public static int ExperienceForNextLevel(int level)
    {
        var safeLevel = Math.Max(1, Math.Min(MaxLevel, level));
        return safeLevel >= MaxLevel ? 0 : 20 + (safeLevel - 1) * 15;
    }

    private static bool EnsureBody(FamiliarRosterDocument document, FamiliarSpeciesSpec species)
    {
        var bodyId = species.BodyInstanceId;
        var body = Find(document, bodyId);
        if (body == null)
        {
            body = new FamiliarInstance
            {
                InstanceId = bodyId,
                SpeciesId = species.SpeciesId,
                Name = species.DisplayName,
                Level = 1,
                Experience = 0,
                Aptitude = FamiliarBlessingRoller.BodyDefaultAptitude,
                IsBody = true,
                CreatedVersion = CurrentVersion,
                Blessings = new List<string>(),
                PendingBlessingChoices = new List<FamiliarBlessingChoice>()
            };
            document.Instances.Add(body);
            return true;
        }

        var changed = false;
        if (!body.IsBody)
        {
            body.IsBody = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(body.Name))
        {
            body.Name = species.DisplayName;
            changed = true;
        }

        if (body.Aptitude != FamiliarBlessingRoller.BodyDefaultAptitude)
        {
            body.Aptitude = FamiliarBlessingRoller.BodyDefaultAptitude;
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeInstance(FamiliarInstance instance, IReadOnlyList<FamiliarSpeciesSpec> species)
    {
        var changed = false;
        var wasCreatedBeforeAptitude = instance.CreatedVersion < CurrentVersion;
        var normalizedSpecies = FamiliarId.NormalizeSpeciesId(instance.SpeciesId);
        if (!string.Equals(instance.SpeciesId, normalizedSpecies, StringComparison.Ordinal))
        {
            instance.SpeciesId = normalizedSpecies;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(instance.InstanceId))
        {
            instance.InstanceId = instance.IsBody
                ? FamiliarId.BodyInstanceId(instance.SpeciesId)
                : FamiliarId.InstanceId(instance.SpeciesId, 1);
            changed = true;
        }

        if (instance.IsBody && !string.Equals(instance.InstanceId, FamiliarId.BodyInstanceId(instance.SpeciesId), StringComparison.Ordinal))
        {
            instance.InstanceId = FamiliarId.BodyInstanceId(instance.SpeciesId);
            changed = true;
        }

        var level = Math.Max(1, Math.Min(MaxLevel, instance.Level));
        if (instance.Level != level)
        {
            instance.Level = level;
            changed = true;
        }

        var experience = Math.Max(0, instance.Level >= MaxLevel ? 0 : instance.Experience);
        if (instance.Experience != experience)
        {
            instance.Experience = experience;
            changed = true;
        }

        var aptitude = wasCreatedBeforeAptitude && instance.Aptitude == 0 && !instance.IsBody
            ? FamiliarBlessingRoller.DefaultAptitude(instance)
            : FamiliarBlessingRoller.NormalizeAptitude(instance.IsBody ? FamiliarBlessingRoller.BodyDefaultAptitude : instance.Aptitude);
        if (instance.Aptitude != aptitude)
        {
            instance.Aptitude = aptitude;
            changed = true;
        }

        var createdVersion = Math.Max(1, Math.Min(CurrentVersion, instance.CreatedVersion));
        if (instance.CreatedVersion != createdVersion)
        {
            instance.CreatedVersion = createdVersion;
            changed = true;
        }

        instance.Blessings ??= new List<string>();
        var normalizedBlessings = instance.Blessings
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedBlessings.Count != instance.Blessings.Count
            || normalizedBlessings.Where((id, index) => !string.Equals(id, instance.Blessings[index], StringComparison.Ordinal)).Any())
        {
            instance.Blessings = normalizedBlessings;
            changed = true;
        }

        instance.PendingBlessingChoices ??= new List<FamiliarBlessingChoice>();
        var normalizedChoices = NormalizePendingChoices(instance);
        if (normalizedChoices.Count != instance.PendingBlessingChoices.Count
            || normalizedChoices.Where((choice, index) => !SameChoice(choice, instance.PendingBlessingChoices[index])).Any())
        {
            instance.PendingBlessingChoices = normalizedChoices;
            changed = true;
        }

        var rollIndex = Math.Max(instance.BlessingRollIndex, instance.PendingBlessingChoices.Count);
        if (instance.BlessingRollIndex != rollIndex)
        {
            instance.BlessingRollIndex = rollIndex;
            changed = true;
        }

        if (instance.CreatedVersion < CurrentVersion)
        {
            instance.CreatedVersion = CurrentVersion;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            instance.Name = species.FirstOrDefault(spec => string.Equals(spec.SpeciesId, instance.SpeciesId, StringComparison.Ordinal))?.DisplayName
                            ?? instance.InstanceId;
            changed = true;
        }

        return changed;
    }

    private static void EnqueueBlessingChoice(FamiliarInstance instance, int level)
    {
        instance.PendingBlessingChoices ??= new List<FamiliarBlessingChoice>();
        var choice = FamiliarBlessingRoller.CreateChoice(instance, level);
        if (choice == null)
        {
            return;
        }

        instance.PendingBlessingChoices.Add(choice);
        instance.BlessingRollIndex++;
    }

    private static List<FamiliarBlessingChoice> NormalizePendingChoices(FamiliarInstance instance)
    {
        var result = new List<FamiliarBlessingChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in instance.PendingBlessingChoices ?? new List<FamiliarBlessingChoice>())
        {
            var choiceId = string.IsNullOrWhiteSpace(choice.ChoiceId)
                ? "choice-" + result.Count.ToString("000")
                : choice.ChoiceId.Trim();
            if (!seen.Add(choiceId))
            {
                continue;
            }

            var blessingIds = (choice.BlessingIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Where(id => FamiliarBlessingRegistry.Find(id) != null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (blessingIds.Count == 0)
            {
                continue;
            }

            result.Add(new FamiliarBlessingChoice
            {
                ChoiceId = choiceId,
                Level = Math.Max(1, Math.Min(MaxLevel, choice.Level)),
                Tier = Math.Max(1, Math.Min(5, choice.Tier)),
                BlessingIds = blessingIds
            });
        }

        return result;
    }

    private static bool SameChoice(FamiliarBlessingChoice left, FamiliarBlessingChoice right)
    {
        return string.Equals(left.ChoiceId, right.ChoiceId, StringComparison.Ordinal)
               && left.Level == right.Level
               && left.Tier == right.Tier
               && left.BlessingIds.SequenceEqual(right.BlessingIds, StringComparer.Ordinal);
    }

    private static int SerialFromInstanceId(string instanceId)
    {
        var value = (instanceId ?? "").Trim();
        var index = value.LastIndexOf('-');
        if (index < 0 || index >= value.Length - 1)
        {
            return 0;
        }

        return DictionaryUtil.ParseInt(value.Substring(index + 1));
    }

    private static string CleanName(string name)
    {
        var clean = (name ?? "").Trim();
        return clean.Length > 24 ? clean.Substring(0, 24) : clean;
    }
}
