using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal static class ReplayProtocolV12
{
    internal const int DocumentVersion = 12;
    internal const int MinimumReadableDocumentVersion = 12;
    internal const int PackageVersion = 12;
    internal const long TimebaseTicksPerSecond = 1_000_000L;
    internal const int DefaultCheckpointTransactionInterval = 64;
    internal const string PresentationAbi = "aura-replay-presentation.v1";
}

internal static class ReplayLimitsV12
{
    internal const int MaximumTextLength = 32 * 1024;
    internal const int MaximumEventsPerLane = 1_000_000;
    internal const int MaximumCheckpoints = 50_000;
    internal const int MaximumDescriptorsPerKind = 20_000;
    internal const int MaximumAssets = 20_000;
    internal const long MaximumAssetBytes = 128L * 1024L * 1024L;
    internal const int MaximumEntitiesPerState = 2048;
    internal const int MaximumCardsPerState = 20_000;
    internal const int MaximumIntentsPerState = 4096;
    internal const long MaximumTimelineTicks = 24L * 60L * 60L * ReplayProtocolV12.TimebaseTicksPerSecond;
}

internal static class ReplayCapabilitiesV12
{
    internal const string CausalTransactions = "causal-transactions.v1";
    internal const string AuthoritativePublicState = "authoritative-public-state.v1";
    internal const string DualJournalLane = "dual-journal-lane.v1";
    internal const string FullCheckpoints = "full-checkpoints.v1";
    internal const string PortablePresentation = "portable-presentation.v1";
    internal const string IndependentReplayScene = "independent-replay-scene.v1";
    internal const string EmbeddedAssets = "embedded-assets.v1";
    internal const string OptionalPovSidecar = "optional-pov-sidecar.v1";
    internal const string FixedRenderTextureMp4 = "fixed-rendertexture-mp4.v1";

    internal static readonly string[] Required =
    {
        CausalTransactions,
        AuthoritativePublicState,
        DualJournalLane,
        FullCheckpoints,
        PortablePresentation,
        IndependentReplayScene,
        EmbeddedAssets
    };

    internal static readonly string[] Optional =
    {
        OptionalPovSidecar,
        FixedRenderTextureMp4
    };
}

internal static class ReplayJournalLanesV12
{
    internal const string Truth = "Truth";
    internal const string Presentation = "Presentation";
}

internal static class ReplayTransactionKindsV12
{
    internal const string Bootstrap = "Bootstrap";
    internal const string Card = "Card";
    internal const string Skill = "Skill";
    internal const string Intent = "Intent";
    internal const string Passive = "Passive";
    internal const string SystemPhase = "SystemPhase";
    internal const string Spawn = "Spawn";
    internal const string Despawn = "Despawn";
    internal const string Transform = "Transform";
    internal const string Outcome = "Outcome";
    internal const string Cleanup = "Cleanup";
    internal const string ImplicitNative = "ImplicitNative";

    internal static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Bootstrap,
        Card,
        Skill,
        Intent,
        Passive,
        SystemPhase,
        Spawn,
        Despawn,
        Transform,
        Outcome,
        Cleanup,
        ImplicitNative
    };
}

internal static class ReplayEventTypesV12
{
    internal const string TransactionStarted = "TransactionStarted";
    internal const string BattleMaterialized = "BattleMaterialized";
    internal const string FightStartSignaled = "FightStartSignaled";
    internal const string RoundStarted = "RoundStarted";
    internal const string ActorTurnStarted = "ActorTurnStarted";
    internal const string ActorTurnCompleted = "ActorTurnCompleted";
    internal const string EntitySpawned = "EntitySpawned";
    internal const string EntityDespawned = "EntityDespawned";
    internal const string StateDeltaApplied = "StateDeltaApplied";
    internal const string OutcomeEntering = "OutcomeEntering";
    internal const string BattleFinalized = "BattleFinalized";
    internal const string TransactionCompleted = "TransactionCompleted";
    internal const string TransactionAborted = "TransactionAborted";

    internal const string EntityPresented = "EntityPresented";
    internal const string EntityPresentationChanged = "EntityPresentationChanged";
    internal const string SourcePresented = "SourcePresented";
    internal const string ActorAnimationPresented = "ActorAnimationPresented";
    internal const string EffectPresented = "EffectPresented";
    internal const string HitReactionPresented = "HitReactionPresented";
    internal const string AudioPresented = "AudioPresented";

