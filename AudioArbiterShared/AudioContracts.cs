using System;
using Witch.Mod;

namespace AudioArbiter.Shared;

[Serializable]
public sealed class AudioRegistryManifest
{
    public int schemaVersion = 1;
    public string ownerModId = "";
    public AudioProtocolManifest? audioProtocol;
    public AudioRegistryDefaults? defaults;
    public AudioProviderManifest[]? providers;
}

[Serializable]
public sealed class AudioProtocolManifest
{
    public int minVersion = 1;
    public int preferredVersion = 1;
}

[Serializable]
public sealed class AudioRegistryDefaults
{
    public string bus = "";
    public string policy = "";
    public bool? hardClaim;
    public bool? sync;
    public float? cooldownSeconds;
    public float? gainDb;
    public float? volumeMultiplier;
}

[Serializable]
public sealed class AudioProviderManifest
{
    public string providerId = "";
    public string ownerModId = "";
    public string displayName = "";
    public string kind = "";
    public string vocalState = "";
    public string bus = "";
    public string policy = "";
    public string path = "";
    public string[]? variantPaths;
    public int priority;
    public bool? hardClaim;
    public bool? sync;
    public float? cooldownSeconds;
    public float? gainDb;
    public float? volumeMultiplier;
    public AudioProviderMatch? match;
    public AudioSuppressOriginal? suppressOriginal;
}

[Serializable]
public sealed class AudioProviderMatch
{
    public string[]? stages;
    public string[]? careerIds;
    public string[]? roleIds;
    public string[]? cardIds;
    public int? skillSlot;
    public string[]? buffIds;
    public string[]? effectNames;
    public string[]? actionNames;
    public string[]? battleResults;
    public bool? localOwnerOnly;
    public float? hpRatioCrossDown;
}

[Serializable]
public sealed class AudioSuppressOriginal
{
    public string[]? vocalStates;
    public int[]? narrationIds;
}

public static class SoundEventKinds
{
    public const string CardUse = "CardUse";
    public const string SkillVoice = "SkillVoice";
    public const string CareerSelected = "CareerSelected";
    public const string BuffApplied = "BuffApplied";
    public const string LowHealth = "LowHealth";
    public const string BattleCompleted = "BattleCompleted";
    public const string VocalState = "VocalState";
}

public static class AudioSignalStages
{
    public const string Committed = "Committed";
    public const string PresentationCommitted = "PresentationCommitted";
    public const string Applied = "Applied";
    public const string Observed = "Observed";
    public const string ThresholdCrossedDown = "ThresholdCrossedDown";
    public const string Completed = "Completed";
}

public static class SoundBuses
{
    public const string Effect = "Effect";
    public const string Vocal = "Vocal";
    public const string Ui = "Ui";
}

public static class SoundPolicies
{
    public const string Additive = "Additive";
    public const string Replace = "Replace";
    public const string ReplaceOriginal = "ReplaceOriginal";
    public const string SuppressOriginal = "SuppressOriginal";
}

[Serializable]
public sealed class SoundPlaybackRequest
{
    public const int DefaultPresentationMaxAgeMilliseconds = 10000;

    [NonSerialized]
    public ModConfig? ModConfig;

    public string EventId { get; set; } = "";

    public string FightToken { get; set; } = "";

    public string IssuerPlayerId { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string Kind { get; set; } = "";

    public string Stage { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string StatusInstanceId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string SkillId { get; set; } = "";

    public int SkillSlot { get; set; }

    public string BuffId { get; set; } = "";

    public string EffectName { get; set; } = "";

    public string ActionName { get; set; } = "";

    public string VocalState { get; set; } = "";

    public string BattleResult { get; set; } = "";

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public float PreviousHpRatio { get; set; }

    public float HpRatio { get; set; }

    public string SourceName { get; set; } = "";

    public long CreatedAtUtcTicks { get; set; }

    public int MaxAgeMilliseconds { get; set; }

    public bool IsRemote { get; set; }

    public bool DisableSync { get; set; }

    public bool IsLocalOwner { get; set; }

    public static SoundPlaybackRequest FromObject(object request)
    {
        if (request is SoundPlaybackRequest typed)
        {
            return typed;
        }

        return new SoundPlaybackRequest
        {
            EventId = AudioPropertyReader.ReadString(request, nameof(EventId)),
            FightToken = AudioPropertyReader.ReadString(request, nameof(FightToken)),
            IssuerPlayerId = AudioPropertyReader.ReadString(request, nameof(IssuerPlayerId)),
            ProviderId = AudioPropertyReader.ReadString(request, nameof(ProviderId)),
            OwnerModId = AudioPropertyReader.ReadString(request, nameof(OwnerModId)),
            Kind = AudioPropertyReader.ReadString(request, nameof(Kind)),
            Stage = AudioPropertyReader.ReadString(request, nameof(Stage)),
            CareerId = AudioPropertyReader.ReadString(request, nameof(CareerId)),
            RoleId = AudioPropertyReader.ReadString(request, nameof(RoleId)),
            StatusInstanceId = AudioPropertyReader.ReadString(request, nameof(StatusInstanceId)),
            CardId = AudioPropertyReader.ReadString(request, nameof(CardId)),
            SkillId = AudioPropertyReader.ReadString(request, nameof(SkillId)),
            SkillSlot = AudioPropertyReader.ReadInt(request, nameof(SkillSlot), 0),
            BuffId = AudioPropertyReader.ReadString(request, nameof(BuffId)),
            EffectName = AudioPropertyReader.ReadString(request, nameof(EffectName)),
            ActionName = AudioPropertyReader.ReadString(request, nameof(ActionName)),
            VocalState = AudioPropertyReader.ReadString(request, nameof(VocalState)),
            BattleResult = AudioPropertyReader.ReadString(request, nameof(BattleResult)),
            Hp = AudioPropertyReader.ReadInt(request, nameof(Hp), 0),
            MaxHp = AudioPropertyReader.ReadInt(request, nameof(MaxHp), 0),
            PreviousHpRatio = AudioPropertyReader.ReadFloat(request, nameof(PreviousHpRatio), 0f),
            HpRatio = AudioPropertyReader.ReadFloat(request, nameof(HpRatio), 0f),
            SourceName = AudioPropertyReader.ReadString(request, nameof(SourceName)),
            CreatedAtUtcTicks = AudioPropertyReader.ReadLong(request, nameof(CreatedAtUtcTicks), 0L),
            MaxAgeMilliseconds = AudioPropertyReader.ReadInt(request, nameof(MaxAgeMilliseconds), 0),
            IsLocalOwner = AudioPropertyReader.ReadBool(request, nameof(IsLocalOwner), false)
        };
    }
}

public sealed class ResolvedSoundPlayback
{
    public SoundPlaybackRequest Request { get; set; } = new();

    public object Clip { get; set; } = null!;

    public string OwnerModId { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public string Bus { get; set; } = "";

    public float VolumeMultiplier { get; set; } = 1f;
}
