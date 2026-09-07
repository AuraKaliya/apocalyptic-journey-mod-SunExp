using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayProtocolV17
{
    internal const int DocumentVersion = 17;
    internal const int MinimumReadableDocumentVersion = 17;
    internal const int PackageVersion = 17;
    internal const long TimebaseTicksPerSecond = 1_000_000L;
    internal const int DefaultCheckpointTransactionInterval = 32;
    internal const string PresentationAbi = "aura-replay-native-presentation.v2";
}

internal static class ReplayLimitsV17
{
    internal const int MaximumTextLength = 32 * 1024;
    internal const int MaximumEventsPerLane = 1_000_000;
    internal const int MaximumCheckpoints = 50_000;
    internal const int MaximumDescriptorsPerKind = 20_000;
    internal const int MaximumFramesPerAnimation = 4_096;
    internal const int MaximumAssets = 20_000;
    internal const long MaximumAssetBytes = 32L * 1024L * 1024L;
    internal const int MaximumEntitiesPerState = 2048;
    internal const int MaximumCardsPerState = 20_000;
    internal const int MaximumIntentsPerState = 4096;
    internal const int MaximumOperationsPerTransaction = 4096;
    internal const int MaximumPresentationSamplesPerEvent = 4096;
    internal const long MaximumTimelineTicks = 24L * 60L * 60L * ReplayProtocolV17.TimebaseTicksPerSecond;
}

internal static class ReplayCapabilitiesV17
{
    internal const string CausalTransactions = "causal-transactions.v2";
    internal const string PerspectiveVisibleState = "perspective-visible-state.v1";
    internal const string ResolvedInstructionStream = "resolved-instruction-stream.v1";
    internal const string UnifiedTransactionOrder = "unified-transaction-order.v1";
    internal const string DeterministicReducer = "deterministic-visible-reducer.v1";
    internal const string StateCheckpoints = "visible-state-checkpoints.v1";
    internal const string NativeResourceProjection = "native-resource-projection.v3";
    internal const string MeasuredNativeLayout = "measured-native-layout.v1";
    internal const string NativePrefabPresentation = "native-prefab-presentation.v1";
    internal const string SharedModPresentation = "shared-mod-presentation.v1";
    internal const string ObservedOverlapTracks = "observed-overlap-tracks.v1";
    internal const string VisualStateCommit = "visual-state-commit.v1";
    internal const string NativeCardView = "native-card-view.v1";
    internal const string ObservedPresentationTimeline = "observed-presentation-timeline.v3";
    internal const string NativeFightUi = "native-fight-ui.v1";
    internal const string NativeRendererProfile = "native-renderer-profile.v1";
    internal const string PixelReadbackPreflight = "pixel-readback-preflight.v1";
    internal const string SharedPresentationModules = "shared-presentation-modules.v1";
    internal const string IncrementalPersistence = "incremental-persistence.v1";
    internal const string CrashResumableFinalization = "crash-resumable-finalization.v1";
    internal const string OwnerQualifiedExtensions = "owner-qualified-replay-extensions.v1";
    internal const string OptionalEmbeddedDynamicAssets = "embedded-dynamic-assets.v1";
    internal const string FixedRenderTextureMp4 = "fixed-rendertexture-mp4.v1";
    internal const string MeasuredAttachmentBounds = "measured-attachment-bounds.v1";
    internal const string CardViewIdentity = "observed-card-view-identity.v1";
    internal const string HandLifecycle = "observed-hand-arrival-and-layout.v1";

    internal static IEnumerable<string> RequiredFor(ReplayDocumentV17 document)
    {
        foreach (var capability in Required) yield return capability;
        if (document.PresentationEvents.Any(item => item.Presentation?.EntityBinding?.AttachmentBounds != null
            || item.Presentation?.WorldTransformSamples?.Any(sample => sample.AttachmentBounds != null) == true))
            yield return MeasuredAttachmentBounds;
        if (document.PresentationEvents.Any(item => item.Presentation?.VisualInstanceId != null))
            yield return CardViewIdentity;
        if (document.Presentation.Ui.HandPresentationContract != null
            || document.PresentationEvents.Any(item => item.Presentation?.CardView != null))
            yield return HandLifecycle;
    }