    internal static readonly HashSet<string> Truth = new(StringComparer.Ordinal)
    {
        TransactionStarted,
        BattleMaterialized,
        FightStartSignaled,
        RoundStarted,
        ActorTurnStarted,
        ActorTurnCompleted,
        EntitySpawned,
        EntityDespawned,
        StateDeltaApplied,
        OutcomeEntering,
        BattleFinalized,
        TransactionCompleted,
        TransactionAborted
    };

    internal static readonly HashSet<string> Presentation = new(StringComparer.Ordinal)
    {
        EntityPresented,
        EntityPresentationChanged,
        SourcePresented,
        ActorAnimationPresented,
        EffectPresented,
        HitReactionPresented,
        AudioPresented
    };
}

internal static class ReplayStateOperationKindsV12
{
    internal const string SetBattlePhase = "SetBattlePhase";
    internal const string SetRoundTurn = "SetRoundTurn";
    internal const string SetActiveActor = "SetActiveActor";
    internal const string SetOutcome = "SetOutcome";
    internal const string SetEntityVitals = "SetEntityVitals";
    internal const string SetEntityPresence = "SetEntityPresence";
    internal const string ReplaceVisibleBuffs = "ReplaceVisibleBuffs";
    internal const string ReplaceVisibleIntents = "ReplaceVisibleIntents";
    internal const string AddPublicCard = "AddPublicCard";
    internal const string MovePublicCard = "MovePublicCard";
    internal const string RemovePublicCard = "RemovePublicCard";
    internal const string SetPublicZoneCount = "SetPublicZoneCount";

    internal static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        SetBattlePhase,
        SetRoundTurn,
        SetActiveActor,
        SetOutcome,
        SetEntityVitals,
        SetEntityPresence,
        ReplaceVisibleBuffs,
        ReplaceVisibleIntents,
        AddPublicCard,
        MovePublicCard,
        RemovePublicCard,
        SetPublicZoneCount
    };
}

internal static class ReplayTeamsV12
{
    internal const string Friendly = "Friendly";
    internal const string Enemy = "Enemy";
    internal const string Neutral = "Neutral";
}

internal static class ReplayPovEventKindsV12
{
    internal const string UpsertPrivateCard = "UpsertPrivateCard";
    internal const string RemovePrivateCard = "RemovePrivateCard";

    internal static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        UpsertPrivateCard,
        RemovePrivateCard
    };
}

internal static class ReplayEntityArchetypesV12
{
    internal const string PlayerCombatant = "PlayerCombatant";
    internal const string EnemyCombatant = "EnemyCombatant";
    internal const string AlliedCombatant = "AlliedCombatant";
    internal const string NeutralCombatant = "NeutralCombatant";
}

internal sealed class ReplayDocumentEnvelopeV12
{
    public ReplayDocumentV12 Document { get; set; } = new();

    public string DeclaredDocumentRoot { get; set; } = "";
}

internal sealed class ReplayDocumentV12
{
    public ReplayDocumentHeaderCoreV12 Header { get; set; } = new();

    public ReplayPublicStateV12 InitialState { get; set; } = new();

    public List<ReplayJournalEventV12> TruthEvents { get; set; } = new();

    public List<ReplayJournalEventV12> PresentationEvents { get; set; } = new();

    public List<ReplayTruthCheckpointV12> TruthCheckpoints { get; set; } = new();

    public List<ReplayPresentationCheckpointV12> PresentationCheckpoints { get; set; } = new();

    public ReplayPresentationCapsuleV12 Presentation { get; set; } = new();

    public List<ReplayAssetV12> Assets { get; set; } = new();
}

internal sealed class ReplayDocumentHeaderCoreV12
{
    public int DocumentVersion { get; set; } = ReplayProtocolV12.DocumentVersion;

    public int MinimumReadableDocumentVersion { get; set; } = ReplayProtocolV12.MinimumReadableDocumentVersion;

    public int PackageVersion { get; set; } = ReplayProtocolV12.PackageVersion;

    public string RecordId { get; set; } = "";

    public string AdventureId { get; set; } = "";

