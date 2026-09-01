using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class SpiritComponentVersions
{
    public const int Identity = 1;
    public const int Source = 1;
    public const int Growth = 1;
    public const int Element = 1;
    public const int Ascension = 1;
    public const int Training = 1;
    public const int Equipment = 1;
    public const int Metadata = 1;
}

[Serializable]
public sealed class SpiritIdentityComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Identity;
    public string SpiritUid { get; set; } = "";
    public string SpeciesId { get; set; } = "";
    public string ProfileId { get; set; } = "";

    public SpiritIdentityComponent Clone() => new()
    {
        Version = Version,
        SpiritUid = SpiritUid,
        SpeciesId = SpeciesId,
        ProfileId = ProfileId
    };
}

[Serializable]
public sealed class SpiritSourceComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Source;
    public CapturedEnemySnapshot Capture { get; set; } = new();
    public SpiritLocalizedPresentation Presentation { get; set; } = new();
    public string CapturedAt { get; set; } = "";

    public SpiritSourceComponent Clone() => new()
    {
        Version = Version,
        Capture = SpiritModelCloner.CloneSnapshot(Capture),
        Presentation = (Presentation ?? new SpiritLocalizedPresentation()).Clone(),
        CapturedAt = CapturedAt
    };
}

[Serializable]
public sealed class SpiritGrowthComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Growth;
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Aptitude { get; set; } = 60;
    public int Speed { get; set; }

    public SpiritGrowthComponent Clone() => new()
    {
        Version = Version,
        Level = Level,
        Experience = Experience,
        Aptitude = Aptitude,
        Speed = Speed
    };
}

[Serializable]
public sealed class SpiritElementComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Element;
    public string ElementId { get; set; } = "";
    public string Source { get; set; } = "";
    public int AssignmentRevision { get; set; }

    public SpiritElementComponent Clone() => new()
    {
        Version = Version,
        ElementId = ElementId,
        Source = Source,
        AssignmentRevision = AssignmentRevision
    };
}

[Serializable]
public sealed class SpiritAscensionComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Ascension;
    public int GuiyuanValue { get; set; }
    public SpiritOriginVector Allocations { get; set; } = new();

    public SpiritAscensionComponent Clone() => new()
    {
        Version = Version,
        GuiyuanValue = GuiyuanValue,
        Allocations = Allocations?.Clone() ?? new SpiritOriginVector()
    };
}

[Serializable]
public sealed class SpiritTrainingComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Training;
    public int TrainingPlanVersion { get; set; }
    public int InherentAbilityPlanVersion { get; set; }
    public List<string> ResolvedInherentIntentIds { get; set; } = new();
    public string ResolvedInherentPassiveId { get; set; } = "";
    public List<string> LearnedIntentIds { get; set; } = new();
    public List<string> EquippedIntentIds { get; set; } = new();
    public List<string> LearnedPassiveIds { get; set; } = new();
    public string EquippedPassiveId { get; set; } = "";
    public List<SpiritUnlockNode> UnlockPlan { get; set; } = new();
    public List<string> NewAbilityIds { get; set; } = new();
    public int LoadoutRevision { get; set; }
    public string LoadoutHash { get; set; } = "";

    public SpiritTrainingComponent Clone() => new()
    {
        Version = Version,
        TrainingPlanVersion = TrainingPlanVersion,
        InherentAbilityPlanVersion = InherentAbilityPlanVersion,
        ResolvedInherentIntentIds = new List<string>(ResolvedInherentIntentIds ?? new List<string>()),
        ResolvedInherentPassiveId = ResolvedInherentPassiveId,
        LearnedIntentIds = new List<string>(LearnedIntentIds ?? new List<string>()),
        EquippedIntentIds = new List<string>(EquippedIntentIds ?? new List<string>()),
        LearnedPassiveIds = new List<string>(LearnedPassiveIds ?? new List<string>()),
        EquippedPassiveId = EquippedPassiveId,
        UnlockPlan = (UnlockPlan ?? new List<SpiritUnlockNode>()).ConvertAll(value => value.Clone()),
        NewAbilityIds = new List<string>(NewAbilityIds ?? new List<string>()),
        LoadoutRevision = LoadoutRevision,
        LoadoutHash = LoadoutHash
    };
}

