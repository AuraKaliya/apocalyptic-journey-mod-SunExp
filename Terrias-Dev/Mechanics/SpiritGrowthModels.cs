using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public enum SpiritSpeciesTier
{
    Normal = 1,
    Elite = 2,
    Boss = 3,
    FinalBoss = 4
}

[Serializable]
public sealed class SpiritOriginVector
{
    public int Magic { get; set; }

    public int Spirit { get; set; }

    public int Luck { get; set; }

    public int Perception { get; set; }

    public int Total => Magic + Spirit + Luck + Perception;

    public SpiritOriginVector Clone()
    {
        return new SpiritOriginVector
        {
            Magic = Magic,
            Spirit = Spirit,
            Luck = Luck,
            Perception = Perception
        };
    }
}

[Serializable]
public sealed class SpiritSpeciesGrowthProfile
{
    public string EnemyId { get; set; } = "*";

    public string VariantId { get; set; } = "*";

    public string Tier { get; set; } = nameof(SpiritSpeciesTier.Normal);

    public SpiritOriginVector BaseOrigins { get; set; } = new();

    public SpiritOriginVector GrowthOrigins { get; set; } = new();
}

[Serializable]
public sealed class SpiritGrowthRegistryDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<SpiritSpeciesGrowthProfile> Profiles { get; set; } = new();
}

[Serializable]
public sealed class SpiritInstance
{
    public string SpiritUid { get; set; } = "";

    public CapturedEnemySnapshot Snapshot { get; set; } = new();

    public int Level { get; set; } = 1;

    public int Experience { get; set; }

    public int Aptitude { get; set; } = 60;

    public bool Favorite { get; set; }

    public bool Locked { get; set; }

    public string CapturedAt { get; set; } = "";

    public SpiritInstance Clone()
    {
        return new SpiritInstance
        {
            SpiritUid = SpiritUid,
            Snapshot = SpiritModelCloner.CloneSnapshot(Snapshot),
            Level = Level,
            Experience = Experience,
            Aptitude = Aptitude,
            Favorite = Favorite,
            Locked = Locked,
            CapturedAt = CapturedAt
        };
    }
}

[Serializable]
public sealed class SpiritCollectionDocument
{
    public int Version { get; set; } = SpiritCollectionService.CurrentVersion;

    public int LegacyCardMigrationVersion { get; set; }

    public List<SpiritInstance> Instances { get; set; } = new();

    public List<string> DefaultPartySlots { get; set; } = new();

    public string DefaultActiveSpiritUid { get; set; } = "";

    public Dictionary<string, string> ProcessedCaptureTokens { get; set; } = new(StringComparer.Ordinal);

    public List<string> ProcessedBattleTokens { get; set; } = new();
}

[Serializable]
public sealed class SpiritAdventureParty
{
    public int Version { get; set; } = 1;

    public List<string> PartySlots { get; set; } = new();

    public string ActiveSpiritUid { get; set; } = "";

    public SpiritAdventureParty Clone()
    {
        return new SpiritAdventureParty
        {
            Version = Version,
            PartySlots = new List<string>(PartySlots ?? new List<string>()),
            ActiveSpiritUid = ActiveSpiritUid
        };
    }
}

public sealed class SpiritCaptureRecordResult
{
    public bool Success { get; set; }

    public bool DuplicateOperation { get; set; }

    public bool AddedToParty { get; set; }

    public string Reason { get; set; } = "";

    public SpiritInstance? Instance { get; set; }
}

public sealed class SpiritExperienceResult
{
    public SpiritInstance Instance { get; set; } = new();

    public int OldLevel { get; set; }

    public int OldExperience { get; set; }

    public int GainedExperience { get; set; }

    public bool LeveledUp => Instance.Level > OldLevel;
}

public interface ISpiritCollectionStore
{
    SpiritCollectionDocument Load();

    void Save(SpiritCollectionDocument document);
}

public static class SpiritModelCloner
{
    public static CapturedEnemySnapshot CloneSnapshot(CapturedEnemySnapshot? source)
    {
        source ??= new CapturedEnemySnapshot();
        return new CapturedEnemySnapshot
        {
            SpiritUid = source.SpiritUid,
            SourceModId = source.SourceModId,
            EnemyId = source.EnemyId,
            VariantId = source.VariantId,
            InstanceId = source.InstanceId,
            DisplayName = source.DisplayName,
            Description = source.Description,
            AnimationPath = source.AnimationPath,
            DictPath = source.DictPath,
            IdlePath = source.IdlePath,
            CaptureOrigin = source.CaptureOrigin,
            CapturedAt = source.CapturedAt,
            BaseHp = source.BaseHp,
            BaseAttack = source.BaseAttack,
            BaseArmor = source.BaseArmor,
            Rarity = source.Rarity,
            SourceEnemyCardIds = (source.SourceEnemyCardIds ?? new List<string>()).ToList(),
            SpiritLevel = source.SpiritLevel,
            SpiritAptitude = source.SpiritAptitude,
            OriginMagic = source.OriginMagic,
            OriginSpirit = source.OriginSpirit,
            OriginLuck = source.OriginLuck,
            OriginPerception = source.OriginPerception,
            DeploymentToken = source.DeploymentToken
        };
    }
}