    public string BattleSessionId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string BattleTitle { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public string Result { get; set; } = "";

    public string GameBuildProvenance { get; set; } = "";

    public string RecorderBuild { get; set; } = "";

    public string PresentationAbi { get; set; } = ReplayProtocolV12.PresentationAbi;

    public long TimebaseTicksPerSecond { get; set; } = ReplayProtocolV12.TimebaseTicksPerSecond;

    public List<string> RequiredCapabilities { get; set; } = new();

    public List<string> OptionalCapabilities { get; set; } = new();

    public string InitialPublicStateSha256 { get; set; } = "";

    public string FinalPublicStateSha256 { get; set; } = "";

    public string TruthRoot { get; set; } = "";

    public string PresentationRoot { get; set; } = "";

    public int TruthEventCount { get; set; }

    public int PresentationEventCount { get; set; }

    public int TruthCheckpointCount { get; set; }

    public int PresentationCheckpointCount { get; set; }

    public int AssetCount { get; set; }
}

internal sealed class ReplayContentProvenanceV12
{
    public string OwnerModId { get; set; } = "Witch";

    public string ContentKind { get; set; } = "";

    public string StableContentId { get; set; } = "";

    public string SourceVersion { get; set; } = "";
}

internal sealed class ReplayPublicStateV12
{
    public string LevelId { get; set; } = "";

    public string BattlePhase { get; set; } = "Bootstrap";

    public int RoundSequence { get; set; }

    public int ActorTurnSequence { get; set; }

    public string ActiveActorId { get; set; } = "";

    public string Outcome { get; set; } = "";

    public long StateVersion { get; set; }

    public List<ReplayEntityStateV12> Entities { get; set; } = new();

    public List<ReplayPublicCardStateV12> Cards { get; set; } = new();

    public List<ReplayPublicZoneCountV12> ZoneCounts { get; set; } = new();

    public List<ReplayIntentStateV12> Intents { get; set; } = new();
}

internal sealed class ReplayEntityStateV12
{
    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; } = 1;

    public string Team { get; set; } = ReplayTeamsV12.Neutral;

    public string OwnerPlayerId { get; set; } = "";

    public int SlotIndex { get; set; }

    public bool IsPresent { get; set; } = true;

    public bool IsAlive { get; set; } = true;

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Defense { get; set; }

    public List<ReplayBuffStateV12> Buffs { get; set; } = new();
}

internal sealed class ReplayBuffStateV12
{
    public string InstanceId { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public int Level { get; set; }

    public int UpperBound { get; set; }

    public int VisibleDuration { get; set; }
}

internal sealed class ReplayPublicCardStateV12
{
    public string CardInstanceId { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public string OwnerPlayerId { get; set; } = "";

    public string Zone { get; set; } = "";

    public int Order { get; set; }

    public int DisplayedCost { get; set; }
}

internal sealed class ReplayPublicZoneCountV12
{
    public string OwnerPlayerId { get; set; } = "";

    public string Zone { get; set; } = "";

    public int Count { get; set; }
}

internal sealed class ReplayIntentStateV12
{
    public string IntentInstanceId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public int SlotIndex { get; set; }

    public string DisplayValue { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();
}

internal sealed class ReplayStateDeltaV12
{
    public List<ReplayStateOperationV12> Operations { get; set; } = new();
}

internal sealed class ReplayStateOperationV12
{
    public string Kind { get; set; } = "";

    public string BattlePhase { get; set; } = "";

    public int RoundSequence { get; set; }

    public int ActorTurnSequence { get; set; }

    public string ActiveActorId { get; set; } = "";

    public string Outcome { get; set; } = "";

    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; }

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Defense { get; set; }

    public bool IsPresent { get; set; }

    public bool IsAlive { get; set; }

    public List<ReplayBuffStateV12> Buffs { get; set; } = new();

    public List<ReplayIntentStateV12> Intents { get; set; } = new();

    public ReplayPublicCardStateV12? Card { get; set; }

    public string CardInstanceId { get; set; } = "";

    public string OwnerPlayerId { get; set; } = "";

    public string Zone { get; set; } = "";

    public int Order { get; set; }

    public int Count { get; set; }
}

internal sealed class ReplayCausalTransactionV12
{
    public string Kind { get; set; } = ReplayTransactionKindsV12.SystemPhase;

    public string SourceToken { get; set; } = "";

    public string IssuerPlayerId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string SourceDescriptorId { get; set; } = "";

    public string Label { get; set; } = "";
}

internal sealed class ReplayJournalEventV12
{
    public string Lane { get; set; } = ReplayJournalLanesV12.Truth;

    public long Sequence { get; set; }

    public string EventId { get; set; } = "";

    public int RoundSequence { get; set; }

    public int ActorTurnSequence { get; set; }

    public string TransactionId { get; set; } = "";

    public int StepOrdinal { get; set; }

    public string CauseEventId { get; set; } = "";

    public string ParentTransactionId { get; set; } = "";

    public long TimeTicks { get; set; }