[Serializable]
public sealed class SpiritEquipmentComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Equipment;
    public SpiritArtifactLoadout ArtifactLoadout { get; set; } = new();

    public SpiritEquipmentComponent Clone() => new()
    {
        Version = Version,
        ArtifactLoadout = ArtifactLoadout?.Clone() ?? new SpiritArtifactLoadout()
    };
}

[Serializable]
public sealed class SpiritMetadataComponent
{
    public int Version { get; set; } = SpiritComponentVersions.Metadata;
    public bool Favorite { get; set; }
    public bool Locked { get; set; }

    public SpiritMetadataComponent Clone() => new()
    {
        Version = Version,
        Favorite = Favorite,
        Locked = Locked
    };
}

public static class SpiritComponentNormalizer
{
    public static void Normalize(SpiritInstance instance)
    {
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        instance.Identity ??= new SpiritIdentityComponent();
        instance.Source ??= new SpiritSourceComponent();
        instance.Growth ??= new SpiritGrowthComponent();
        instance.Element ??= new SpiritElementComponent();
        instance.Ascension ??= new SpiritAscensionComponent();
        instance.Training ??= new SpiritTrainingComponent();
        instance.Equipment ??= new SpiritEquipmentComponent();
        instance.Metadata ??= new SpiritMetadataComponent();

        ValidateVersion("identity", instance.Identity.Version, SpiritComponentVersions.Identity);
        ValidateVersion("source", instance.Source.Version, SpiritComponentVersions.Source);
        ValidateVersion("growth", instance.Growth.Version, SpiritComponentVersions.Growth);
        ValidateVersion("element", instance.Element.Version, SpiritComponentVersions.Element);
        ValidateVersion("ascension", instance.Ascension.Version, SpiritComponentVersions.Ascension);
        ValidateVersion("training", instance.Training.Version, SpiritComponentVersions.Training);
        ValidateVersion("equipment", instance.Equipment.Version, SpiritComponentVersions.Equipment);
        ValidateVersion("metadata", instance.Metadata.Version, SpiritComponentVersions.Metadata);

        instance.Identity.Version = SpiritComponentVersions.Identity;
        instance.Source.Version = SpiritComponentVersions.Source;
        instance.Growth.Version = SpiritComponentVersions.Growth;
        instance.Element.Version = SpiritComponentVersions.Element;
        instance.Ascension.Version = SpiritComponentVersions.Ascension;
        instance.Training.Version = SpiritComponentVersions.Training;
        instance.Equipment.Version = SpiritComponentVersions.Equipment;
        instance.Metadata.Version = SpiritComponentVersions.Metadata;

        instance.Source.Capture ??= new CapturedEnemySnapshot();
        instance.Source.Presentation ??= new SpiritLocalizedPresentation();
        instance.Ascension.Allocations ??= new SpiritOriginVector();
        instance.Training.ResolvedInherentIntentIds ??= new List<string>();
        instance.Training.LearnedIntentIds ??= new List<string>();
        instance.Training.EquippedIntentIds ??= new List<string>();
        instance.Training.LearnedPassiveIds ??= new List<string>();
        instance.Training.UnlockPlan ??= new List<SpiritUnlockNode>();
        instance.Training.NewAbilityIds ??= new List<string>();
        instance.Equipment.ArtifactLoadout ??= new SpiritArtifactLoadout();
    }

    private static void ValidateVersion(string component, int value, int supported)
    {
        if (value > supported)
            throw new InvalidOperationException("Unsupported Spirit " + component
                                                + " component version " + value
                                                + "; supported=" + supported + ".");
    }
}
