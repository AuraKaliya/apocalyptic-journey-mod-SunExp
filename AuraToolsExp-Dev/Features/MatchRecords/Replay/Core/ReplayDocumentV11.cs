using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal static class ReplayProtocolV11
{
    internal const int DocumentVersion = 11;
    internal const int MinimumReadableDocumentVersion = 11;
    internal const int PackageVersion = 11;
    internal const long TimebaseTicksPerSecond = 1_000_000L;
    internal const int DefaultCheckpointInterval = 128;
}

internal static class ReplayTeamsV11
{
    internal const string Friendly = "Friendly";
    internal const string Enemy = "Enemy";
    internal const string Neutral = "Neutral";
}

internal static class ReplayEntityKindsV11
{
    internal const string Player = "Player";
    internal const string RemotePlayer = "RemotePlayer";
    internal const string Enemy = "Enemy";
    internal const string Summon = "Summon";
}

internal static class ReplayEventTypesV11
{
    internal const string TurnChanged = "TurnChanged";
    internal const string ActionStarted = "ActionStarted";
    internal const string ActionCompleted = "ActionCompleted";
    internal const string StateChanged = "StateChanged";
    internal const string BattleCompleted = "BattleCompleted";

    internal static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        TurnChanged,
        ActionStarted,
        ActionCompleted,
        StateChanged,
        BattleCompleted
    };
}

internal static class ReplayPresentationKindsV11
{
    internal const string Card = "Card";
    internal const string Skill = "Skill";
    internal const string EnemyIntent = "EnemyIntent";
    internal const string Attack = "Attack";
    internal const string Hit = "Hit";
    internal const string Heal = "Heal";
    internal const string Block = "Block";
    internal const string Buff = "Buff";
    internal const string Resource = "Resource";
    internal const string Death = "Death";
    internal const string Notice = "Notice";
}

internal static class ReplaySemanticKindsV11
{
    internal const string Damage = "Damage";
    internal const string Healing = "Healing";
    internal const string Defense = "Defense";
    internal const string Buff = "Buff";
    internal const string Resource = "Resource";
    internal const string Card = "Card";
    internal const string Intent = "Intent";
    internal const string State = "State";
}

internal sealed class ReplayDocumentV11
{
    public ReplayDocumentHeaderV11 Header { get; set; } = new();

    public ReplayContentManifestV11 Content { get; set; } = new();

    public ReplayLogicalStateV11 InitialState { get; set; } = new();

    public ReplayNativeBattleContextV11 NativeBattle { get; set; } = new();

    public List<ReplayTimelineEventV11> Events { get; set; } = new();

    public List<ReplayCheckpointV11> Checkpoints { get; set; } = new();

    public List<ReplayAttachmentV11> Attachments { get; set; } = new();
}

internal sealed class ReplayDocumentHeaderV11
{
    public int DocumentVersion { get; set; } = ReplayProtocolV11.DocumentVersion;

    public int MinimumReadableDocumentVersion { get; set; } = ReplayProtocolV11.MinimumReadableDocumentVersion;

    public string RecordId { get; set; } = "";