    internal static readonly string[] Required =
    {
        CausalTransactions,
        PerspectiveVisibleState,
        ResolvedInstructionStream,
        UnifiedTransactionOrder,
        DeterministicReducer,
        StateCheckpoints,
        NativeResourceProjection,
        MeasuredNativeLayout,
        NativePrefabPresentation,
        SharedModPresentation,
        ObservedOverlapTracks,
        VisualStateCommit,
        NativeCardView,
        ObservedPresentationTimeline,
        NativeFightUi,
        NativeRendererProfile,
        PixelReadbackPreflight,
        SharedPresentationModules,
        IncrementalPersistence,
        CrashResumableFinalization,
        OwnerQualifiedExtensions
    };

    internal static readonly string[] Optional =
    {
        OptionalEmbeddedDynamicAssets,
        FixedRenderTextureMp4
    };
}

internal static class ReplayJournalLanesV17
{
    internal const string Truth = "Truth";
    internal const string Presentation = "Presentation";
}

internal static class ReplayTransactionKindsV17
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
    internal const string ImplicitObserved = "ImplicitObserved";

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
        ImplicitObserved
    };
}

internal static class ReplayEventTypesV17
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
    internal const string CardMotionPresented = "CardMotionPresented";
    internal const string DamageTextPresented = "DamageTextPresented";
    internal const string TurnTransitionPresented = "TurnTransitionPresented";
    internal const string ExtensionPresented = "ExtensionPresented";
    internal const string VisualStateCommitted = "VisualStateCommitted";

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
        AudioPresented,
        CardMotionPresented,
        DamageTextPresented,
        TurnTransitionPresented,
        ExtensionPresented,
        VisualStateCommitted
    };
}

internal static class ReplayPresentationPhasesV17
{
    internal const string SourceFocus = "SourceFocus";
    internal const string CardTravel = "CardTravel";
    internal const string ActorFocus = "ActorFocus";
    internal const string Impact = "Impact";
    internal const string StateCommit = "StateCommit";
    internal const string Recovery = "Recovery";
}

internal static class ReplayPresentationTimingV17
{
    internal static long EffectiveTimeTicks(ReplayJournalEventV17 value)
    {
        if (value == null) return 0L;
        var delay = value.Lane == ReplayJournalLanesV17.Presentation
            ? Math.Max(0L, value.Presentation?.DelayTicks ?? 0L)
            : 0L;
        return value.TimeTicks > long.MaxValue - delay ? long.MaxValue : value.TimeTicks + delay;
    }
}

internal static class ReplayStateOperationKindsV17
{
    internal const string SetBattlePhase = "SetBattlePhase";
    internal const string SetRoundTurn = "SetRoundTurn";
    internal const string SetActiveActor = "SetActiveActor";
    internal const string SetOutcome = "SetOutcome";
    internal const string SetEntityVitals = "SetEntityVitals";
    internal const string SetEntityPresence = "SetEntityPresence";
    internal const string ReplaceVisibleBuffs = "ReplaceVisibleBuffs";
    internal const string ReplaceVisibleIntents = "ReplaceVisibleIntents";
    internal const string AddVisibleCard = "AddVisibleCard";
    internal const string MoveVisibleCard = "MoveVisibleCard";
    internal const string RemoveVisibleCard = "RemoveVisibleCard";
    internal const string SetVisibleZoneCount = "SetVisibleZoneCount";
    internal const string ReplaceVisibleResources = "ReplaceVisibleResources";
    internal const string ReplaceVisibleExtensions = "ReplaceVisibleExtensions";

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
        AddVisibleCard,
        MoveVisibleCard,
        RemoveVisibleCard,
        SetVisibleZoneCount,
        ReplaceVisibleResources,
        ReplaceVisibleExtensions
    };
}

