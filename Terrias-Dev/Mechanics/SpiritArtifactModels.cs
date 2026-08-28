using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactSlots
{
    public const string Flower = "flower";
    public const string Plume = "plume";
    public const string Sands = "sands";
    public const string Goblet = "goblet";
    public const string Circlet = "circlet";

    public static IReadOnlyList<string> All { get; } = new[] { Flower, Plume, Sands, Goblet, Circlet };

    public static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return All.Contains(normalized, StringComparer.Ordinal) ? normalized : "";
    }

    public static string DisplayName(string? value)
    {
        return Normalize(value) switch
        {
            Flower => "生之花",
            Plume => "死之羽",
            Sands => "时之沙",
            Goblet => "空之杯",
            Circlet => "理之冠",
            _ => "未知部件"
        };
    }
}

public static class SpiritArtifactStats
{
    public const string Life = "life";
    public const string Magic = "magic";
    public const string Spirit = "spirit";
    public const string Luck = "luck";
    public const string Perception = "perception";
    public const string Speed = "speed";
    public const string MaxMagic = "max-magic";
    public const string Extraordinary = "extraordinary";

    public static IReadOnlyList<string> MainChoiceStats { get; } =
        new[] { Magic, Spirit, Luck, Perception, Speed };

    public static IReadOnlyList<string> SubStats { get; } =
        new[] { Life, Magic, Spirit, Luck, Perception, Speed, MaxMagic, Extraordinary };

    public static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return SubStats.Contains(normalized, StringComparer.Ordinal) ? normalized : "";
    }

    public static string DisplayName(string? value)
    {
        return Normalize(value) switch
        {
            Life => "生命",
            Magic => "魔力",
            Spirit => "精神",
            Luck => "幸运",
            Perception => "感知",
            Speed => "速度",
            MaxMagic => "魔能上限",
            Extraordinary => "超凡",
            _ => "未知词条"
        };
    }
}

[Serializable]
public sealed class SpiritArtifactStatRoll
{
    public string StatId { get; set; } = "";

    public int Value { get; set; }

    public SpiritArtifactStatRoll Clone()
    {
        return new SpiritArtifactStatRoll { StatId = StatId, Value = Value };
    }
}

[Serializable]
public sealed class SpiritArtifactInstance
{
    public string ArtifactUid { get; set; } = "";

    public string SetId { get; set; } = "";

    public string PieceId { get; set; } = "";

    public string SlotId { get; set; } = "";

    public int Rarity { get; set; } = 1;

    public int Level { get; set; } = 1;

    public SpiritArtifactStatRoll MainStat { get; set; } = new();

    public List<SpiritArtifactStatRoll> SubStatRolls { get; set; } = new();

    public int InvestedEssence { get; set; }

    public bool Locked { get; set; }

    public string AcquiredAt { get; set; } = "";

    public string AcquisitionToken { get; set; } = "";

    public SpiritArtifactInstance Clone()
    {
        return new SpiritArtifactInstance
        {
            ArtifactUid = ArtifactUid,
            SetId = SetId,
            PieceId = PieceId,
            SlotId = SlotId,
            Rarity = Rarity,
            Level = Level,
            MainStat = MainStat?.Clone() ?? new SpiritArtifactStatRoll(),
            SubStatRolls = (SubStatRolls ?? new List<SpiritArtifactStatRoll>())
                .Select(value => value.Clone())
                .ToList(),
            InvestedEssence = InvestedEssence,
            Locked = Locked,
            AcquiredAt = AcquiredAt,
            AcquisitionToken = AcquisitionToken
        };
    }
}

[Serializable]
public sealed class SpiritArtifactLoadout
{
    public int Revision { get; set; }

    public string LoadoutHash { get; set; } = "";

    public string FlowerArtifactUid { get; set; } = "";

    public string PlumeArtifactUid { get; set; } = "";

    public string SandsArtifactUid { get; set; } = "";

    public string GobletArtifactUid { get; set; } = "";

    public string CircletArtifactUid { get; set; } = "";

    public string Get(string? slotId)
    {
        return SpiritArtifactSlots.Normalize(slotId) switch
        {
            SpiritArtifactSlots.Flower => FlowerArtifactUid ?? "",
            SpiritArtifactSlots.Plume => PlumeArtifactUid ?? "",
            SpiritArtifactSlots.Sands => SandsArtifactUid ?? "",
            SpiritArtifactSlots.Goblet => GobletArtifactUid ?? "",
            SpiritArtifactSlots.Circlet => CircletArtifactUid ?? "",
            _ => ""
        };
    }

    public bool Set(string? slotId, string? artifactUid)
    {
        var value = (artifactUid ?? "").Trim();
        switch (SpiritArtifactSlots.Normalize(slotId))
        {
            case SpiritArtifactSlots.Flower: FlowerArtifactUid = value; return true;
            case SpiritArtifactSlots.Plume: PlumeArtifactUid = value; return true;
            case SpiritArtifactSlots.Sands: SandsArtifactUid = value; return true;
            case SpiritArtifactSlots.Goblet: GobletArtifactUid = value; return true;
            case SpiritArtifactSlots.Circlet: CircletArtifactUid = value; return true;
            default: return false;
        }
    }

