using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

public sealed class FamiliarSpeciesSpec
{
    public FamiliarSpeciesSpec(
        string speciesId,
        string fullSpeciesId,
        string displayName,
        string description,
        string iconPath,
        string modelPath,
        string animationPath,
        string nativeBlessingId)
    {
        SpeciesId = FamiliarId.NormalizeSpeciesId(speciesId);
        FullSpeciesId = string.IsNullOrWhiteSpace(fullSpeciesId) ? SpeciesId : fullSpeciesId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? SpeciesId : displayName.Trim();
        Description = description?.Trim() ?? "";
        IconPath = iconPath?.Trim() ?? "";
        ModelPath = modelPath?.Trim() ?? "";
        AnimationPath = animationPath?.Trim() ?? "";
        NativeBlessingId = nativeBlessingId?.Trim() ?? "";
    }

    public string SpeciesId { get; }

    public string FullSpeciesId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string IconPath { get; }

    public string ModelPath { get; }

    public string AnimationPath { get; }

    public string NativeBlessingId { get; }

    public string BodyInstanceId => FamiliarId.BodyInstanceId(SpeciesId);
}

public sealed class FamiliarInstance
{
    public string InstanceId { get; set; } = "";

    public string SpeciesId { get; set; } = "";

    public string Name { get; set; } = "";

    public int Level { get; set; } = 1;

    public int Experience { get; set; }

    public int Aptitude { get; set; }

    public List<string> Blessings { get; set; } = new();

    public List<FamiliarBlessingChoice> PendingBlessingChoices { get; set; } = new();

    public int BlessingRollIndex { get; set; }

    public bool IsBody { get; set; }

    public int CreatedVersion { get; set; } = 1;

    public bool Deleted { get; set; }

    public FamiliarInstance Clone()
    {
        return new FamiliarInstance
        {
            InstanceId = InstanceId,
            SpeciesId = SpeciesId,
            Name = Name,
            Level = Level,
            Experience = Experience,
            Aptitude = Aptitude,
            Blessings = Blessings.ToList(),
            PendingBlessingChoices = PendingBlessingChoices.Select(choice => choice.Clone()).ToList(),
            BlessingRollIndex = BlessingRollIndex,
            IsBody = IsBody,
            CreatedVersion = CreatedVersion,
            Deleted = Deleted
        };
    }
}

public sealed class FamiliarBlessingChoice
{
    public string ChoiceId { get; set; } = "";

    public int Level { get; set; }

    public int Tier { get; set; }

    public List<string> BlessingIds { get; set; } = new();

    public FamiliarBlessingChoice Clone()
    {
        return new FamiliarBlessingChoice
        {
            ChoiceId = ChoiceId,
            Level = Level,
            Tier = Tier,
            BlessingIds = BlessingIds.ToList()
        };
    }
}

public sealed class FamiliarRosterDocument
{
    public int Version { get; set; } = 1;

    public string SelectedInstanceId { get; set; } = "";

    public Dictionary<string, int> NextSerialBySpecies { get; set; } = new(StringComparer.Ordinal);

    public List<FamiliarInstance> Instances { get; set; } = new();
}

public sealed class FamiliarBlessingRegistryDocument
{
    public List<FamiliarBlessingDefinition> Blessings { get; set; } = new();
}

public sealed class FamiliarBlessingDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconPath { get; set; } = "";

    public int Tier { get; set; } = 1;

    public int Weight { get; set; } = 100;

    public string Pool { get; set; } = "common";

    public string ExclusiveGroup { get; set; } = "";

    public int RequiredLevel { get; set; } = 1;

    public int MaxRank { get; set; } = 1;

    public List<string> AllowedSpecies { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public List<FamiliarBlessingEffect> Effects { get; set; } = new();
}

public sealed class FamiliarBlessingEffect
{
    public string Kind { get; set; } = "";

    public string Value { get; set; } = "";

    public string Pool { get; set; } = "";

    public int Amount { get; set; }
}

public readonly struct FamiliarExperienceResult
{
    public FamiliarExperienceResult(FamiliarInstance instance, int oldLevel, int oldExperience, int gainedExperience)
    {
        Instance = instance;
        OldLevel = oldLevel;
        OldExperience = oldExperience;
        GainedExperience = Math.Max(0, gainedExperience);
    }

    public FamiliarInstance Instance { get; }

    public int OldLevel { get; }

    public int OldExperience { get; }

    public int GainedExperience { get; }

    public bool LeveledUp => Instance.Level > OldLevel;
}

public interface IFamiliarProfileStore
{
    FamiliarRosterDocument Load();

    void Save(FamiliarRosterDocument document);
}

public static class FamiliarId
{
    private const string SunExpPartnerPrefix = "SunExp_sunexp_";

    public static string NormalizeSpeciesId(string? speciesId)
    {
        var value = (speciesId ?? "").Trim();
        if (value.StartsWith(SunExpPartnerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(SunExpPartnerPrefix.Length);
        }

        return Sanitize(value).ToLowerInvariant();
    }

    public static string BodyInstanceId(string speciesId)
    {
        var normalized = NormalizeSpeciesId(speciesId);
        return normalized.Length == 0 ? "" : normalized + "-000";
    }

    public static string InstanceId(string speciesId, int serial)
    {
        var normalized = NormalizeSpeciesId(speciesId);
        return normalized.Length == 0 ? "" : normalized + "-" + Math.Max(0, serial).ToString("000");
    }

    public static string Sanitize(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
        {
            return "";
        }

        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            chars[i] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_';
        }

        return new string(chars).Trim('_', '-');
    }
}