internal static class ReplayTeamsV17
{
    internal const string Friendly = "Friendly";
    internal const string Enemy = "Enemy";
    internal const string Neutral = "Neutral";
}

internal static class ReplayEntityArchetypesV17
{
    internal const string PlayerCombatant = "PlayerCombatant";
    internal const string EnemyCombatant = "EnemyCombatant";
    internal const string AlliedCombatant = "AlliedCombatant";
    internal const string NeutralCombatant = "NeutralCombatant";
}

internal sealed class ReplayDocumentEnvelopeV17
{
    public ReplayDocumentV17 Document { get; set; } = new();

    public string DeclaredDocumentRoot { get; set; } = "";
}

internal sealed class ReplayDocumentV17
{
    public ReplayDocumentHeaderCoreV17 Header { get; set; } = new();

    public ReplayVisibleStateV17 InitialState { get; set; } = new();

    public List<ReplayJournalEventV17> TruthEvents { get; set; } = new();

    public List<ReplayJournalEventV17> PresentationEvents { get; set; } = new();

    public List<ReplayTruthCheckpointV17> TruthCheckpoints { get; set; } = new();

    public List<ReplayPresentationCheckpointV17> PresentationCheckpoints { get; set; } = new();

    public ReplayPresentationCapsuleV17 Presentation { get; set; } = new();

    public List<ReplayAssetV17> Assets { get; set; } = new();
}

internal sealed class ReplayDocumentHeaderCoreV17
{
    public int DocumentVersion { get; set; } = ReplayProtocolV17.DocumentVersion;

    public int MinimumReadableDocumentVersion { get; set; } = ReplayProtocolV17.MinimumReadableDocumentVersion;

    public int PackageVersion { get; set; } = ReplayProtocolV17.PackageVersion;

    public string RecordId { get; set; } = "";

    public string AdventureId { get; set; } = "";

    public string BattleSessionId { get; set; } = "";

    public string PerspectivePlayerId { get; set; } = "";

    public string PerspectiveKind { get; set; } = "Player";

    public string LevelId { get; set; } = "";

    public string BattleTitle { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public string Result { get; set; } = "";

    public string GameBuildProvenance { get; set; } = "";

    public string RecorderBuild { get; set; } = "";

    public string PresentationAbi { get; set; } = ReplayProtocolV17.PresentationAbi;

    public long TimebaseTicksPerSecond { get; set; } = ReplayProtocolV17.TimebaseTicksPerSecond;

    public List<string> RequiredCapabilities { get; set; } = new();

    public List<string> OptionalCapabilities { get; set; } = new();

    public string InitialVisibleStateSha256 { get; set; } = "";

    public string FinalVisibleStateSha256 { get; set; } = "";

    public string TruthRoot { get; set; } = "";

    public string PresentationRoot { get; set; } = "";

    public int TruthEventCount { get; set; }

    public int PresentationEventCount { get; set; }

    public int TruthCheckpointCount { get; set; }

    public int PresentationCheckpointCount { get; set; }

    public int AssetCount { get; set; }
}

internal sealed class ReplayContentProvenanceV17
{
    public string OwnerModId { get; set; } = "Witch";

    public string ContentKind { get; set; } = "";

    public string StableContentId { get; set; } = "";

    public string SourceVersion { get; set; } = "";
}

internal sealed class ReplayVisibleStateV17
{
    public string LevelId { get; set; } = "";

    public string PerspectivePlayerId { get; set; } = "";

    public string BattlePhase { get; set; } = "Bootstrap";

    public int RoundSequence { get; set; }

    public int ActorTurnSequence { get; set; }

    public string ActiveActorId { get; set; } = "";

    public string Outcome { get; set; } = "";

    public long StateVersion { get; set; }

    public List<ReplayEntityStateV17> Entities { get; set; } = new();

    public List<ReplayVisibleCardStateV17> Cards { get; set; } = new();

    public List<ReplayVisibleZoneCountV17> ZoneCounts { get; set; } = new();

    public List<ReplayIntentStateV17> Intents { get; set; } = new();

