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

    public string CaptureElement { get; set; } = "";

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
            CaptureElement = CaptureElement,
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
    public int SchemaVersion { get; set; } = SpiritSystemContract.GrowthRegistrySchemaVersion;

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
    public SpiritIdentityComponent Identity { get; set; } = new();
    public SpiritSourceComponent Source { get; set; } = new();
    public SpiritGrowthComponent Growth { get; set; } = new();
    public SpiritElementComponent Element { get; set; } = new();
    public SpiritAscensionComponent Ascension { get; set; } = new();
    public SpiritTrainingComponent Training { get; set; } = new();
    public SpiritEquipmentComponent Equipment { get; set; } = new();
    public SpiritMetadataComponent Metadata { get; set; } = new();

    [Newtonsoft.Json.JsonIgnore] public string SpiritUid { get => (Identity ??= new()).SpiritUid; set => (Identity ??= new()).SpiritUid = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public string SpeciesId { get => (Identity ??= new()).SpeciesId; set => (Identity ??= new()).SpeciesId = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public string ProfileId { get => (Identity ??= new()).ProfileId; set => (Identity ??= new()).ProfileId = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public string ElementId { get => (Element ??= new()).ElementId; set => (Element ??= new()).ElementId = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public string ElementSource { get => (Element ??= new()).Source; set => (Element ??= new()).Source = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public int ElementAssignmentRevision { get => (Element ??= new()).AssignmentRevision; set => (Element ??= new()).AssignmentRevision = value; }
    [Newtonsoft.Json.JsonIgnore] public CapturedEnemySnapshot Snapshot { get => (Source ??= new()).Capture ??= new CapturedEnemySnapshot(); set => (Source ??= new()).Capture = value ?? new CapturedEnemySnapshot(); }
    [Newtonsoft.Json.JsonIgnore] public SpiritLocalizedPresentation Presentation { get => (Source ??= new()).Presentation ??= new SpiritLocalizedPresentation(); set => (Source ??= new()).Presentation = value ?? new SpiritLocalizedPresentation(); }
    [Newtonsoft.Json.JsonIgnore] public int Level { get => (Growth ??= new()).Level; set => (Growth ??= new()).Level = value; }
    [Newtonsoft.Json.JsonIgnore] public int Experience { get => (Growth ??= new()).Experience; set => (Growth ??= new()).Experience = value; }
    [Newtonsoft.Json.JsonIgnore] public int Aptitude { get => (Growth ??= new()).Aptitude; set => (Growth ??= new()).Aptitude = value; }
    [Newtonsoft.Json.JsonIgnore] public int Speed { get => (Growth ??= new()).Speed; set => (Growth ??= new()).Speed = value; }
    [Newtonsoft.Json.JsonIgnore] public int GuiyuanValue { get => (Ascension ??= new()).GuiyuanValue; set => (Ascension ??= new()).GuiyuanValue = value; }
    [Newtonsoft.Json.JsonIgnore] public SpiritOriginVector GuiyuanAllocations { get => (Ascension ??= new()).Allocations ??= new SpiritOriginVector(); set => (Ascension ??= new()).Allocations = value ?? new SpiritOriginVector(); }
    [Newtonsoft.Json.JsonIgnore] public int TrainingPlanVersion { get => (Training ??= new()).TrainingPlanVersion; set => (Training ??= new()).TrainingPlanVersion = value; }
    [Newtonsoft.Json.JsonIgnore] public int InherentAbilityPlanVersion { get => (Training ??= new()).InherentAbilityPlanVersion; set => (Training ??= new()).InherentAbilityPlanVersion = value; }
    [Newtonsoft.Json.JsonIgnore] public List<string> ResolvedInherentIntentIds { get => (Training ??= new()).ResolvedInherentIntentIds ??= new List<string>(); set => (Training ??= new()).ResolvedInherentIntentIds = value ?? new List<string>(); }
    [Newtonsoft.Json.JsonIgnore] public string ResolvedInherentPassiveId { get => (Training ??= new()).ResolvedInherentPassiveId; set => (Training ??= new()).ResolvedInherentPassiveId = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public List<string> LearnedIntentIds { get => (Training ??= new()).LearnedIntentIds ??= new List<string>(); set => (Training ??= new()).LearnedIntentIds = value ?? new List<string>(); }
    [Newtonsoft.Json.JsonIgnore] public List<string> EquippedIntentIds { get => (Training ??= new()).EquippedIntentIds ??= new List<string>(); set => (Training ??= new()).EquippedIntentIds = value ?? new List<string>(); }
    [Newtonsoft.Json.JsonIgnore] public List<string> LearnedPassiveIds { get => (Training ??= new()).LearnedPassiveIds ??= new List<string>(); set => (Training ??= new()).LearnedPassiveIds = value ?? new List<string>(); }
    [Newtonsoft.Json.JsonIgnore] public string EquippedPassiveId { get => (Training ??= new()).EquippedPassiveId; set => (Training ??= new()).EquippedPassiveId = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public List<SpiritUnlockNode> UnlockPlan { get => (Training ??= new()).UnlockPlan ??= new List<SpiritUnlockNode>(); set => (Training ??= new()).UnlockPlan = value ?? new List<SpiritUnlockNode>(); }
    [Newtonsoft.Json.JsonIgnore] public List<string> NewAbilityIds { get => (Training ??= new()).NewAbilityIds ??= new List<string>(); set => (Training ??= new()).NewAbilityIds = value ?? new List<string>(); }
    [Newtonsoft.Json.JsonIgnore] public int LoadoutRevision { get => (Training ??= new()).LoadoutRevision; set => (Training ??= new()).LoadoutRevision = value; }
    [Newtonsoft.Json.JsonIgnore] public string LoadoutHash { get => (Training ??= new()).LoadoutHash; set => (Training ??= new()).LoadoutHash = value ?? ""; }
    [Newtonsoft.Json.JsonIgnore] public SpiritArtifactLoadout ArtifactLoadout { get => (Equipment ??= new()).ArtifactLoadout ??= new SpiritArtifactLoadout(); set => (Equipment ??= new()).ArtifactLoadout = value ?? new SpiritArtifactLoadout(); }
    [Newtonsoft.Json.JsonIgnore] public bool Favorite { get => (Metadata ??= new()).Favorite; set => (Metadata ??= new()).Favorite = value; }
    [Newtonsoft.Json.JsonIgnore] public bool Locked { get => (Metadata ??= new()).Locked; set => (Metadata ??= new()).Locked = value; }
    [Newtonsoft.Json.JsonIgnore] public string CapturedAt { get => (Source ??= new()).CapturedAt; set => (Source ??= new()).CapturedAt = value ?? ""; }

    public SpiritInstance Clone()
    {
        return new SpiritInstance
        {
            Identity = (Identity ?? new SpiritIdentityComponent()).Clone(),
            Source = (Source ?? new SpiritSourceComponent()).Clone(),
            Growth = (Growth ?? new SpiritGrowthComponent()).Clone(),
            Element = (Element ?? new SpiritElementComponent()).Clone(),
            Ascension = (Ascension ?? new SpiritAscensionComponent()).Clone(),
            Training = (Training ?? new SpiritTrainingComponent()).Clone(),
            Equipment = (Equipment ?? new SpiritEquipmentComponent()).Clone(),
            Metadata = (Metadata ?? new SpiritMetadataComponent()).Clone()
        };
    }
}

[Serializable]
public sealed class SpiritCollectionDocument
{
    public int Version { get; set; } = SpiritCollectionService.CurrentVersion;

    public long Revision { get; set; }

    public int LegacyCardMigrationVersion { get; set; }

    public int InitialRosterGrantVersion { get; set; }

    public List<SpiritInstance> Instances { get; set; } = new();

    public List<string> DefaultPartySlots { get; set; } = new();

    public string DefaultActiveSpiritUid { get; set; } = "";

    public Dictionary<string, string> ProcessedCaptureTokens { get; set; } = new(StringComparer.Ordinal);

    public List<string> ProcessedBattleTokens { get; set; } = new();

    public SpiritArtifactInventory ArtifactInventory { get; set; } = new();
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

public sealed class SpiritInitialRosterSeed
{
    public string ProfileId { get; set; } = "";

    public CapturedEnemySnapshot Snapshot { get; set; } = new();
}

public sealed class SpiritInitialRosterGrantResult
{
    public bool Success { get; set; }

    public bool AlreadyGranted { get; set; }

    public int GrantedCount { get; set; }

    public string Reason { get; set; } = "";
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

public interface ISpiritInitialRosterGrantGuard
{
    bool CanGrantInitialRoster { get; }

    string InitialRosterGrantBlockReason { get; }
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

    public string ElementId { get; set; } = "";

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
            SourceEnemyCardIds = (source.SourceEnemyCardIds ?? new List<string>()).ToList()
        };
    }
}
