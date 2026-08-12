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
public sealed class SpiritGrowthRegistryDefaults
{
    public int MaxLevel { get; set; } = 50;

    public string LevelCurveId { get; set; } = "level-linear-1-50";

    public string AptitudeRollProfileId { get; set; } = "aptitude-roll-normal-60-15";

    public string AptitudeCurveId { get; set; } = "aptitude-smoothstep-080-120";

    public string ExperienceCurveId { get; set; } = "xp-standard-1-50";

    public string BattleConversionId { get; set; } = "origins-battle-standard-v1";

    public string RadarScaleId { get; set; } = "origins-global-v1";
}

[Serializable]
public sealed class SpiritLevelCurveDefinition
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "normalizedLinear";

    public int MinLevel { get; set; } = 1;

    public int MaxLevel { get; set; } = 50;
}

[Serializable]
public sealed class SpiritAptitudeRollProfile
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "truncatedNormal";

    public double Mean { get; set; } = 60d;

    public double StandardDeviation { get; set; } = 15d;

    public int Minimum { get; set; }

    public int Maximum { get; set; } = 100;

    public int Fallback { get; set; } = 60;

    public int MaximumAttempts { get; set; } = 64;
}

[Serializable]
public sealed class SpiritAptitudeCurveDefinition
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "smoothstep";

    public int InputMin { get; set; }

    public int InputMax { get; set; } = 100;

    public double OutputMin { get; set; } = 0.8d;

    public double OutputMax { get; set; } = 1.2d;
}

[Serializable]
public sealed class SpiritExperienceCurveDefinition
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "quadraticStep";

    public int Base { get; set; } = 20;

    public int Linear { get; set; } = 2;

    public int QuadraticDivisor { get; set; } = 24;
}

[Serializable]
public sealed class SpiritBattleConversionDefinition
{
    public string Id { get; set; } = "";

    public double HpBase { get; set; } = 20d;

    public double HpSpirit { get; set; } = 2.4d;

    public double HpLuck { get; set; } = 0.8d;

    public double AttackBase { get; set; } = 3d;

    public double AttackMagic { get; set; } = 0.8d;

    public double AttackPerception { get; set; } = 0.25d;

    public double AttackLuck { get; set; } = 0.15d;

    public double ArmorBase { get; set; } = 1d;

    public double ArmorPerception { get; set; } = 0.55d;

    public double ArmorSpirit { get; set; } = 0.2d;

    public double ArmorLuck { get; set; } = 0.1d;

    public double IntentEnergyBase { get; set; } = 3d;

    public double IntentEnergyMagic { get; set; } = 0.15d;

    public double IntentEnergyPerception { get; set; } = 0.1d;
}

[Serializable]
public sealed class SpiritRadarAxisDefinition
{
    public string Key { get; set; } = "";

    public int Cap { get; set; } = 80;
}

[Serializable]
public sealed class SpiritRadarScaleSet
{
    public string Id { get; set; } = "";

    public string Mode { get; set; } = "absoluteCaps";

    public List<SpiritRadarAxisDefinition> Axes { get; set; } = new();
}

[Serializable]
public sealed class SpiritSpeciesGrowthMatch
{
    public string SourceModId { get; set; } = "*";

    public string EnemyId { get; set; } = "*";

    public string VariantId { get; set; } = "*";
}

[Serializable]
public sealed class SpiritSpeciesGrowthProfile
{
    public string SpeciesId { get; set; } = "";

    public string ProfileId { get; set; } = "";

    public string FormKey { get; set; } = "default";

    public int FormOrder { get; set; }

    public string FormLabelKey { get; set; } = "form.default";

    public SpiritSpeciesGrowthMatch Match { get; set; } = new();

    // Schema 1 compatibility fields. Schema 2 files use Match instead.
    public string EnemyId { get; set; } = "";

    public string VariantId { get; set; } = "";

    public string Tier { get; set; } = nameof(SpiritSpeciesTier.Normal);

    public SpiritOriginVector BaseOrigins { get; set; } = new();

    public SpiritOriginVector GrowthOrigins { get; set; } = new();

    public string LevelCurveId { get; set; } = "";

    public string AptitudeRollProfileId { get; set; } = "";

    public string AptitudeCurveId { get; set; } = "";

    public string ExperienceCurveId { get; set; } = "";

    public string BattleConversionId { get; set; } = "";

    public string RadarScaleId { get; set; } = "";

