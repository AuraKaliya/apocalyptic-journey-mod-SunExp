using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class SpiritDeploymentIdentity
{
    public string SpiritUid { get; set; } = "";
    public string SpeciesId { get; set; } = "";
    public string ProfileId { get; set; } = "";

    public SpiritDeploymentIdentity Clone() => new()
    {
        SpiritUid = SpiritUid,
        SpeciesId = SpeciesId,
        ProfileId = ProfileId
    };
}

[Serializable]
public sealed class SpiritDeploymentGrowth
{
    public int Level { get; set; }
    public int Aptitude { get; set; }
    public int Speed { get; set; } = 100;
    public SpiritOriginVector EffectiveOrigins { get; set; } = new();

    public SpiritDeploymentGrowth Clone() => new()
    {
        Level = Level,
        Aptitude = Aptitude,
        Speed = Speed,
        EffectiveOrigins = EffectiveOrigins?.Clone() ?? new SpiritOriginVector()
    };
}

[Serializable]
public sealed class SpiritDeploymentElement
{
    public string ElementId { get; set; } = "";

    public SpiritDeploymentElement Clone() => new() { ElementId = ElementId };
}

[Serializable]
public sealed class SpiritDeploymentAscension
{
    public int GuiyuanValue { get; set; }
    public int StarRank { get; set; }
    public SpiritOriginVector Allocations { get; set; } = new();

    public SpiritDeploymentAscension Clone() => new()
    {
        GuiyuanValue = GuiyuanValue,
        StarRank = StarRank,
        Allocations = Allocations?.Clone() ?? new SpiritOriginVector()
    };
}

[Serializable]
public sealed class SpiritDeploymentTraining
{
    public List<string> EquippedIntentIds { get; set; } = new();
    public string EquippedPassiveId { get; set; } = "";
    public int LoadoutRevision { get; set; }
    public string LoadoutHash { get; set; } = "";

    public SpiritDeploymentTraining Clone() => new()
    {
        EquippedIntentIds = new List<string>(EquippedIntentIds ?? new List<string>()),
        EquippedPassiveId = EquippedPassiveId,
        LoadoutRevision = LoadoutRevision,
        LoadoutHash = LoadoutHash
    };
}