    public List<ReplayVisibleResourceStateV17> Resources { get; set; } = new();

    public List<ReplayVisibleExtensionStateV17> Extensions { get; set; } = new();
}

internal sealed class ReplayEntityStateV17
{
    public string EntityId { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public int SpawnGeneration { get; set; } = 1;

    public string Team { get; set; } = ReplayTeamsV17.Neutral;

    public string OwnerPlayerId { get; set; } = "";

    public int SlotIndex { get; set; }

    public bool IsPresent { get; set; } = true;

    public bool IsAlive { get; set; } = true;

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Defense { get; set; }

    public List<ReplayBuffStateV17> Buffs { get; set; } = new();
}

internal sealed class ReplayBuffStateV17
{
    public string InstanceId { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public int Level { get; set; }

    public int UpperBound { get; set; }

    public int VisibleDuration { get; set; }
}

internal sealed class ReplayVisibleCardStateV17
{
    public string CardInstanceId { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public string OwnerPlayerId { get; set; } = "";

    public string Zone { get; set; } = "";

    public int Order { get; set; }

    public int DisplayedCost { get; set; }

    public string RenderedName { get; set; } = "";

    public string RenderedDescription { get; set; } = "";

    public string EnchantIconResourcePath { get; set; } = "";

    public bool IsRevealed { get; set; } = true;

    public bool HasMeasuredLayout { get; set; }

    public ReplayVector2Q16V17 CanvasPosition { get; set; } = new();

    public ReplayVector2Q16V17 CanvasSize { get; set; } = new();

    public int RotationZQ16 { get; set; }

    public ReplayVector3Q16V17 LocalScale { get; set; } = ReplayVector3Q16V17.One();
}

internal sealed class ReplayVisibleZoneCountV17
{
    public string OwnerPlayerId { get; set; } = "";

    public string Zone { get; set; } = "";

    public int Count { get; set; }
}

internal sealed class ReplayIntentStateV17
{
    public string IntentInstanceId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public int SlotIndex { get; set; }

    public string DisplayValue { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();
}

internal sealed class ReplayVisibleResourceStateV17
{
    public string OwnerPlayerId { get; set; } = "";

    public string ResourceId { get; set; } = "";

    public int Value { get; set; }

    public int Maximum { get; set; }

    public string DisplayText { get; set; } = "";

    public string Name { get; set; } = "";

    public string ResourcePath { get; set; } = "";
}

internal sealed class ReplayVisibleExtensionStateV17
{
    public string OwnerModId { get; set; } = "";

    public string TypeId { get; set; } = "";

    public string InstanceId { get; set; } = "";

    public int SchemaVersion { get; set; } = 1;

    public string DisplayText { get; set; } = "";

    public string PayloadJson { get; set; } = "";
}

internal sealed class ReplayStateDeltaV17
{
    public List<ReplayStateOperationV17> Operations { get; set; } = new();
}

internal sealed class ReplayStateOperationV17
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

    public List<ReplayBuffStateV17> Buffs { get; set; } = new();

    public List<ReplayIntentStateV17> Intents { get; set; } = new();

    public ReplayVisibleCardStateV17? Card { get; set; }

    public string CardInstanceId { get; set; } = "";

    public string OwnerPlayerId { get; set; } = "";

    public string Zone { get; set; } = "";

    public int Order { get; set; }

    public int Count { get; set; }

    public List<ReplayVisibleResourceStateV17> Resources { get; set; } = new();

    public List<ReplayVisibleExtensionStateV17> Extensions { get; set; } = new();
}

internal sealed class ReplayCausalTransactionV17
{
    public string Kind { get; set; } = ReplayTransactionKindsV17.SystemPhase;

    public string SourceToken { get; set; } = "";

    public string IssuerPlayerId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string SourceDescriptorId { get; set; } = "";

    public string Label { get; set; } = "";
}

internal sealed class ReplayJournalEventV17
{
    public string Lane { get; set; } = ReplayJournalLanesV17.Truth;

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