    public string AuthorityKind { get; set; } = "Host";

    public string IssuerPlayerId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string EventType { get; set; } = "";

    public ReplayCausalTransactionV12? Transaction { get; set; }

    public ReplayEntityStateV12? Entity { get; set; }

    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; }

    public ReplayStateDeltaV12? Delta { get; set; }

    public ReplayPresentationMessageV12? Presentation { get; set; }

    public string StateHashBefore { get; set; } = "";

    public string StateHashAfter { get; set; } = "";

    public string PreviousLaneEventHash { get; set; } = "";

    public string EventHash { get; set; } = "";
}

internal sealed class ReplayPresentationMessageV12
{
    public string Kind { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();

    public string SourceInstanceId { get; set; } = "";

    public string SourceZone { get; set; } = "";

    public int SourceSlot { get; set; } = -1;

    public string AnimationState { get; set; } = "";

    public string EffectDescriptorId { get; set; } = "";

    public long DelayTicks { get; set; }

    public long DurationTicks { get; set; }

    public long Value { get; set; }

    public ReplayEntityPresentationBindingV12? EntityBinding { get; set; }

    public ReplayAudioCueV12? Audio { get; set; }
}

internal sealed class ReplayAudioCueV12
{
    public string AssetSha256 { get; set; } = "";

    public string Kind { get; set; } = "";

    public string Bus { get; set; } = "Effect";

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
}

internal sealed class ReplayTruthCheckpointV12
{
    public long EventSequence { get; set; }

    public long TimeTicks { get; set; }

    public string LastTruthEventHash { get; set; } = "";

    public ReplayPublicStateV12 State { get; set; } = new();

    public string StateSha256 { get; set; } = "";

    public string CheckpointSha256 { get; set; } = "";
}

internal sealed class ReplayPresentationCheckpointV12
{
    public long EventSequence { get; set; }

    public long TimeTicks { get; set; }

    public string LastPresentationEventHash { get; set; } = "";

    public string SceneDescriptorId { get; set; } = "scene";

    public List<ReplayEntityPresentationBindingV12> EntityBindings { get; set; } = new();

    public List<ReplayEntityViewStateV12> EntityViews { get; set; } = new();

    public string CheckpointSha256 { get; set; } = "";
}

internal sealed class ReplayEntityViewStateV12
{
    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; }

    public string AnimationState { get; set; } = "Idle";

    public int FrameIndex { get; set; }

    public long AnimationStartedTicks { get; set; }

    public long AnimationEndsTicks { get; set; }
}

internal sealed class ReplayPresentationCapsuleV12
{
    public ReplaySceneDescriptorV12 Scene { get; set; } = new();

    public List<ReplayEntityDescriptorV12> Entities { get; set; } = new();

    public List<ReplayCardDescriptorV12> Cards { get; set; } = new();

    public List<ReplayBuffDescriptorV12> Buffs { get; set; } = new();

    public List<ReplayIntentDescriptorV12> Intents { get; set; } = new();

    public List<ReplayEffectDescriptorV12> Effects { get; set; } = new();
}

internal sealed class ReplaySceneDescriptorV12
{
    public string DescriptorId { get; set; } = "scene";

    public int ReferenceWidth { get; set; } = 1920;

    public int ReferenceHeight { get; set; } = 1080;

    public string BackgroundAssetSha256 { get; set; } = "";

    public ReplayColorQ8V12 ClearColor { get; set; } = new() { R = 10, G = 12, B = 18, A = 255 };

    public int CameraOrthographicSizeQ16 { get; set; } = 5 * 65_536;

    public List<ReplayLayoutAnchorV12> Anchors { get; set; } = new();
}

internal sealed class ReplayLayoutAnchorV12
{
    public string AnchorId { get; set; } = "";

    public ReplayVector2Q16V12 Position { get; set; } = new();
}

internal sealed class ReplayEntityDescriptorV12
{
    public string DescriptorId { get; set; } = "";

    public string Archetype { get; set; } = ReplayEntityArchetypesV12.NeutralCombatant;

    public ReplayContentProvenanceV12 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Subtitle { get; set; } = "";

    public List<ReplayAnimationDescriptorV12> Animations { get; set; } = new();

    public string SafeActionProfile { get; set; } = "default";
}

internal sealed class ReplayEntityPresentationBindingV12
{
    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; } = 1;

    public string DescriptorId { get; set; } = "";

    public string LayoutAnchor { get; set; } = "";

    public ReplayVector2Q16V12 Offset { get; set; } = new();

