using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

/// <summary>
/// Allocation-only copies for the recording hot path. Canonical JSON remains the
/// persisted/hash format, but is produced by the background storage owner.
/// </summary>
internal static class ReplayFastCloneV17
{
    internal static ReplayJournalEventV17 Event(ReplayJournalEventV17 value) => new()
    {
        Lane = value.Lane ?? "",
        Sequence = value.Sequence,
        EventId = value.EventId ?? "",
        RoundSequence = value.RoundSequence,
        ActorTurnSequence = value.ActorTurnSequence,
        TransactionId = value.TransactionId ?? "",
        StepOrdinal = value.StepOrdinal,
        CauseEventId = value.CauseEventId ?? "",
        ParentTransactionId = value.ParentTransactionId ?? "",
        TimeTicks = value.TimeTicks,
        AuthorityKind = value.AuthorityKind ?? "",
        IssuerPlayerId = value.IssuerPlayerId ?? "",
        ActorId = value.ActorId ?? "",
        EventType = value.EventType ?? "",
        Transaction = value.Transaction == null ? null : Transaction(value.Transaction),
        Entity = value.Entity == null ? null : ReplayStateReducerV17.Clone(value.Entity),
        EntityId = value.EntityId ?? "",
        SpawnGeneration = value.SpawnGeneration,
        Delta = value.Delta == null ? null : Delta(value.Delta),
        Presentation = value.Presentation == null ? null : Presentation(value.Presentation),
        StateHashBefore = value.StateHashBefore ?? "",
        StateHashAfter = value.StateHashAfter ?? "",
        PreviousLaneEventHash = value.PreviousLaneEventHash ?? "",
        EventHash = value.EventHash ?? ""
    };

    internal static ReplayCausalTransactionV17 Transaction(ReplayCausalTransactionV17 value) => new()
    {
        Kind = value.Kind ?? "",
        SourceToken = value.SourceToken ?? "",
        IssuerPlayerId = value.IssuerPlayerId ?? "",
        ActorId = value.ActorId ?? "",
        SourceInstanceId = value.SourceInstanceId ?? "",
        SourceDescriptorId = value.SourceDescriptorId ?? "",
        Label = value.Label ?? ""
    };

    internal static ReplayStateDeltaV17 Delta(ReplayStateDeltaV17 value) => new()
    {
        Operations = (value.Operations ?? new List<ReplayStateOperationV17>())
            .Where(item => item != null)
            .Select(Operation)
            .ToList()
    };

    internal static ReplayStateOperationV17 Operation(ReplayStateOperationV17 value) => new()
    {
        Kind = value.Kind ?? "",
        BattlePhase = value.BattlePhase ?? "",
        RoundSequence = value.RoundSequence,
        ActorTurnSequence = value.ActorTurnSequence,
        ActiveActorId = value.ActiveActorId ?? "",
        Outcome = value.Outcome ?? "",
        EntityId = value.EntityId ?? "",
        SpawnGeneration = value.SpawnGeneration,
        MaxHp = value.MaxHp,
        CurrentHp = value.CurrentHp,
        Defense = value.Defense,
        IsPresent = value.IsPresent,
        IsAlive = value.IsAlive,
        Buffs = (value.Buffs ?? new List<ReplayBuffStateV17>()).Select(ReplayStateReducerV17.Clone).ToList(),
        Intents = (value.Intents ?? new List<ReplayIntentStateV17>()).Select(ReplayStateReducerV17.Clone).ToList(),
        Card = value.Card == null ? null : ReplayStateReducerV17.Clone(value.Card),
        CardInstanceId = value.CardInstanceId ?? "",
        OwnerPlayerId = value.OwnerPlayerId ?? "",
        Zone = value.Zone ?? "",
        Order = value.Order,
        Count = value.Count,
        Resources = (value.Resources ?? new List<ReplayVisibleResourceStateV17>())
            .Select(ReplayStateReducerV17.Clone).ToList(),
        Extensions = (value.Extensions ?? new List<ReplayVisibleExtensionStateV17>())
            .Select(ReplayStateReducerV17.Clone).ToList()
    };

