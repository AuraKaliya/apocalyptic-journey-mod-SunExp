using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal static class ReplayProtocolV10
{
    internal const int DocumentVersion = 10;
    internal const int MinimumReadableDocumentVersion = 10;
    internal const int PackageVersion = 10;
    internal const long TimebaseTicksPerSecond = 1_000_000L;
    internal const int DefaultCheckpointInterval = 128;
}

internal static class ReplayTeamsV10
{
    internal const string Friendly = "Friendly";
    internal const string Enemy = "Enemy";
    internal const string Neutral = "Neutral";
}

internal static class ReplayEntityKindsV10
{
    internal const string Player = "Player";
    internal const string RemotePlayer = "RemotePlayer";
    internal const string Enemy = "Enemy";
    internal const string Summon = "Summon";
}

internal static class ReplayEventTypesV10
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

internal static class ReplayPresentationKindsV10
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

internal static class ReplaySemanticKindsV10
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

internal sealed class ReplayDocumentV10
{
    public ReplayDocumentHeaderV10 Header { get; set; } = new();

    public ReplayContentManifestV10 Content { get; set; } = new();

    public ReplayLogicalStateV10 InitialState { get; set; } = new();

    public List<ReplayTimelineEventV10> Events { get; set; } = new();

    public List<ReplayCheckpointV10> Checkpoints { get; set; } = new();

    public List<ReplayAttachmentV10> Attachments { get; set; } = new();
}

internal sealed class ReplayDocumentHeaderV10
{
    public int DocumentVersion { get; set; } = ReplayProtocolV10.DocumentVersion;

    public int MinimumReadableDocumentVersion { get; set; } = ReplayProtocolV10.MinimumReadableDocumentVersion;

    public string RecordId { get; set; } = "";

    public string AdventureId { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public string Result { get; set; } = "";

    public string GameBuild { get; set; } = "";

    public string ToolBuild { get; set; } = "";

    public string RendererBuild { get; set; } = "";

    public string RenderProfileId { get; set; } = "aura-replay-2d.v1";

    public long TimebaseTicksPerSecond { get; set; } = ReplayProtocolV10.TimebaseTicksPerSecond;

    public int CheckpointInterval { get; set; } = ReplayProtocolV10.DefaultCheckpointInterval;

    public string ContentManifestSha256 { get; set; } = "";

    public string TimelineRootSha256 { get; set; } = "";

    public string InitialLogicalStateSha256 { get; set; } = "";

    public string FinalLogicalStateSha256 { get; set; } = "";

    public string FinalEventChainSha256 { get; set; } = "";

    public string DocumentSha256 { get; set; } = "";
}

internal sealed class ReplayContentManifestV10
{
    public List<ReplayContentDependencyV10> Dependencies { get; set; } = new();

    public List<ReplayContentDefinitionV10> Definitions { get; set; } = new();
}

internal sealed class ReplayContentDependencyV10
{
    public string OwnerModId { get; set; } = "";

    public string Version { get; set; } = "";

    public string ManifestSha256 { get; set; } = "";

    public List<ReplayContentFileHashV10> Files { get; set; } = new();
}

internal sealed class ReplayContentFileHashV10
{
    public string LogicalPath { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public long ByteLength { get; set; }
}

internal sealed class ReplayContentDefinitionV10
{
    public ReplayContentRefV10 Content { get; set; } = new();

    public ReplayDisplaySnapshotV10 Display { get; set; } = new();
}

internal sealed class ReplayContentRefV10
{
    public string OwnerModId { get; set; } = "Witch";

    public string ContentKind { get; set; } = "";

    public string StableContentId { get; set; } = "";

    [JsonIgnore]
    internal string Key => OwnerModId + ":" + ContentKind + ":" + StableContentId;
}

internal sealed class ReplayDisplaySnapshotV10
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

    public List<ReplayStringValueV10> Values { get; set; } = new();
}

internal sealed class ReplayAttachmentV10
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

internal sealed class ReplayLogicalStateV10
{
    public string LevelId { get; set; } = "";