    public IReadOnlyList<string> ArtifactUids()
    {
        return SpiritArtifactSlots.All.Select(Get)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public SpiritArtifactLoadout Clone()
    {
        return new SpiritArtifactLoadout
        {
            Revision = Revision,
            LoadoutHash = LoadoutHash,
            FlowerArtifactUid = FlowerArtifactUid,
            PlumeArtifactUid = PlumeArtifactUid,
            SandsArtifactUid = SandsArtifactUid,
            GobletArtifactUid = GobletArtifactUid,
            CircletArtifactUid = CircletArtifactUid
        };
    }
}

[Serializable]
public sealed class SpiritArtifactPreset
{
    public string PresetUid { get; set; } = "";

    public string Name { get; set; } = "";

    public int Order { get; set; }

    public int Revision { get; set; }

    public string FlowerArtifactUid { get; set; } = "";

    public string PlumeArtifactUid { get; set; } = "";

    public string SandsArtifactUid { get; set; } = "";

    public string GobletArtifactUid { get; set; } = "";

    public string CircletArtifactUid { get; set; } = "";

    public string CreatedAt { get; set; } = "";

    public string UpdatedAt { get; set; } = "";

    public string Get(string? slotId)
    {
        return SpiritArtifactSlots.Normalize(slotId) switch
        {
            SpiritArtifactSlots.Flower => FlowerArtifactUid ?? "",
            SpiritArtifactSlots.Plume => PlumeArtifactUid ?? "",
            SpiritArtifactSlots.Sands => SandsArtifactUid ?? "",
            SpiritArtifactSlots.Goblet => GobletArtifactUid ?? "",
            SpiritArtifactSlots.Circlet => CircletArtifactUid ?? "",
            _ => ""
        };
    }

    public bool Set(string? slotId, string? artifactUid)
    {
        var value = (artifactUid ?? "").Trim();
        switch (SpiritArtifactSlots.Normalize(slotId))
        {
            case SpiritArtifactSlots.Flower: FlowerArtifactUid = value; return true;
            case SpiritArtifactSlots.Plume: PlumeArtifactUid = value; return true;
            case SpiritArtifactSlots.Sands: SandsArtifactUid = value; return true;
            case SpiritArtifactSlots.Goblet: GobletArtifactUid = value; return true;
            case SpiritArtifactSlots.Circlet: CircletArtifactUid = value; return true;
            default: return false;
        }
    }