    public int ScaleQ16 { get; set; } = 65_536;

    public int SortingOrder { get; set; }

    public bool FlipX { get; set; }

    public ReplayColorQ8V12 Color { get; set; } = new() { R = 255, G = 255, B = 255, A = 255 };
}

internal sealed class ReplayAnimationDescriptorV12
{
    public string State { get; set; } = "Idle";

    public int FramesPerSecondQ16 { get; set; } = 8 * 65_536;

    public bool Loop { get; set; } = true;

    public List<ReplaySpriteFrameV12> Frames { get; set; } = new();
}

internal sealed class ReplaySpriteFrameV12
{
    public string AssetSha256 { get; set; } = "";

    public int RectX { get; set; }

    public int RectY { get; set; }

    public int RectWidth { get; set; }

    public int RectHeight { get; set; }

    public int PivotXQ16 { get; set; } = 32_768;

    public int PivotYQ16 { get; set; } = 32_768;

    public int PixelsPerUnitQ16 { get; set; } = 100 * 65_536;
}

internal sealed class ReplayCardDescriptorV12
{
    public string DescriptorId { get; set; } = "";

    public ReplayContentProvenanceV12 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Tag { get; set; } = "";

    public string CostFormat { get; set; } = "{0}";

    public string ArtworkAssetSha256 { get; set; } = "";

    public string FrameAssetSha256 { get; set; } = "";

    public string ThemeProfile { get; set; } = "default";

    public ReplayColorQ8V12 AccentColor { get; set; } = new() { R = 210, G = 210, B = 220, A = 255 };
}

internal sealed class ReplayBuffDescriptorV12
{
    public string DescriptorId { get; set; } = "";

    public ReplayContentProvenanceV12 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconAssetSha256 { get; set; } = "";

    public string LevelFormat { get; set; } = "{0}";
}

internal sealed class ReplayIntentDescriptorV12
{
    public string DescriptorId { get; set; } = "";

    public ReplayContentProvenanceV12 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconAssetSha256 { get; set; } = "";

    public string TargetFormat { get; set; } = "{0}";
}

internal sealed class ReplayEffectDescriptorV12
{
    public string DescriptorId { get; set; } = "";

    public string Primitive { get; set; } = "SpriteSequence";

    public List<ReplaySpriteFrameV12> Frames { get; set; } = new();

    public int FramesPerSecondQ16 { get; set; } = 12 * 65_536;

    public long DurationTicks { get; set; }

    public ReplayColorQ8V12 Color { get; set; } = new() { R = 255, G = 255, B = 255, A = 255 };
}

internal sealed class ReplayAssetV12
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

    public bool Required { get; set; } = true;

    [JsonIgnore]
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal sealed class ReplayVector2Q16V12
{
    public int X { get; set; }

    public int Y { get; set; }
}

internal sealed class ReplayColorQ8V12
{
    public byte R { get; set; }

    public byte G { get; set; }

    public byte B { get; set; }

    public byte A { get; set; } = 255;
}

internal sealed class ReplayPovSidecarV12
{
    public int SidecarVersion { get; set; } = 1;

    public string ParentDocumentRoot { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public List<ReplayPovEventV12> Events { get; set; } = new();

    public List<ReplayCardDescriptorV12> PrivateCards { get; set; } = new();

    public List<ReplayAssetV12> Assets { get; set; } = new();

    public string EventChainSha256 { get; set; } = "";

    public string SidecarSha256 { get; set; } = "";
}

internal sealed class ReplayPovEventV12
{
    public long Sequence { get; set; }

    public long CanonicalSequence { get; set; }

    public string TransactionId { get; set; } = "";

    public int StepOrdinal { get; set; }

    public string Kind { get; set; } = "";

    public ReplayPublicCardStateV12? Card { get; set; }

    public string CardInstanceId { get; set; } = "";

    public string PreviousEventHash { get; set; } = "";

    public string EventHash { get; set; } = "";
}

internal sealed class ReplayJournalChunkV12
{
    public string Lane { get; set; } = ReplayJournalLanesV12.Truth;

    public int ChunkIndex { get; set; }

    public long FirstSequence { get; set; }

    public long LastSequence { get; set; }

    public long FirstTimeTicks { get; set; }

    public long LastTimeTicks { get; set; }

    public string PreviousChunkSha256 { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal sealed class ReplayValidationResultV12
{
    public List<string> Errors { get; } = new();

    public bool IsValid => Errors.Count == 0;

    public string Message => string.Join("; ", Errors);
}