[Serializable]
public sealed class SpiritDeploymentSnapshot
{
    public int ProtocolVersion { get; set; } = SpiritSystemContract.DeploymentProtocolVersion;
    public string IntentRegistryHash { get; set; } = "";
    public string TrainingRegistryHash { get; set; } = "";
    public string GrowthRegistryHash { get; set; } = "";
    public string ArtifactRegistryHash { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string DeploymentToken { get; set; } = "";
    public SpiritDeploymentIdentity Identity { get; set; } = new();
    public CapturedEnemySnapshot Source { get; set; } = new();
    public SpiritLocalizedPresentation Presentation { get; set; } = new();
    public SpiritDeploymentGrowth Growth { get; set; } = new();
    public SpiritDeploymentElement Element { get; set; } = new();
    public SpiritDeploymentAscension Ascension { get; set; } = new();
    public SpiritDeploymentTraining Training { get; set; } = new();
    public SpiritArtifactBattleSnapshot Artifacts { get; set; } = new();

    [JsonIgnore] public string SpiritUid => Identity?.SpiritUid ?? "";
    [JsonIgnore] public string SpeciesId => Identity?.SpeciesId ?? "";
    [JsonIgnore] public string ProfileId => Identity?.ProfileId ?? "";
    [JsonIgnore] public string SpiritElementId => Element?.ElementId ?? "";
    [JsonIgnore] public int SpiritLevel => Growth?.Level ?? 0;
    [JsonIgnore] public int SpiritAptitude => Growth?.Aptitude ?? 0;
    [JsonIgnore] public int SpiritSpeed => Growth?.Speed ?? 100;
    [JsonIgnore] public int OriginMagic => Growth?.EffectiveOrigins?.Magic ?? 0;
    [JsonIgnore] public int OriginSpirit => Growth?.EffectiveOrigins?.Spirit ?? 0;
    [JsonIgnore] public int OriginLuck => Growth?.EffectiveOrigins?.Luck ?? 0;
    [JsonIgnore] public int OriginPerception => Growth?.EffectiveOrigins?.Perception ?? 0;
    [JsonIgnore] public int SpiritGuiyuanValue => Ascension?.GuiyuanValue ?? 0;
    [JsonIgnore] public int SpiritStarRank => Ascension?.StarRank ?? 0;
    [JsonIgnore] public int GuiyuanAllocationMagic => Ascension?.Allocations?.Magic ?? 0;
    [JsonIgnore] public int GuiyuanAllocationSpirit => Ascension?.Allocations?.Spirit ?? 0;
    [JsonIgnore] public int GuiyuanAllocationLuck => Ascension?.Allocations?.Luck ?? 0;
    [JsonIgnore] public int GuiyuanAllocationPerception => Ascension?.Allocations?.Perception ?? 0;
    [JsonIgnore] public List<string> EquippedIntentIds => Training?.EquippedIntentIds ?? new List<string>();
    [JsonIgnore] public string EquippedPassiveId => Training?.EquippedPassiveId ?? "";
    [JsonIgnore] public int LoadoutRevision => Training?.LoadoutRevision ?? 0;
    [JsonIgnore] public string LoadoutHash => Training?.LoadoutHash ?? "";
    [JsonIgnore] public SpiritArtifactBattleSnapshot ArtifactBattle => Artifacts ??= new SpiritArtifactBattleSnapshot();
    [JsonIgnore] public string SourceModId => Source?.SourceModId ?? "";
    [JsonIgnore] public string EnemyId => Source?.EnemyId ?? "";
    [JsonIgnore] public string VariantId => Source?.VariantId ?? "";
    [JsonIgnore] public string DisplayName => Source?.DisplayName ?? "";
    [JsonIgnore] public string Description => Source?.Description ?? "";
    [JsonIgnore] public string AnimationPath => Source?.AnimationPath ?? "";
    [JsonIgnore] public string DictPath => Source?.DictPath ?? "";
    [JsonIgnore] public string IdlePath => Source?.IdlePath ?? "";
    [JsonIgnore] public string CaptureOrigin => Source?.CaptureOrigin ?? "";
    [JsonIgnore] public string CapturedAt => Source?.CapturedAt ?? "";
    [JsonIgnore] public int BaseHp => Source?.BaseHp ?? 0;
    [JsonIgnore] public int BaseAttack => Source?.BaseAttack ?? 0;
    [JsonIgnore] public int BaseArmor => Source?.BaseArmor ?? 0;
    [JsonIgnore] public int Rarity => Source?.Rarity ?? 0;
    [JsonIgnore] public List<string> SourceEnemyCardIds => Source?.SourceEnemyCardIds ?? new List<string>();
    [JsonIgnore] public string ProfileKey => SpiritProfileKey.Create(EnemyId, VariantId);
    [JsonIgnore] public string IntentProfileKey => string.IsNullOrWhiteSpace(ProfileId) ? ProfileKey : ProfileId;

    public SpiritDeploymentSnapshot Clone() => new()
    {
        ProtocolVersion = ProtocolVersion,
        IntentRegistryHash = IntentRegistryHash,
        TrainingRegistryHash = TrainingRegistryHash,
        GrowthRegistryHash = GrowthRegistryHash,
        ArtifactRegistryHash = ArtifactRegistryHash,
        PayloadHash = PayloadHash,
        DeploymentToken = DeploymentToken,
        Identity = (Identity ?? new SpiritDeploymentIdentity()).Clone(),
        Source = SpiritModelCloner.CloneSnapshot(Source),
        Presentation = (Presentation ?? new SpiritLocalizedPresentation()).Clone(),
        Growth = (Growth ?? new SpiritDeploymentGrowth()).Clone(),
        Element = (Element ?? new SpiritDeploymentElement()).Clone(),
        Ascension = (Ascension ?? new SpiritDeploymentAscension()).Clone(),
        Training = (Training ?? new SpiritDeploymentTraining()).Clone(),
        Artifacts = Artifacts?.Clone() ?? new SpiritArtifactBattleSnapshot()
    };
}

public static class SpiritDeploymentCodec
{
    public const int MaximumSerializedBytes = 96 * 1024;