    public ReplayCausalTransactionV17? Transaction { get; set; }

    public ReplayEntityStateV17? Entity { get; set; }

    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; }

    public ReplayStateDeltaV17? Delta { get; set; }

    public ReplayPresentationMessageV17? Presentation { get; set; }

    public string StateHashBefore { get; set; } = "";

    public string StateHashAfter { get; set; } = "";

    public string PreviousLaneEventHash { get; set; } = "";

    public string EventHash { get; set; } = "";
}

internal sealed class ReplayPresentationMessageV17
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ReplayVisibleCardStateV17? CardView { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? VisualInstanceId { get; set; }
    public string Kind { get; set; } = "";

    public string DescriptorId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();

    public string SourceInstanceId { get; set; } = "";

    public string SourceZone { get; set; } = "";

    public int SourceSlot { get; set; } = -1;

    public string AnimationState { get; set; } = "";

    public string EffectDescriptorId { get; set; } = "";

    public string Phase { get; set; } = "";

    public int PhaseOrdinal { get; set; }

    public long TruthEventSequence { get; set; }

    public long DelayTicks { get; set; }

    public long DurationTicks { get; set; }

    public long Value { get; set; }

    public ReplayEntityPresentationBindingV17? EntityBinding { get; set; }

    public ReplayAudioCueV17? Audio { get; set; }

    public ReplayVector2Q16V17 ScreenPosition { get; set; } = new();

    public string DisplayText { get; set; } = "";

    public string FinalDisplayText { get; set; } = "";

    public string ExtensionOwnerModId { get; set; } = "";

    public string ExtensionTypeId { get; set; } = "";

    public int ExtensionSchemaVersion { get; set; } = 1;

    public string ExtensionPayloadJson { get; set; } = "";

    public string ExtensionEventId { get; set; } = "";

    public string OwnerEntityId { get; set; } = "";

    public string ResourcePath { get; set; } = "";

    public List<ReplayTransformSampleV17> TransformSamples { get; set; } = new();

    public List<ReplayWorldTransformSampleV17> WorldTransformSamples { get; set; } = new();

    public bool Persistent { get; set; }

    public bool HasCameraState { get; set; }

    public ReplayVector3Q16V17 CameraPosition { get; set; } = new();

    public ReplayVector3Q16V17 CameraRotation { get; set; } = new();

    public int CameraOrthographicSizeQ16 { get; set; }
}

internal sealed class ReplayTransformSampleV17
{
    public long OffsetTicks { get; set; }
    public ReplayVector2Q16V17 CanvasPosition { get; set; } = new();
    public ReplayVector2Q16V17 CanvasSize { get; set; } = new();
    public ReplayVector3Q16V17 LocalScale { get; set; } = ReplayVector3Q16V17.One();
    public int RotationZQ16 { get; set; }
    public int AlphaQ16 { get; set; } = 65_536;
    public bool HasMaterialFade { get; set; }
    public int MaterialFadeQ16 { get; set; }
}

internal sealed class ReplayWorldTransformSampleV17
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ReplayBoundsQ16V17? AttachmentBounds { get; set; }
    public long OffsetTicks { get; set; }
    public ReplayVector3Q16V17 WorldPosition { get; set; } = new();
    public ReplayVector3Q16V17 RootScale { get; set; } = ReplayVector3Q16V17.One();
    public ReplayVector3Q16V17 BodyLocalPosition { get; set; } = new();
    public ReplayVector3Q16V17 BodyLocalScale { get; set; } = ReplayVector3Q16V17.One();
    public string SortingLayerName { get; set; } = "Default";
    public int SortingOrder { get; set; }
}

internal sealed class ReplayAudioCueV17
{
    public string AssetSha256 { get; set; } = "";

    public string ResourcePath { get; set; } = "";

    public string ProviderId { get; set; } = "";

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

internal sealed class ReplayTruthCheckpointV17
{
    public long EventSequence { get; set; }

    public long TimeTicks { get; set; }