    public int TurnIndex { get; set; } = 1;

    public string ActiveActorId { get; set; } = "";

    public int PlayerPower { get; set; }

    public int PlayerMaxPower { get; set; }

    public int CardTopCount { get; set; }

    public List<ReplayActorStateV10> Actors { get; set; } = new();

    public List<ReplayCardStateV10> Cards { get; set; } = new();

    public List<ReplayIntentStateV10> Intents { get; set; } = new();
}

internal sealed class ReplayActorStateV10
{
    public string InstanceId { get; set; } = "";

    public ReplayContentRefV10 Content { get; set; } = new();

    public string EntityKind { get; set; } = "";

    public string Team { get; set; } = ReplayTeamsV10.Neutral;

    public string OwnerPlayerId { get; set; } = "";

    public int SlotIndex { get; set; }

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Defense { get; set; }

    public string State { get; set; } = "";

    public List<ReplayIntValueV10> Variables { get; set; } = new();

    public List<ReplayBuffStateV10> Buffs { get; set; } = new();
}

internal sealed class ReplayBuffStateV10
{
    public string InstanceId { get; set; } = "";

    public ReplayContentRefV10 Content { get; set; } = new();

    public int Level { get; set; }

    public int UpperBound { get; set; }

    public int ReducePerTurn { get; set; }

    public int ReducePerUse { get; set; }

    public int ReducePerAttacked { get; set; }

    public List<ReplayStringValueV10> Values { get; set; } = new();
}

internal sealed class ReplayCardStateV10
{
    public string InstanceId { get; set; } = "";

    public ReplayContentRefV10 Content { get; set; } = new();

    public string Zone { get; set; } = "";

    public int Order { get; set; }

    public int DisplayedCost { get; set; }

    public List<ReplayStringValueV10> Values { get; set; } = new();
}

internal sealed class ReplayIntentStateV10
{
    public string InstanceId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public ReplayContentRefV10 Content { get; set; } = new();

    public int SlotIndex { get; set; }

    public string DisplayValue { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();
}

internal sealed class ReplayStringValueV10
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}

internal sealed class ReplayIntValueV10
{
    public string Key { get; set; } = "";

    public long Value { get; set; }
}

internal sealed class ReplayTimelineEventV10
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

    public ReplayStateDeltaV10? Delta { get; set; }

    public List<ReplaySemanticEventV10> Semantics { get; set; } = new();

    public List<ReplayPresentationCueV10> Presentation { get; set; } = new();

    public List<ReplayAudioCueV10> Audio { get; set; } = new();

    public string StateHashAfter { get; set; } = "";

    public string EventChainHashAfter { get; set; } = "";
}

internal sealed class ReplayStateDeltaV10
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

    public List<ReplayActorStateV10> ActorUpserts { get; set; } = new();

    public List<string> RemovedActorIds { get; set; } = new();

    public List<ReplayCardStateV10> CardUpserts { get; set; } = new();

    public List<string> RemovedCardIds { get; set; } = new();

    public List<ReplayIntentStateV10> IntentUpserts { get; set; } = new();

    public List<string> RemovedIntentIds { get; set; } = new();
}

internal sealed class ReplaySemanticEventV10
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

internal sealed class ReplayPresentationCueV10
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

internal sealed class ReplayAudioCueV10
{
    public string AssetSha256 { get; set; } = "";

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

internal sealed class ReplayCheckpointV10
{
    public long EventSequence { get; set; }

    public long TimeTicks { get; set; }

    public ReplayLogicalStateV10 State { get; set; } = new();

    public string LogicalStateSha256 { get; set; } = "";

    public string EventChainSha256 { get; set; } = "";
}

internal sealed class ReplayTimelineChunkV10
{
    public int ChunkIndex { get; set; }

    public long FirstSequence { get; set; }

    public long LastSequence { get; set; }

    public long FirstTimeTicks { get; set; }

    public long LastTimeTicks { get; set; }

    public string Sha256 { get; set; } = "";

    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