    internal static ReplayPresentationMessageV17 Presentation(ReplayPresentationMessageV17 value) => new()
    {
        Kind = value.Kind ?? "",
        DescriptorId = value.DescriptorId ?? "",
        ActorId = value.ActorId ?? "",
        TargetIds = (value.TargetIds ?? new List<string>()).Select(item => item ?? "").ToList(),
        SourceInstanceId = value.SourceInstanceId ?? "",
        SourceZone = value.SourceZone ?? "",
        SourceSlot = value.SourceSlot,
        AnimationState = value.AnimationState ?? "",
        EffectDescriptorId = value.EffectDescriptorId ?? "",
        Phase = value.Phase ?? "",
        PhaseOrdinal = value.PhaseOrdinal,
        TruthEventSequence = value.TruthEventSequence,
        DelayTicks = value.DelayTicks,
        DurationTicks = value.DurationTicks,
        Value = value.Value,
        EntityBinding = value.EntityBinding == null ? null : Binding(value.EntityBinding),
        Audio = value.Audio == null ? null : Audio(value.Audio),
        ScreenPosition = Vector(value.ScreenPosition),
        DisplayText = value.DisplayText ?? "",
        FinalDisplayText = value.FinalDisplayText ?? "",
        ExtensionOwnerModId = value.ExtensionOwnerModId ?? "",
        ExtensionTypeId = value.ExtensionTypeId ?? "",
        ExtensionSchemaVersion = value.ExtensionSchemaVersion,
        ExtensionPayloadJson = value.ExtensionPayloadJson ?? "",
        ExtensionEventId = value.ExtensionEventId ?? "",
        OwnerEntityId = value.OwnerEntityId ?? "",
        ResourcePath = value.ResourcePath ?? "",
        TransformSamples = (value.TransformSamples ?? new List<ReplayTransformSampleV17>())
            .Select(item => new ReplayTransformSampleV17
            {
                OffsetTicks = item.OffsetTicks,
                CanvasPosition = Vector(item.CanvasPosition),
                CanvasSize = Vector(item.CanvasSize),
                LocalScale = Vector(item.LocalScale),
                RotationZQ16 = item.RotationZQ16,
                AlphaQ16 = item.AlphaQ16,
                HasMaterialFade = item.HasMaterialFade,
                MaterialFadeQ16 = item.MaterialFadeQ16
            })
            .ToList(),
        WorldTransformSamples = (value.WorldTransformSamples ?? new List<ReplayWorldTransformSampleV17>())
            .Select(item => new ReplayWorldTransformSampleV17
            {
                OffsetTicks = item.OffsetTicks,
                WorldPosition = Vector(item.WorldPosition),
                RootScale = Vector(item.RootScale),
                BodyLocalPosition = Vector(item.BodyLocalPosition),
                BodyLocalScale = Vector(item.BodyLocalScale),
                SortingLayerName = item.SortingLayerName ?? "",
                SortingOrder = item.SortingOrder
            })
            .ToList(),
        Persistent = value.Persistent,
        HasCameraState = value.HasCameraState,
        CameraPosition = Vector(value.CameraPosition),
        CameraRotation = Vector(value.CameraRotation),
        CameraOrthographicSizeQ16 = value.CameraOrthographicSizeQ16
    };

    internal static ReplayEntityPresentationBindingV17 Binding(ReplayEntityPresentationBindingV17 value) => new()
    {
        EntityId = value.EntityId ?? "",
        SpawnGeneration = value.SpawnGeneration,
        DescriptorId = value.DescriptorId ?? "",
        HasMeasuredLayout = value.HasMeasuredLayout,
        WorldPosition = Vector(value.WorldPosition),
        WorldEulerAngles = Vector(value.WorldEulerAngles),
        RootScale = Vector(value.RootScale),
        BodyLocalPosition = Vector(value.BodyLocalPosition),
        BodyLocalEulerAngles = Vector(value.BodyLocalEulerAngles),
        BodyLocalScale = Vector(value.BodyLocalScale),
        HeadLocalPosition = Vector(value.HeadLocalPosition),
        BottomLocalPosition = Vector(value.BottomLocalPosition),
        CenterLocalPosition = Vector(value.CenterLocalPosition),
        StatusBarSize = Vector(value.StatusBarSize),
        HudScaleQ16 = value.HudScaleQ16,
        SortingLayerName = value.SortingLayerName ?? "",
        SortingOrder = value.SortingOrder,
        FlipX = value.FlipX,
        Color = Color(value.Color),
        CustomPresentation = value.CustomPresentation == null ? null : Custom(value.CustomPresentation)
    };

    internal static ReplayCustomEntityPresentationV17 Custom(ReplayCustomEntityPresentationV17 value) => new()
    {
        OwnerModId = value.OwnerModId ?? "",
        SchemaVersion = value.SchemaVersion,
        PresentationMode = value.PresentationMode ?? "",
        OwnerEntityId = value.OwnerEntityId ?? "",
        ReferenceHeightPixels = value.ReferenceHeightPixels,
        HorizontalOverlapQ16 = value.HorizontalOverlapQ16,
        SortingOrderOffset = value.SortingOrderOffset,
        HudMode = value.HudMode ?? "",
        HudScaleQ16 = value.HudScaleQ16,
        HudRotationQ16 = value.HudRotationQ16,
        BadgeIconResourcePath = value.BadgeIconResourcePath ?? "",
        BadgeText = value.BadgeText ?? "",
        AttackFocusTravelPixels = value.AttackFocusTravelPixels,
        InterferenceFocusTravelPixels = value.InterferenceFocusTravelPixels,
        SupportFocusTravelPixels = value.SupportFocusTravelPixels
    };

    internal static ReplayAudioCueV17 Audio(ReplayAudioCueV17 value) => new()
    {
        AssetSha256 = value.AssetSha256 ?? "",
        ResourcePath = value.ResourcePath ?? "",
        ProviderId = value.ProviderId ?? "",
        Kind = value.Kind ?? "",
        Bus = value.Bus ?? "",
        StartSample = value.StartSample,
        SourceOffsetSample = value.SourceOffsetSample,
        DurationSamples = value.DurationSamples,
        GainQ16 = value.GainQ16,
        PanQ16 = value.PanQ16,
        PlaybackRateQ16 = value.PlaybackRateQ16,
        LoopStartSample = value.LoopStartSample,
        LoopEndSample = value.LoopEndSample,
        FadeInSamples = value.FadeInSamples,
        FadeOutSamples = value.FadeOutSamples
    };

    internal static ReplayVector2Q16V17 Vector(ReplayVector2Q16V17? value) => new()
    {
        X = value?.X ?? 0,
        Y = value?.Y ?? 0
    };

    internal static ReplayVector3Q16V17 Vector(ReplayVector3Q16V17? value) => new()
    {
        X = value?.X ?? 0,
        Y = value?.Y ?? 0,
        Z = value?.Z ?? 0
    };

    internal static ReplayColorQ8V17 Color(ReplayColorQ8V17? value) => new()
    {
        R = value?.R ?? 0,
        G = value?.G ?? 0,
        B = value?.B ?? 0,
        A = value?.A ?? 0
    };
}