    public string LastTruthEventHash { get; set; } = "";

    public ReplayVisibleStateV17 State { get; set; } = new();

    public string StateSha256 { get; set; } = "";

    public string CheckpointSha256 { get; set; } = "";
}

internal sealed class ReplayPresentationCheckpointV17
{
    public long EventSequence { get; set; }

    public long TimeTicks { get; set; }

    public string LastPresentationEventHash { get; set; } = "";

    public string SceneDescriptorId { get; set; } = "scene";

    public List<ReplayEntityPresentationBindingV17> EntityBindings { get; set; } = new();

    public List<ReplayEntityViewStateV17> EntityViews { get; set; } = new();

    public string CheckpointSha256 { get; set; } = "";
}

internal sealed class ReplayEntityViewStateV17
{
    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; }

    public string AnimationState { get; set; } = "Idle";

    public int FrameIndex { get; set; }

    public long AnimationStartedTicks { get; set; }

    public long AnimationEndsTicks { get; set; }
}

internal sealed class ReplayPresentationCapsuleV17
{
    public ReplaySceneDescriptorV17 Scene { get; set; } = new();

    public ReplayUiTemplateDescriptorV17 Ui { get; set; } = new();

    public List<ReplayEntityDescriptorV17> Entities { get; set; } = new();

    public List<ReplayCardDescriptorV17> Cards { get; set; } = new();

    public List<ReplayBuffDescriptorV17> Buffs { get; set; } = new();

    public List<ReplayIntentDescriptorV17> Intents { get; set; } = new();

    public List<ReplayEffectDescriptorV17> Effects { get; set; } = new();

    public List<ReplayPresentationModuleRequirementV17> Modules { get; set; } = new();
}

internal sealed class ReplayPresentationModuleRequirementV17
{
    public string OwnerModId { get; set; } = "";
    public string TypeId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string Portability { get; set; } = "Portable";
    public string BuildIdentity { get; set; } = "";
    public string RendererCapability { get; set; } = "";
}

internal sealed class ReplaySceneDescriptorV17
{
    public string DescriptorId { get; set; } = "scene";

    public int ReferenceWidth { get; set; } = 1920;

    public int ReferenceHeight { get; set; } = 1080;

    public string BackgroundAssetSha256 { get; set; } = "";

    public ReplayColorQ8V17 ClearColor { get; set; } = new() { R = 10, G = 12, B = 18, A = 255 };

    public int CameraOrthographicSizeQ16 { get; set; } = 5 * 65_536;

    public ReplayVector3Q16V17 CameraPosition { get; set; } = new() { Z = -10 * 65_536 };

    public ReplayVector3Q16V17 CameraRotation { get; set; } = new();

    public bool CameraOrthographic { get; set; } = true;

    public int CameraFieldOfViewQ16 { get; set; } = 60 * 65_536;

    public string SceneResourcePath { get; set; } = "";

    public string SceneResourceId { get; set; } = "";

}

internal sealed class ReplayUiTemplateDescriptorV17
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? HandPresentationContract { get; set; }
    public string FightUiResourcePath { get; set; } = "UI/FightUI";

    public string StatusBarResourcePath { get; set; } = "UI/StatusBarUI";

    public string HpItemResourcePath { get; set; } = "UI/HpItem";

    public string BuffBarResourcePath { get; set; } = "UI/BuffBarUI";

    public string BuffItemResourcePath { get; set; } = "UI/BuffItem";

    public string ActionContentResourcePath { get; set; } = "UI/ActionContent";

    public string ActionItemResourcePath { get; set; } = "UI/ActionMsg";

    public string CardItemResourcePath { get; set; } = "UI/CardItem";

    public string CloneMode { get; set; } = "NativePrefabSanitized";
}

internal sealed class ReplayEntityDescriptorV17
{
    public string DescriptorId { get; set; } = "";

    public string Archetype { get; set; } = ReplayEntityArchetypesV17.NeutralCombatant;

    public ReplayContentProvenanceV17 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Subtitle { get; set; } = "";