    public SpiritSpeciesGrowthProfile Clone()
    {
        return new SpiritSpeciesGrowthProfile
        {
            SpeciesId = SpeciesId,
            ProfileId = ProfileId,
            FormKey = FormKey,
            FormOrder = FormOrder,
            FormLabelKey = FormLabelKey,
            Match = new SpiritSpeciesGrowthMatch
            {
                SourceModId = Match?.SourceModId ?? "*",
                EnemyId = Match?.EnemyId ?? "*",
                VariantId = Match?.VariantId ?? "*"
            },
            EnemyId = EnemyId,
            VariantId = VariantId,
            Tier = Tier,
            BaseOrigins = BaseOrigins?.Clone() ?? new SpiritOriginVector(),
            GrowthOrigins = GrowthOrigins?.Clone() ?? new SpiritOriginVector(),
            LevelCurveId = LevelCurveId,
            AptitudeRollProfileId = AptitudeRollProfileId,
            AptitudeCurveId = AptitudeCurveId,
            ExperienceCurveId = ExperienceCurveId,
            BattleConversionId = BattleConversionId,
            RadarScaleId = RadarScaleId
        };
    }
}

[Serializable]
public sealed class SpiritGrowthRegistryDocument
{
    public int SchemaVersion { get; set; } = 2;

    public SpiritGrowthRegistryDefaults Defaults { get; set; } = new();

    public Dictionary<string, string> FormLabels { get; set; } = new(StringComparer.Ordinal);

    public List<SpiritLevelCurveDefinition> LevelCurves { get; set; } = new();

    public List<SpiritAptitudeRollProfile> AptitudeRollProfiles { get; set; } = new();

    public List<SpiritAptitudeCurveDefinition> AptitudeCurves { get; set; } = new();

    public List<SpiritExperienceCurveDefinition> ExperienceCurves { get; set; } = new();

    public List<SpiritBattleConversionDefinition> BattleConversions { get; set; } = new();

    public List<SpiritRadarScaleSet> RadarScaleSets { get; set; } = new();

    public List<SpiritSpeciesGrowthProfile> Profiles { get; set; } = new();
}

public sealed class SpiritProfileIdentity
{
    public string SpeciesId { get; set; } = "";

    public string ProfileId { get; set; } = "";

    public bool UsedFallback { get; set; }

    public SpiritSpeciesGrowthProfile Profile { get; set; } = new();
}

[Serializable]
public sealed class SpiritInstance
{
    public string SpiritUid { get; set; } = "";

    public string SpeciesId { get; set; } = "";

    public string ProfileId { get; set; } = "";

    public CapturedEnemySnapshot Snapshot { get; set; } = new();

    public int Level { get; set; } = 1;

    public int Experience { get; set; }

    public int Aptitude { get; set; } = 60;

    public int Speed { get; set; }

    public int GuiyuanValue { get; set; }

    public SpiritOriginVector GuiyuanAllocations { get; set; } = new();

    public int TrainingPlanVersion { get; set; }

    public List<string> LearnedIntentIds { get; set; } = new();

    public List<string> EquippedIntentIds { get; set; } = new();

    public List<string> LearnedPassiveIds { get; set; } = new();

    public string EquippedPassiveId { get; set; } = "";

    public List<SpiritUnlockNode> UnlockPlan { get; set; } = new();

    public List<string> NewAbilityIds { get; set; } = new();

    public int LoadoutRevision { get; set; }

    public string LoadoutHash { get; set; } = "";

    public bool Favorite { get; set; }

    public bool Locked { get; set; }

    public string CapturedAt { get; set; } = "";