    public string AdventureId { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string BattleTitle { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public string Result { get; set; } = "";

    public string GameBuild { get; set; } = "";

    public string ToolBuild { get; set; } = "";

    public string RendererBuild { get; set; } = "";

    public string RenderProfileId { get; set; } = "aura-replay-native.v1";

    public string RuntimeFingerprint { get; set; } = "";

    public List<string> RequiredCapabilities { get; set; } = new();

    public long TimebaseTicksPerSecond { get; set; } = ReplayProtocolV11.TimebaseTicksPerSecond;

    public int CheckpointInterval { get; set; } = ReplayProtocolV11.DefaultCheckpointInterval;

    public string ContentManifestSha256 { get; set; } = "";

    public string TimelineRootSha256 { get; set; } = "";

    public string InitialLogicalStateSha256 { get; set; } = "";

    public string FinalLogicalStateSha256 { get; set; } = "";

    public string FinalEventChainSha256 { get; set; } = "";

    public string DocumentSha256 { get; set; } = "";
}

internal sealed class ReplayContentManifestV11
{
    public List<ReplayContentDependencyV11> Dependencies { get; set; } = new();

    public List<ReplayContentDefinitionV11> Definitions { get; set; } = new();
}

internal sealed class ReplayContentDependencyV11
{
    public string OwnerModId { get; set; } = "";

    public string Version { get; set; } = "";

    public string ManifestSha256 { get; set; } = "";

    public List<ReplayContentFileHashV11> Files { get; set; } = new();
}

internal sealed class ReplayContentFileHashV11
{
    public string LogicalPath { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public long ByteLength { get; set; }
}

internal sealed class ReplayContentDefinitionV11
{
    public ReplayContentRefV11 Content { get; set; } = new();

    public ReplayDisplaySnapshotV11 Display { get; set; } = new();
}

internal sealed class ReplayContentRefV11
{
    public string OwnerModId { get; set; } = "Witch";

    public string ContentKind { get; set; } = "";

    public string StableContentId { get; set; } = "";

    [JsonIgnore]
    internal string Key => OwnerModId + ":" + ContentKind + ":" + StableContentId;
}

internal sealed class ReplayDisplaySnapshotV11
{
    public string Name { get; set; } = "";

    public string Subtitle { get; set; } = "";

    public string Description { get; set; } = "";

    public string RulesText { get; set; } = "";

    public string IconAssetSha256 { get; set; } = "";

    public string PortraitAssetSha256 { get; set; } = "";

    public string ArtworkAssetSha256 { get; set; } = "";

    public string BackgroundAssetSha256 { get; set; } = "";

    public string AccentColor { get; set; } = "";

    public List<ReplayStringValueV11> Values { get; set; } = new();
}

internal sealed class ReplayAttachmentV11
{
    public string Sha256 { get; set; } = "";

    public string MediaType { get; set; } = "";

    public string Extension { get; set; } = "";

    public string Usage { get; set; } = "";

    public long ByteLength { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int SampleRate { get; set; }

    public int Channels { get; set; }

    public long SampleFrames { get; set; }

    public bool Required { get; set; }

    [JsonIgnore]
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal sealed class ReplayLogicalStateV11
{
    public string LevelId { get; set; } = "";

    public int TurnIndex { get; set; } = 1;

    public string ActiveActorId { get; set; } = "";

    public int PlayerPower { get; set; }

    public int PlayerMaxPower { get; set; }

    public int CardTopCount { get; set; }

    public List<ReplayActorStateV11> Actors { get; set; } = new();

    public List<ReplayCardStateV11> Cards { get; set; } = new();

    public List<ReplayIntentStateV11> Intents { get; set; } = new();
}

internal sealed class ReplayActorStateV11
{
    public string InstanceId { get; set; } = "";

    public ReplayContentRefV11 Content { get; set; } = new();

    public string EntityKind { get; set; } = "";

    public string Team { get; set; } = ReplayTeamsV11.Neutral;

    public string OwnerPlayerId { get; set; } = "";

    public int SlotIndex { get; set; }

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Defense { get; set; }

    public string State { get; set; } = "";

    public List<ReplayIntValueV11> Variables { get; set; } = new();

    public List<ReplayBuffStateV11> Buffs { get; set; } = new();
}

internal sealed class ReplayBuffStateV11
{
    public string InstanceId { get; set; } = "";

    public ReplayContentRefV11 Content { get; set; } = new();

    public int Level { get; set; }

    public int UpperBound { get; set; }

    public int ReducePerTurn { get; set; }

    public int ReducePerUse { get; set; }

    public int ReducePerAttacked { get; set; }

    public List<ReplayStringValueV11> Values { get; set; } = new();
}

internal sealed class ReplayCardStateV11
{
    public string InstanceId { get; set; } = "";

    public ReplayContentRefV11 Content { get; set; } = new();

    public string Zone { get; set; } = "";

    public int Order { get; set; }

    public int DisplayedCost { get; set; }

    public List<ReplayStringValueV11> Values { get; set; } = new();
}

internal sealed class ReplayIntentStateV11
{
    public string InstanceId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public ReplayContentRefV11 Content { get; set; } = new();

    public int SlotIndex { get; set; }

    public string DisplayValue { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();
}

internal sealed class ReplayStringValueV11
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}

internal sealed class ReplayIntValueV11
{
    public string Key { get; set; } = "";

    public long Value { get; set; }
}

internal sealed class ReplayTimelineEventV11
{
    public long Sequence { get; set; }

    public long TimeTicks { get; set; }

    public int TurnIndex { get; set; }

    public string EventId { get; set; } = "";

    public string ActionId { get; set; } = "";

    public string CauseEventId { get; set; } = "";

    public string EventType { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public ReplayStateDeltaV11? Delta { get; set; }

    public List<ReplaySemanticEventV11> Semantics { get; set; } = new();

    public List<ReplayPresentationCueV11> Presentation { get; set; } = new();

    public List<ReplayAudioCueV11> Audio { get; set; } = new();

    public ReplayNativeActionPresentationV11? NativePresentation { get; set; }

    public string StateHashAfter { get; set; } = "";

    public string EventChainHashAfter { get; set; } = "";
}

internal sealed class ReplayStateDeltaV11
{
    public bool LevelChanged { get; set; }

    public string LevelId { get; set; } = "";

    public bool TurnChanged { get; set; }

    public int TurnIndex { get; set; }

    public bool ActiveActorChanged { get; set; }

    public string ActiveActorId { get; set; } = "";

    public bool PlayerPowerChanged { get; set; }

    public int PlayerPower { get; set; }

    public int PlayerMaxPower { get; set; }

    public bool CardTopCountChanged { get; set; }

    public int CardTopCount { get; set; }

    public List<ReplayActorStateV11> ActorUpserts { get; set; } = new();

    public List<string> RemovedActorIds { get; set; } = new();

    public List<ReplayCardStateV11> CardUpserts { get; set; } = new();

    public List<string> RemovedCardIds { get; set; } = new();

    public List<ReplayIntentStateV11> IntentUpserts { get; set; } = new();

    public List<string> RemovedIntentIds { get; set; } = new();
}

internal sealed class ReplaySemanticEventV11
{
    public string Kind { get; set; } = "";

    public string Action { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public long Value { get; set; }

    public long SecondaryValue { get; set; }

    public string Label { get; set; } = "";
}

internal sealed class ReplayPresentationCueV11
{
    public string CueId { get; set; } = "";

    public string Kind { get; set; } = "";

    public long StartOffsetTicks { get; set; }

    public long DurationTicks { get; set; }

    public string ActorId { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();

    public string SourceInstanceId { get; set; } = "";

    public string Label { get; set; } = "";

    public long Value { get; set; }
}

internal sealed class ReplayAudioCueV11
{
    // v11 freezes the effective playback clip as PCM. NativeResourceId remains
    // only as an exact-installation diagnostic and interactive resolver hint.
    public string AssetSha256 { get; set; } = "";

    public string NativeResourceId { get; set; } = "";

    public string ResolutionPolicy { get; set; } = "embedded-required";

    public string OwnerModId { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public string Kind { get; set; } = "";

    public long StartSample { get; set; }

    public long SourceOffsetSample { get; set; }

    public long DurationSamples { get; set; }

    public int GainQ16 { get; set; } = 65_536;

    public int PanQ16 { get; set; }

    public int PlaybackRateQ16 { get; set; } = 65_536;

    public long LoopStartSample { get; set; }

    public long LoopEndSample { get; set; }

    public long FadeInSamples { get; set; }

    public long FadeOutSamples { get; set; }

    public string Bus { get; set; } = "Sfx";
}

internal sealed class ReplayCheckpointV11
{
    public long EventSequence { get; set; }

    public long TimeTicks { get; set; }

    public ReplayLogicalStateV11 State { get; set; } = new();

    public string LogicalStateSha256 { get; set; } = "";

    public string EventChainSha256 { get; set; } = "";
}

internal sealed class ReplayNativeBattleContextV11
{
    public string SceneName { get; set; } = "";

    public string BackgroundScene { get; set; } = "";

    public string MapMode { get; set; } = "";

    public int MapLevel { get; set; }

    public string DiceJson { get; set; } = "";

    public byte[] RoleQueue { get; set; } = Array.Empty<byte>();

    public byte[] TemporaryRoles { get; set; } = Array.Empty<byte>();

    public float EnemyPositive { get; set; }

    public float EnemyHp { get; set; }

    public string RoleTableJson { get; set; } = "";

    public List<ReplayScopedSkinSelectionV11> SkinSelections { get; set; } = new();
}

internal sealed class ReplayScopedSkinSelectionV11
{
    public string InstanceId { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string QualifiedSkinId { get; set; } = "";
}

internal sealed class ReplayNativeActionPresentationV11
{
    public string ActorAnimationState { get; set; } = "";

    public string EffectName { get; set; } = "";

    public int EffectDelayMilliseconds { get; set; } = 50;

    public int PresentationDurationMilliseconds { get; set; } = 1040;

    public List<ReplayNativeTargetPresentationV11> Targets { get; set; } = new();
}

internal sealed class ReplayNativeTargetPresentationV11
{
    public string TargetId { get; set; } = "";

    public string AnimationState { get; set; } = "";
}

internal sealed class ReplayTimelineChunkV11
{
    public int ChunkIndex { get; set; }

    public long FirstSequence { get; set; }

    public long LastSequence { get; set; }

    public long FirstTimeTicks { get; set; }

    public long LastTimeTicks { get; set; }

    public string Sha256 { get; set; } = "";

    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