    public string NativePrefabResourcePath { get; set; } = "";

    public string IdleResourcePath { get; set; } = "";

    public string PortraitResourcePath { get; set; } = "";

    public List<ReplayAnimationDescriptorV17> Animations { get; set; } = new();

    public string SafeActionProfile { get; set; } = "default";
}

internal sealed class ReplayEntityPresentationBindingV17
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ReplayBoundsQ16V17? AttachmentBounds { get; set; }
    public string EntityId { get; set; } = "";

    public int SpawnGeneration { get; set; } = 1;

    public string DescriptorId { get; set; } = "";

    public bool HasMeasuredLayout { get; set; }

    public ReplayVector3Q16V17 WorldPosition { get; set; } = new();

    public ReplayVector3Q16V17 WorldEulerAngles { get; set; } = new();

    public ReplayVector3Q16V17 RootScale { get; set; } = ReplayVector3Q16V17.One();

    public ReplayVector3Q16V17 BodyLocalPosition { get; set; } = new();

    public ReplayVector3Q16V17 BodyLocalEulerAngles { get; set; } = new();

    public ReplayVector3Q16V17 BodyLocalScale { get; set; } = ReplayVector3Q16V17.One();

    public ReplayVector3Q16V17 HeadLocalPosition { get; set; } = new();

    public ReplayVector3Q16V17 BottomLocalPosition { get; set; } = new();

    public ReplayVector3Q16V17 CenterLocalPosition { get; set; } = new();

    public ReplayVector2Q16V17 StatusBarSize { get; set; } = new()
    {
        X = 280 * 65_536,
        Y = 78 * 65_536
    };

    public int HudScaleQ16 { get; set; } = 65_536;

    public string SortingLayerName { get; set; } = "Default";

    public int SortingOrder { get; set; }

    public bool FlipX { get; set; }

    public ReplayColorQ8V17 Color { get; set; } = new() { R = 255, G = 255, B = 255, A = 255 };

    public ReplayCustomEntityPresentationV17? CustomPresentation { get; set; }
}

internal sealed class ReplayCustomEntityPresentationV17
{
    public string OwnerModId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string PresentationMode { get; set; } = "WorldEntity";
    public string OwnerEntityId { get; set; } = "";
    public int ReferenceHeightPixels { get; set; }
    public int HorizontalOverlapQ16 { get; set; }
    public int SortingOrderOffset { get; set; }
    public string HudMode { get; set; } = "NativeHorizontal";
    public int HudScaleQ16 { get; set; } = 65_536;
    public int HudRotationQ16 { get; set; }
    public string BadgeIconResourcePath { get; set; } = "";
    public string BadgeText { get; set; } = "";
    public int AttackFocusTravelPixels { get; set; }
    public int InterferenceFocusTravelPixels { get; set; }
    public int SupportFocusTravelPixels { get; set; }
}

internal sealed class ReplayBoundsQ16V17
{
    public ReplayVector3Q16V17 Center { get; set; } = new();
    public ReplayVector3Q16V17 Size { get; set; } = new();
}

internal sealed class ReplayAnimationDescriptorV17
{
    public string State { get; set; } = "Idle";

    public string ResourcePath { get; set; } = "";

    public long FrameDurationTicks { get; set; } = 120_000L;

    public bool Loop { get; set; } = true;

    public string Direction { get; set; } = "Left";

    public string Size { get; set; } = "Normal";

    public int YOffsetQ16 { get; set; }

    public int FightYOffsetQ16 { get; set; }

    public int FightXOffsetQ16 { get; set; }

    public int TargetScaleQ16 { get; set; } = 65_536;

    public string SoundResourcePath { get; set; } = "";

    public List<string> FrameNames { get; set; } = new();

    public List<ReplaySpriteFrameV17> Frames { get; set; } = new();
}

internal sealed class ReplaySpriteFrameV17
{
    public string AssetSha256 { get; set; } = "";

    public int RectX { get; set; }

    public int RectY { get; set; }

    public int RectWidth { get; set; }

