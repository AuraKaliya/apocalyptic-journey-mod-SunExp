using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SunExp.Dll.Mechanics;

public static class FamiliarBlessingCategory
{
    public const string Growth = "growth";
    public const string FinalGeneric = "final-generic";
    public const string FinalSpecies = "final-species";
    public const string FinalTag = "final-tag";
}

public static class FamiliarChoiceKind
{
    public const string Growth = "growth";
    public const string Final = "final";
}

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
        FullSpeciesId = FamiliarId.NormalizeFullSpeciesId(fullSpeciesId, SpeciesId);
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

    public string BodyInstanceId => FullSpeciesId;
}

public sealed class FamiliarInstance
{
    public string InstanceId { get; set; } = "";

    public string SpeciesId { get; set; } = "";

    public string FullSpeciesId { get; set; } = "";

    public string Name { get; set; } = "";

    public int Level { get; set; } = 1;

    public int Experience { get; set; }

    public int Aptitude { get; set; }

    public List<string> GrowthBlessingIds { get; set; } = new();

    public string FinalBlessingId { get; set; } = "";

    public List<FamiliarBlessingChoice> PendingBlessingChoices { get; set; } = new();

    public int BlessingRollIndex { get; set; }

    public int RebirthCount { get; set; }

    public int CreatedVersion { get; set; } = FamiliarRosterService.CurrentVersion;

    [JsonProperty("Blessings")]
    public List<string> LegacyBlessings { get; set; } = new();

    [JsonProperty("IsBody")]
    public bool LegacyIsBody { get; set; }

    [JsonProperty("Deleted")]
    public bool LegacyDeleted { get; set; }

    public bool ShouldSerializeLegacyBlessings() => false;

    public bool ShouldSerializeLegacyIsBody() => false;

    public bool ShouldSerializeLegacyDeleted() => false;

    public IReadOnlyList<string> AllBlessingIds()
    {
        return GrowthBlessingIds
            .Concat(string.IsNullOrWhiteSpace(FinalBlessingId) ? Array.Empty<string>() : new[] { FinalBlessingId })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public FamiliarInstance Clone()
    {
        return new FamiliarInstance
        {
            InstanceId = InstanceId,
            SpeciesId = SpeciesId,
            FullSpeciesId = FullSpeciesId,
            Name = Name,
            Level = Level,
            Experience = Experience,
            Aptitude = Aptitude,
            GrowthBlessingIds = GrowthBlessingIds.ToList(),
            FinalBlessingId = FinalBlessingId,
            PendingBlessingChoices = PendingBlessingChoices.Select(choice => choice.Clone()).ToList(),
            BlessingRollIndex = BlessingRollIndex,
            RebirthCount = RebirthCount,
            CreatedVersion = CreatedVersion,
            LegacyBlessings = LegacyBlessings.ToList(),
            LegacyIsBody = LegacyIsBody,
            LegacyDeleted = LegacyDeleted
        };
    }
}

public sealed class FamiliarBlessingChoice
{
    public string ChoiceId { get; set; } = "";

    public int Level { get; set; }

    public int Tier { get; set; }

    public string Kind { get; set; } = FamiliarChoiceKind.Growth;

    public List<string> BlessingIds { get; set; } = new();

    public FamiliarBlessingChoice Clone()
    {
        return new FamiliarBlessingChoice
        {
            ChoiceId = ChoiceId,
            Level = Level,
            Tier = Tier,
            Kind = Kind,
            BlessingIds = BlessingIds.ToList()
        };
    }
}

public sealed class FamiliarRosterDocument
{
    public int Version { get; set; } = FamiliarRosterService.CurrentVersion;

    public List<FamiliarInstance> Instances { get; set; } = new();

    [JsonProperty("SelectedInstanceId")]
    public string LegacySelectedInstanceId { get; set; } = "";

    [JsonProperty("NextSerialBySpecies")]
    public Dictionary<string, int> LegacyNextSerialBySpecies { get; set; } = new(StringComparer.Ordinal);

    public bool ShouldSerializeLegacySelectedInstanceId() => false;

    public bool ShouldSerializeLegacyNextSerialBySpecies() => false;
}

public sealed class FamiliarBlessingRegistryDocument
{
    public int SchemaVersion { get; set; } = 3;

    public string OwnerModId { get; set; } = "";

    public List<FamiliarBlessingDefinition> Blessings { get; set; } = new();

    public List<FamiliarSpeciesGrowthProfile> SpeciesProfiles { get; set; } = new();
}

public sealed class FamiliarSpeciesGrowthProfile
{
    public string FullSpeciesId { get; set; } = "";

    public string SpeciesId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public List<string> Tags { get; set; } = new();

    public List<string> FinalBlessingIds { get; set; } = new();
}

public sealed class FamiliarBlessingDefinition
{
    public string Id { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconPath { get; set; } = "";

    public string Category { get; set; } = FamiliarBlessingCategory.Growth;

    public int Tier { get; set; } = 1;

    public int Weight { get; set; } = 100;

    public string Pool { get; set; } = "";

    public string ExclusiveGroup { get; set; } = "";

    public int RequiredLevel { get; set; } = 1;

    public int MaxRank { get; set; } = 1;

    public List<string> AllowedSpecies { get; set; } = new();

    public List<string> RequiredTags { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public List<FamiliarBlessingEffect> Effects { get; set; } = new();
}

public sealed class FamiliarBlessingEffect
{
    public string Kind { get; set; } = "";

    public string Value { get; set; } = "";

    public string Pool { get; set; } = "";

    public int Amount { get; set; }

    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

public readonly struct FamiliarRebirthResult
{
    public FamiliarRebirthResult(FamiliarInstance instance, int oldAptitude, int aptitudeFloor)
    {
        Instance = instance;
        OldAptitude = oldAptitude;
        AptitudeFloor = aptitudeFloor;
    }

    public FamiliarInstance Instance { get; }

    public int OldAptitude { get; }

    public int AptitudeFloor { get; }
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

    public static string NormalizeFullSpeciesId(string? fullSpeciesId, string? fallbackSpeciesId = null)
    {
        var value = (fullSpeciesId ?? "").Trim();
        return value.Length > 0 ? value : NormalizeSpeciesId(fallbackSpeciesId);
    }

    public static string BodyInstanceId(string fullSpeciesId)
    {
        return NormalizeFullSpeciesId(fullSpeciesId);
    }

    public static bool Matches(string? candidate, FamiliarSpeciesSpec species)
    {
        var value = (candidate ?? "").Trim();
        return value.Length > 0
               && (string.Equals(value, species.FullSpeciesId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(NormalizeSpeciesId(value), species.SpeciesId, StringComparison.OrdinalIgnoreCase)
                   || value.EndsWith("_" + species.SpeciesId, StringComparison.OrdinalIgnoreCase));
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