    public SpiritInstance Clone()
    {
        return new SpiritInstance
        {
            SpiritUid = SpiritUid,
            SpeciesId = SpeciesId,
            ProfileId = ProfileId,
            Snapshot = SpiritModelCloner.CloneSnapshot(Snapshot),
            Level = Level,
            Experience = Experience,
            Aptitude = Aptitude,
            Speed = Speed,
            GuiyuanValue = GuiyuanValue,
            GuiyuanAllocations = GuiyuanAllocations?.Clone() ?? new SpiritOriginVector(),
            TrainingPlanVersion = TrainingPlanVersion,
            LearnedIntentIds = new List<string>(LearnedIntentIds ?? new List<string>()),
            EquippedIntentIds = new List<string>(EquippedIntentIds ?? new List<string>()),
            LearnedPassiveIds = new List<string>(LearnedPassiveIds ?? new List<string>()),
            EquippedPassiveId = EquippedPassiveId,
            UnlockPlan = (UnlockPlan ?? new List<SpiritUnlockNode>()).Select(value => value.Clone()).ToList(),
            NewAbilityIds = new List<string>(NewAbilityIds ?? new List<string>()),
            LoadoutRevision = LoadoutRevision,
            LoadoutHash = LoadoutHash,
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

    public bool Remove(string uid)
    {
        PartySlots ??= new List<string>();
        var active = ActiveSpiritUid;
        var changed = SpiritAdventurePartyRules.Remove(PartySlots, ref active, uid);
        ActiveSpiritUid = active;
        return changed;
    }

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

    public List<string> UnlockedAbilityIds { get; set; } = new();

    public bool LeveledUp => Instance.Level > OldLevel;
}

public interface ISpiritCollectionStore
{
    SpiritCollectionDocument Load();

    void Save(SpiritCollectionDocument document);
}

public sealed class SpiritRadarAxisSnapshot
{
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    public int BaseValue { get; set; }

    public int GrowthBudget { get; set; }

    public int RawCurrent { get; set; }

    public int RawPotential { get; set; }

    public int Cap { get; set; }

    public float NormalizedCurrent { get; set; }

    public float NormalizedPotential { get; set; }
}

public sealed class SpiritGrowthCurvePoint
{
    public int Level { get; set; }

    public int TotalExperience { get; set; }

    public SpiritOriginVector Origins { get; set; } = new();
}

public sealed class SpiritGrowthViewSnapshot
{
    public string SpiritUid { get; set; } = "";

    public string SpeciesId { get; set; } = "";

    public string ProfileId { get; set; } = "";

    public string FormKey { get; set; } = "default";

    public string FormLabel { get; set; } = "";

    public SpiritSpeciesTier Tier { get; set; }

    public int Level { get; set; }

    public int MaxLevel { get; set; }

    public int Experience { get; set; }

    public int ExperienceToNextLevel { get; set; }

    public int Aptitude { get; set; }

    public SpiritOriginVector BaseOrigins { get; set; } = new();

    public SpiritOriginVector GrowthOrigins { get; set; } = new();

    public SpiritOriginVector CurrentOrigins { get; set; } = new();

    public SpiritOriginVector MaxLevelOriginsAtCurrentAptitude { get; set; } = new();

    public SpiritOriginVector StandardOriginsAtLevel50Aptitude60 { get; set; } = new();

    public CompanionStats BattleStats { get; set; } = new(1, 1, 1, 1, 100);

    public string RadarScaleId { get; set; } = "";

    public List<SpiritRadarAxisSnapshot> RadarAxes { get; set; } = new();

    public List<SpiritGrowthCurvePoint> CurrentAptitudeCurve { get; set; } = new();

    public List<SpiritGrowthCurvePoint> StandardAptitudeCurve { get; set; } = new();

    public List<SpiritGrowthCurvePoint> TheoreticalAptitudeCurve { get; set; } = new();
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
            SpeciesId = source.SpeciesId,
            ProfileId = source.ProfileId,
            SpiritLevel = source.SpiritLevel,
            SpiritAptitude = source.SpiritAptitude,
            SpiritGuiyuanValue = source.SpiritGuiyuanValue,
            SpiritStarRank = source.SpiritStarRank,
            GuiyuanAllocationMagic = source.GuiyuanAllocationMagic,
            GuiyuanAllocationSpirit = source.GuiyuanAllocationSpirit,
            GuiyuanAllocationLuck = source.GuiyuanAllocationLuck,
            GuiyuanAllocationPerception = source.GuiyuanAllocationPerception,
            OriginMagic = source.OriginMagic,
            OriginSpirit = source.OriginSpirit,
            OriginLuck = source.OriginLuck,
            OriginPerception = source.OriginPerception,
            SpiritSpeed = source.SpiritSpeed,
            EquippedIntentIds = new List<string>(source.EquippedIntentIds ?? new List<string>()),
            EquippedPassiveId = source.EquippedPassiveId,
            LoadoutRevision = source.LoadoutRevision,
            LoadoutHash = source.LoadoutHash,
            TrainingRegistryHash = source.TrainingRegistryHash,
            DeploymentToken = source.DeploymentToken
        };
    }
}