    public int RectHeight { get; set; }

    public int PivotXQ16 { get; set; } = 32_768;

    public int PivotYQ16 { get; set; } = 32_768;

    public int PixelsPerUnitQ16 { get; set; } = 100 * 65_536;

    public ReplayVector4Q16V17 Border { get; set; } = new();
}

internal sealed class ReplayCardDescriptorV17
{
    public string DescriptorId { get; set; } = "";

    public ReplayContentProvenanceV17 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Tag { get; set; } = "";

    public string CostFormat { get; set; } = "{0}";

    public string ArtworkAssetSha256 { get; set; } = "";

    public string FrameAssetSha256 { get; set; } = "";

    public string ThemeProfile { get; set; } = "default";

    public string SkinId { get; set; } = "";

    public string ResolvedSkinFrameResourcePath { get; set; } = "";

    public string ResolvedSkinBackgroundResourcePath { get; set; } = "";

    public string DynamicEffectId { get; set; } = "";

    public string DynamicEffectParametersJson { get; set; } = "";

    public bool NativeVisualTemplateRequired { get; set; } = true;

    public ReplayColorQ8V17 AccentColor { get; set; } = new() { R = 210, G = 210, B = 220, A = 255 };

    public string NativeCardType { get; set; } = "Common";

    public string NativeResourcePath { get; set; } = "UI/CardItem";

    public string IconResourcePath { get; set; } = "";

    public string FrameResourcePath { get; set; } = "";

    public string Rarity { get; set; } = "1";
}

internal sealed class ReplayBuffDescriptorV17
{
    public string DescriptorId { get; set; } = "";

    public ReplayContentProvenanceV17 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconAssetSha256 { get; set; } = "";

    public string IconResourcePath { get; set; } = "";

    public string LevelFormat { get; set; } = "{0}";

    public string Type { get; set; } = "";

    public int SortOrder { get; set; } = 100;
}

internal sealed class ReplayIntentDescriptorV17
{
    public string DescriptorId { get; set; } = "";

    public ReplayContentProvenanceV17 Provenance { get; set; } = new();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconAssetSha256 { get; set; } = "";

    public string IconResourcePath { get; set; } = "";

    public string BackIconResourcePath { get; set; } = "";

    public string TargetFormat { get; set; } = "{0}";
}

internal sealed class ReplayEffectDescriptorV17
{
    public string DescriptorId { get; set; } = "";

    public string ResourcePath { get; set; } = "";

    public string Primitive { get; set; } = "SpriteSequence";

    public List<ReplaySpriteFrameV17> Frames { get; set; } = new();

    public int FramesPerSecondQ16 { get; set; } = 12 * 65_536;

    public long DurationTicks { get; set; }

    public ReplayColorQ8V17 Color { get; set; } = new() { R = 255, G = 255, B = 255, A = 255 };
}

internal sealed class ReplayAssetV17
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

internal sealed class ReplayVector2Q16V17
{
    public int X { get; set; }

    public int Y { get; set; }
}

internal sealed class ReplayVector3Q16V17
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    internal static ReplayVector3Q16V17 One() => new() { X = 65_536, Y = 65_536, Z = 65_536 };
}

internal sealed class ReplayVector4Q16V17
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int W { get; set; }
}

internal sealed class ReplayColorQ8V17
{
    public byte R { get; set; }

    public byte G { get; set; }

    public byte B { get; set; }

    public byte A { get; set; } = 255;
}

internal sealed class ReplayJournalChunkV17
{
    public string Lane { get; set; } = ReplayJournalLanesV17.Truth;

    public int ChunkIndex { get; set; }

    public long FirstSequence { get; set; }

    public long LastSequence { get; set; }

    public long FirstTimeTicks { get; set; }

    public long LastTimeTicks { get; set; }

    public string PreviousChunkSha256 { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal sealed class ReplayValidationResultV17
{
    public List<string> Errors { get; } = new();

    public bool IsValid => Errors.Count == 0;

    public string Message => string.Join("; ", Errors);
}