    public static SpiritDeploymentSnapshot Seal(SpiritDeploymentSnapshot snapshot)
    {
        var result = snapshot?.Clone() ?? new SpiritDeploymentSnapshot();
        result.ProtocolVersion = SpiritSystemContract.DeploymentProtocolVersion;
        result.PayloadHash = "";
        result.PayloadHash = ComputeHash(result);
        return result;
    }

    public static string Serialize(SpiritDeploymentSnapshot snapshot)
    {
        var sealedSnapshot = Seal(snapshot);
        var json = AuraShared.Core.AuraSharedJson.SerializeCompact(sealedSnapshot);
        if (Encoding.UTF8.GetByteCount(json) > MaximumSerializedBytes)
            throw new InvalidOperationException("Spirit deployment payload exceeds the card payload budget.");
        return json;
    }

    public static bool TryDeserialize(string value, out SpiritDeploymentSnapshot snapshot, out string reason)
    {
        snapshot = new SpiritDeploymentSnapshot();
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumSerializedBytes
            || Encoding.UTF8.GetByteCount(value) > MaximumSerializedBytes)
        {
            reason = "精灵部署载荷为空或超过限制。";
            return false;
        }
        try
        {
            snapshot = AuraShared.Core.AuraSharedJson.Deserialize<SpiritDeploymentSnapshot>(value)
                       ?? new SpiritDeploymentSnapshot();
            return ValidateIntegrity(snapshot, out reason);
        }
        catch (Exception ex)
        {
            reason = "精灵部署载荷无法解析：" + ex.Message;
            return false;
        }
    }

    public static bool ValidateIntegrity(SpiritDeploymentSnapshot snapshot, out string reason)
    {
        if (snapshot == null || snapshot.ProtocolVersion != SpiritSystemContract.DeploymentProtocolVersion)
        {
            reason = "精灵部署协议不兼容。";
            return false;
        }
        if (snapshot.Identity == null || snapshot.Source == null || snapshot.Growth == null
            || snapshot.Element == null || snapshot.Ascension == null || snapshot.Training == null
            || snapshot.Artifacts == null || string.IsNullOrWhiteSpace(snapshot.SpiritUid)
            || string.IsNullOrWhiteSpace(snapshot.EnemyId))
        {
            reason = "精灵部署载荷缺少必需组件。";
            return false;
        }
        var actual = snapshot.PayloadHash ?? "";
        if (actual.Length == 0 || !string.Equals(actual, ComputeHash(snapshot), StringComparison.Ordinal))
        {
            reason = "精灵部署载荷哈希不一致。";
            return false;
        }
        reason = "";
        return true;
    }

    private static string ComputeHash(SpiritDeploymentSnapshot snapshot)
    {
        var candidate = snapshot.Clone();
        candidate.PayloadHash = "";
        return SpiritGrowthService.StableHash(AuraShared.Core.AuraSharedJson.SerializeCompact(candidate)).ToString("x8");
    }
}

public static class SpiritDeploymentProjector
{
    public static SpiritDeploymentSnapshot Project(
        SpiritCollectionDocument collection,
        SpiritInstance instance,
        string deploymentToken)
    {
        return SpiritDeploymentFeatureRegistry.Project(collection, instance, deploymentToken);
    }
}
