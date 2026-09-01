using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritDeploymentFeatureContext
{
    public SpiritDeploymentFeatureContext(
        SpiritCollectionDocument collection,
        SpiritInstance instance,
        SpiritDeploymentSnapshot snapshot)
    {
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public SpiritCollectionDocument Collection { get; }
    public SpiritInstance Instance { get; }
    public SpiritDeploymentSnapshot Snapshot { get; }
}

public interface ISpiritDeploymentFeature
{
    string Id { get; }
    void Project(SpiritDeploymentFeatureContext context);
    bool Validate(SpiritDeploymentSnapshot snapshot, out string reason);
}

public static class SpiritDeploymentFeatureRegistry
{
    private static readonly IReadOnlyList<ISpiritDeploymentFeature> Features = new ISpiritDeploymentFeature[]
    {
        new CoreFeature(),
        new ElementFeature(),
        new AscensionFeature(),
        new TrainingFeature(),
        new ArtifactFeature()
    };

    public static IReadOnlyList<string> FeatureIds()
    {
        var result = new string[Features.Count];
        for (var index = 0; index < Features.Count; index++) result[index] = Features[index].Id;
        return result;
    }

    public static SpiritDeploymentSnapshot Project(
        SpiritCollectionDocument collection,
        SpiritInstance instance,
        string deploymentToken)
    {
        var snapshot = new SpiritDeploymentSnapshot { DeploymentToken = deploymentToken ?? "" };
        var context = new SpiritDeploymentFeatureContext(collection, instance, snapshot);
        foreach (var feature in Features) feature.Project(context);
        return SpiritDeploymentCodec.Seal(snapshot);
    }

    public static bool Validate(SpiritDeploymentSnapshot snapshot, out string reason)
    {
        if (!SpiritDeploymentCodec.ValidateIntegrity(snapshot, out reason)) return false;
        foreach (var feature in Features)
            if (!feature.Validate(snapshot, out reason)) return false;
        reason = "";
        return true;
    }

    private sealed class CoreFeature : ISpiritDeploymentFeature
    {
        public string Id => "core";

        public void Project(SpiritDeploymentFeatureContext context)
        {
            var instance = context.Instance;
            var snapshot = context.Snapshot;
            snapshot.IntentRegistryHash = SpiritIntentRegistry.RegistryHash;
            snapshot.GrowthRegistryHash = SpiritGrowthRegistry.RegistryHash;
            snapshot.Identity = new SpiritDeploymentIdentity
            {
                SpiritUid = instance.SpiritUid,
                SpeciesId = instance.SpeciesId,
                ProfileId = instance.ProfileId
            };
            snapshot.Source = SpiritModelCloner.CloneSnapshot(instance.Snapshot);
            snapshot.Presentation = (instance.Presentation ?? new SpiritLocalizedPresentation()).Clone();
            snapshot.Growth = new SpiritDeploymentGrowth
            {
                Level = instance.Level,
                Aptitude = instance.Aptitude,
                Speed = instance.Speed,
                EffectiveOrigins = SpiritAscensionService.EffectiveOrigins(instance).Clone()
            };
        }

        public bool Validate(SpiritDeploymentSnapshot snapshot, out string reason)
        {
            if (!string.Equals(snapshot.IntentRegistryHash, SpiritIntentRegistry.RegistryHash, StringComparison.Ordinal)
                || !string.Equals(snapshot.GrowthRegistryHash, SpiritGrowthRegistry.RegistryHash, StringComparison.Ordinal))
            {
                reason = "精灵核心成长或意图注册表不兼容。";
                return false;
            }
            reason = "";
            return true;
        }
    }

    private sealed class ElementFeature : ISpiritDeploymentFeature
    {
        public string Id => "element";
        public void Project(SpiritDeploymentFeatureContext context)
            => context.Snapshot.Element = new SpiritDeploymentElement { ElementId = context.Instance.ElementId };
        public bool Validate(SpiritDeploymentSnapshot snapshot, out string reason)
            => SpiritElementService.ValidateDeploymentSnapshot(snapshot, out reason);
    }

    private sealed class AscensionFeature : ISpiritDeploymentFeature
    {
        public string Id => "ascension";

        public void Project(SpiritDeploymentFeatureContext context)
        {
            var instance = context.Instance;
            context.Snapshot.Ascension = new SpiritDeploymentAscension
            {
                GuiyuanValue = instance.GuiyuanValue,
                StarRank = SpiritAscensionService.StarRankFor(instance.GuiyuanValue),
                Allocations = SpiritAscensionService.NormalizeAllocations(
                    instance.GuiyuanAllocations,
                    instance.GuiyuanValue)
            };
        }

        public bool Validate(SpiritDeploymentSnapshot snapshot, out string reason)
            => SpiritAscensionService.ValidateDeploymentSnapshot(snapshot, out reason);
    }

    private sealed class TrainingFeature : ISpiritDeploymentFeature
    {
        public string Id => "training";

        public void Project(SpiritDeploymentFeatureContext context)
        {
            var instance = context.Instance;
            context.Snapshot.TrainingRegistryHash = SpiritTrainingRegistry.RegistryHash;
            context.Snapshot.Training = new SpiritDeploymentTraining
            {
                EquippedIntentIds = new List<string>(instance.EquippedIntentIds ?? new List<string>()),
                EquippedPassiveId = instance.EquippedPassiveId,
                LoadoutRevision = instance.LoadoutRevision,
                LoadoutHash = instance.LoadoutHash
            };
        }

        public bool Validate(SpiritDeploymentSnapshot snapshot, out string reason)
            => SpiritTrainingService.ValidateDeploymentSnapshot(snapshot, out reason);
    }

    private sealed class ArtifactFeature : ISpiritDeploymentFeature
    {
        public string Id => "artifact";

        public void Project(SpiritDeploymentFeatureContext context)
        {
            context.Snapshot.ArtifactRegistryHash = SpiritArtifactRegistry.RegistryHash;
            context.Snapshot.Artifacts = SpiritArtifactLoadoutResolver.Resolve(
                context.Collection,
                context.Instance).Battle;
        }

        public bool Validate(SpiritDeploymentSnapshot snapshot, out string reason)
        {
            if (!string.Equals(snapshot.ArtifactRegistryHash, SpiritArtifactRegistry.RegistryHash, StringComparison.Ordinal))
            {
                reason = "精灵圣遗物注册表不兼容。";
                return false;
            }
            return SpiritArtifactLoadoutResolver.ValidateBattleSnapshot(snapshot.ArtifactBattle, out reason);
        }
    }
}