    public IReadOnlyList<string> ArtifactUids()
    {
        return SpiritArtifactSlots.All.Select(Get)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public SpiritArtifactPreset Clone()
    {
        return new SpiritArtifactPreset
        {
            PresetUid = PresetUid,
            Name = Name,
            Order = Order,
            Revision = Revision,
            FlowerArtifactUid = FlowerArtifactUid,
            PlumeArtifactUid = PlumeArtifactUid,
            SandsArtifactUid = SandsArtifactUid,
            GobletArtifactUid = GobletArtifactUid,
            CircletArtifactUid = CircletArtifactUid,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}

[Serializable]
public sealed class SpiritArtifactDrawReceipt
{
    public string Token { get; set; } = "";

    public List<string> ArtifactUids { get; set; } = new();

    public string CreatedAt { get; set; } = "";

    public SpiritArtifactDrawReceipt Clone()
    {
        return new SpiritArtifactDrawReceipt
        {
            Token = Token,
            ArtifactUids = new List<string>(ArtifactUids ?? new List<string>()),
            CreatedAt = CreatedAt
        };
    }
}

[Serializable]
public sealed class SpiritArtifactPreparedDraw
{
    public string Token { get; set; } = "";

    public int TruthCost { get; set; }

    public string PoolId { get; set; } = "";

    public string TargetSetId { get; set; } = "";

    public List<SpiritArtifactInstance> Results { get; set; } = new();

    public int ResultingRarityPity { get; set; }

    public int ResultingTargetFate { get; set; }

    public string CreatedAt { get; set; } = "";

    public SpiritArtifactPreparedDraw Clone()
    {
        return new SpiritArtifactPreparedDraw
        {
            Token = Token,
            TruthCost = TruthCost,
            PoolId = PoolId,
            TargetSetId = TargetSetId,
            Results = (Results ?? new List<SpiritArtifactInstance>()).Select(value => value.Clone()).ToList(),
            ResultingRarityPity = ResultingRarityPity,
            ResultingTargetFate = ResultingTargetFate,
            CreatedAt = CreatedAt
        };
    }
}

[Serializable]
public sealed class SpiritArtifactInventory
{
    public int Version { get; set; } = 1;

    public int Essence { get; set; }

    public string SelectedPoolId { get; set; } = "";

    public string TargetSetId { get; set; } = "";

    public int RarityPity { get; set; }

    public int TargetFate { get; set; }

    public List<SpiritArtifactInstance> Artifacts { get; set; } = new();

    public List<SpiritArtifactPreset> Presets { get; set; } = new();

    public SpiritArtifactPreparedDraw? PreparedDraw { get; set; }

    public List<SpiritArtifactDrawReceipt> PendingReveals { get; set; } = new();

    public List<string> ProcessedDrawTokens { get; set; } = new();

    public SpiritArtifactInventory Clone()
    {
        return new SpiritArtifactInventory
        {
            Version = Version,
            Essence = Essence,
            SelectedPoolId = SelectedPoolId,
            TargetSetId = TargetSetId,
            RarityPity = RarityPity,
            TargetFate = TargetFate,
            Artifacts = (Artifacts ?? new List<SpiritArtifactInstance>()).Select(value => value.Clone()).ToList(),
            Presets = (Presets ?? new List<SpiritArtifactPreset>()).Select(value => value.Clone()).ToList(),
            PreparedDraw = PreparedDraw?.Clone(),
            PendingReveals = (PendingReveals ?? new List<SpiritArtifactDrawReceipt>())
                .Select(value => value.Clone())
                .ToList(),
            ProcessedDrawTokens = new List<string>(ProcessedDrawTokens ?? new List<string>())
        };
    }
}

[Serializable]
public sealed class SpiritArtifactActiveEffectSnapshot
{
    public string SetId { get; set; } = "";

    public int RequiredPieces { get; set; }

    public string EffectId { get; set; } = "";

    public string HandlerId { get; set; } = "";

    public int Amount { get; set; }

    public int SecondaryAmount { get; set; }

    public int Maximum { get; set; }

    public SpiritArtifactActiveEffectSnapshot Clone()
    {
        return (SpiritArtifactActiveEffectSnapshot)MemberwiseClone();
    }
}

[Serializable]
public sealed class SpiritArtifactBattleItemSnapshot
{
    public string ArtifactUid { get; set; } = "";

    public string SetId { get; set; } = "";

    public string PieceId { get; set; } = "";

    public string SlotId { get; set; } = "";

    public int Rarity { get; set; }

    public int Level { get; set; }

    public SpiritArtifactStatRoll MainStat { get; set; } = new();

    public List<SpiritArtifactStatRoll> SubStatRolls { get; set; } = new();

    public SpiritArtifactBattleItemSnapshot Clone()
    {
        return new SpiritArtifactBattleItemSnapshot
        {
            ArtifactUid = ArtifactUid,
            SetId = SetId,
            PieceId = PieceId,
            SlotId = SlotId,
            Rarity = Rarity,
            Level = Level,
            MainStat = MainStat?.Clone() ?? new SpiritArtifactStatRoll(),
            SubStatRolls = (SubStatRolls ?? new List<SpiritArtifactStatRoll>())
                .Select(value => value.Clone())
                .ToList()
        };
    }
}

[Serializable]
public sealed class SpiritArtifactBattleSnapshot
{
    public int ProtocolVersion { get; set; } = 1;

    public string RegistryHash { get; set; } = "";

    public int LoadoutRevision { get; set; }

    public string LoadoutHash { get; set; } = "";

    public List<SpiritArtifactBattleItemSnapshot> Items { get; set; } = new();

    public int OriginMagic { get; set; }

    public int OriginSpirit { get; set; }

    public int OriginLuck { get; set; }

    public int OriginPerception { get; set; }

    public int FlatLife { get; set; }

    public int FlatArmor { get; set; }

    public int MaxMagic { get; set; }

    public int Speed { get; set; }

    public int StartExtraordinary { get; set; }

    public List<SpiritArtifactActiveEffectSnapshot> ActiveEffects { get; set; } = new();

    public SpiritArtifactBattleSnapshot Clone()
    {
        return new SpiritArtifactBattleSnapshot
        {
            ProtocolVersion = ProtocolVersion,
            RegistryHash = RegistryHash,
            LoadoutRevision = LoadoutRevision,
            LoadoutHash = LoadoutHash,
            Items = (Items ?? new List<SpiritArtifactBattleItemSnapshot>()).Select(value => value.Clone()).ToList(),
            OriginMagic = OriginMagic,
            OriginSpirit = OriginSpirit,
            OriginLuck = OriginLuck,
            OriginPerception = OriginPerception,
            FlatLife = FlatLife,
            FlatArmor = FlatArmor,
            MaxMagic = MaxMagic,
            Speed = Speed,
            StartExtraordinary = StartExtraordinary,
            ActiveEffects = (ActiveEffects ?? new List<SpiritArtifactActiveEffectSnapshot>())
                .Select(value => value.Clone())
                .ToList()
        };
    }
}

public sealed class SpiritArtifactOperationResult
{
    public bool Success { get; set; }

    public string Reason { get; set; } = "";

    public string Token { get; set; } = "";

    public int EssenceDelta { get; set; }

    public SpiritArtifactInstance? Artifact { get; set; }

    public List<SpiritArtifactInstance> Artifacts { get; set; } = new();

    public SpiritArtifactPreset? Preset { get; set; }

    public List<string> AffectedSpiritUids { get; set; } = new();

    public int TransferredArtifactCount { get; set; }
}
