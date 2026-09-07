using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AuraGameData.Shared.GameApi;
using AuraReplay.Presentation.Shared;
using AuraReplay.VisibleState.Shared;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Hooks;

internal sealed class TerriasSpiritReplayVisibleStateProvider : IAuraReplayVisibleStateProvider
{
    private static IDisposable? registration;
    private static IDisposable? projectionRegistration;

    internal static void Initialize()
    {
        registration?.Dispose();
        projectionRegistration?.Dispose();
        AuraReplayVisibleStateRuntime.ClearOwner(TerriasIds.ModId);
        registration = AuraReplayVisibleStateRuntime.Register(new TerriasSpiritReplayVisibleStateProvider());
        projectionRegistration = AuraReplayVisibleStateRuntime.Register(
            new TerriasProjectionReplayVisibleStateProvider());
        TerriasSpiritReplayEntityPresentationProvider.Initialize();
        AuraReplayPresentationRuntime.ClearOwner(TerriasIds.ModId);
        TerriasSpiritReplayPresentationModule.Initialize();
        TerriasProjectionReplayPresentationModule.Initialize();
        TerriasStarScoreReplayPresentationModule.Initialize();
        TerriasWunaOrbitReplayPresentationModule.Initialize();
    }

    public string OwnerModId => TerriasIds.ModId;
    public string TypeId => "SpiritDeployment";
    public int SchemaVersion => 1;

    public IReadOnlyList<AuraReplayVisibleStateItem> Capture(AuraReplayVisibleCaptureContext context)
    {
        var snapshot = SpiritBattleDeploymentService.DeploymentCardSnapshot();
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.SpiritUid))
            return Array.Empty<AuraReplayVisibleStateItem>();
        var name = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.EnemyId : snapshot.DisplayName;
        return new[]
        {
            new AuraReplayVisibleStateItem
            {
                InstanceId = snapshot.SpiritUid,
                DisplayText = "精灵 " + name + "  Lv." + snapshot.SpiritLevel + "  ★" + snapshot.SpiritStarRank,
                PayloadJson = AuraSharedJson.SerializeCompact(new SpiritReplayPayload
                {
                    Aptitude = snapshot.SpiritAptitude,
                    ElementId = snapshot.SpiritElementId,
                    Guiyuan = snapshot.SpiritGuiyuanValue,
                    Level = snapshot.SpiritLevel,
                    SpiritUid = snapshot.SpiritUid,
                    StarRank = snapshot.SpiritStarRank
                })
            }
        };
    }

    private sealed class SpiritReplayPayload
    {
        public int Aptitude { get; set; }
        public string ElementId { get; set; } = "";
        public int Guiyuan { get; set; }
        public int Level { get; set; }
        public string SpiritUid { get; set; } = "";
        public int StarRank { get; set; }
    }
}

internal sealed class TerriasProjectionReplayVisibleStateProvider : IAuraReplayVisibleStateProvider
{
    public string OwnerModId => TerriasIds.ModId;
    public string TypeId => "ProjectionDeployment";
    public int SchemaVersion => 1;

    public IReadOnlyList<AuraReplayVisibleStateItem> Capture(AuraReplayVisibleCaptureContext context)
    {
        return ProjectionStateStore.Active()
            .Where(state => state?.Projection?.Status != null
                            && !string.IsNullOrWhiteSpace(state.StatusId))
            .OrderBy(state => state.StatusId, StringComparer.Ordinal)
            .Select(state => new AuraReplayVisibleStateItem
            {
                InstanceId = state.StatusId,
                DisplayText = "投影 " + state.RoleId,
                PayloadJson = AuraSharedJson.SerializeCompact(new
                {
                    state.RoleId,
                    state.OwnerStatusId,
                    state.OwnerPlayerId,
                    state.ExecutionRoutePlayerId,
                    state.SlotIndex,
                    generation = state.Replication.Generation,
                    state.IsSuspended
                })
            })
            .ToList();
    }
}

internal sealed class TerriasSpiritReplayPresentationModule : IAuraReplayPresentationModule
{
    private static IDisposable? registration;
    private static bool subscribed;
    private static long eventSequence;

    public AuraReplayPresentationModuleDescriptor Descriptor { get; } = new()
    {
        OwnerModId = TerriasIds.ModId,
        TypeId = "SpiritBattlePresentation",
        SchemaVersion = 1,
        Portability = AuraReplayPresentationPortability.ProviderRequired,
        BuildIdentity = typeof(TerriasSpiritReplayPresentationModule).Assembly.GetName().Version + "+"
                        + typeof(TerriasSpiritReplayPresentationModule).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
        RendererCapability = "owner-attached-spirit.v1"
    };

    internal static void Initialize()
    {
        registration?.Dispose();
        registration = AuraReplayPresentationRuntime.Register(new TerriasSpiritReplayPresentationModule());
        if (subscribed) return;
        subscribed = true;
        SpiritStateStore.Registered += OnRegistered;
        SpiritStateStore.Retired += OnRetired;
        SpiritStateStore.IntentPresented += OnIntentPresented;
        SpiritStateStore.ActionPresented += OnActionPresented;
    }

    private static void OnRegistered(SpiritState state)
    {
        if (state == null) return;
        Publish(
            state,
            AuraReplayPresentationKinds.VisibilityChanged,
            "spawn",
            Array.Empty<string>(),
            AuraSharedJson.SerializeCompact(new
            {
                visible = true,
                state.Generation,
                state.SlotIndex,
                state.Snapshot.SpiritElementId
            }),
            0L,
            persistent: true);
    }

    private static void OnRetired(SpiritState state)
    {
        if (state == null) return;
        Publish(
            state,
            AuraReplayPresentationKinds.VisibilityChanged,
            "retire",
            Array.Empty<string>(),
            AuraSharedJson.SerializeCompact(new
            {
                visible = false,
                state.Generation
            }),
            0L,
            persistent: true);
    }

    private static void OnIntentPresented(SpiritState state, CompanionIntentPlan plan)
    {
        if (state == null || plan == null) return;
        var battleState = CompanionBattleStateStore.Find(state.StatusId);
        var intent = CompanionIntentResolver.Find(battleState, plan.IntentId);
        var intentType = CompanionIntentResolver.IntentType(battleState, intent).ToString();
        var source = AuraGameDataHostApi.Resolve(DataType.EnemyCard, plan.EnemyCardId);
        var iconPath = plan.IsWait ? "" : TerriasReplayIntentVisualApi.Icon(
            source?.Fields.TryGetValue("Icon", out var icon) == true ? icon : "");
        var backIconPath = plan.IsWait ? "" : TerriasReplayIntentVisualApi.Background(
            source?.Fields.TryGetValue("BackIcon", out var backIcon) == true ? backIcon : "");
        Publish(
            state,
            AuraReplayPresentationKinds.IntentChanged,
            plan.PlanId,
            plan.OrderedTargetIds ?? new List<string>(),
            AuraSharedJson.SerializeCompact(new
            {
                planId = plan.PlanId ?? "",
                intentId = plan.IntentId ?? "",
                intentType,
                isWait = plan.IsWait,
                visualResourceContract = TerriasReplayIntentVisualApi.Contract,
                iconResourcePath = iconPath,
                backIconResourcePath = backIconPath,
                displayValue = plan.ResolvedValue == 0 ? "" : plan.ResolvedValue.ToString(),
                targets = plan.OrderedTargetIds ?? new List<string>()
            }),
            0L,
            persistent: true);
    }

    private static void OnActionPresented(SpiritState state)
    {
        if (state == null) return;
        var battleState = CompanionBattleStateStore.Find(state.StatusId);
        var plan = battleState?.CurrentPlan;
        var intent = CompanionIntentResolver.Find(battleState, plan?.IntentId ?? "");
        var intentType = CompanionIntentResolver.IntentType(battleState, intent);
        var travelPixels = intentType == CompanionIntentType.Attack
            ? 70
            : intentType == CompanionIntentType.Interference
                ? 45
                : 12;
        var peakScaleQ16 = intentType == CompanionIntentType.Attack
            ? 73_400
            : intentType == CompanionIntentType.Interference
                ? 70_124
                : 70_779;
        Publish(
            state,
            AuraReplayPresentationKinds.OwnerAttachedFocus,
            plan?.PlanId ?? "action",
            plan?.OrderedTargetIds ?? new List<string>(),
            AuraSharedJson.SerializeCompact(new
            {
                intentType = intentType.ToString(),
                travelPixels,
                peakScaleQ16,
                enterMicroseconds = 120_000,
                holdMicroseconds = 100_000,
                returnMicroseconds = 180_000
            }),
            400_000L,
            persistent: false);
    }

    private static void Publish(
        SpiritState state,
        string kind,
        string sourceIdentity,
        IEnumerable<string> targets,
        string payload,
        long durationMicroseconds,
        bool persistent)
    {
        var sequence = Interlocked.Increment(ref eventSequence);
        var eventId = TerriasIds.ModId + ":spirit:" + state.StatusId + ":g" + state.Generation
                      + ":" + kind + ":" + sequence;
        AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
        {
            EventId = eventId,
            DuplicateKey = eventId,
            OwnerModId = TerriasIds.ModId,
            TypeId = "SpiritBattlePresentation",
            SchemaVersion = 1,
            Kind = kind,
            ActorEntityId = state.StatusId,
            OwnerEntityId = state.OwnerStatusId,
            IssuerPlayerId = state.OwnerPlayerId,
            TargetEntityIds = (targets ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            DisplayText = sourceIdentity ?? "",
            PayloadJson = payload,
            DurationMicroseconds = durationMicroseconds,
            Persistent = persistent
        });
    }
}

internal sealed class TerriasProjectionReplayPresentationModule : IAuraReplayPresentationModule
{
    private static IDisposable? registration;
    private static bool subscribed;
    private static long eventSequence;

    public AuraReplayPresentationModuleDescriptor Descriptor { get; } = new()
    {
        OwnerModId = TerriasIds.ModId,
        TypeId = "ProjectionBattlePresentation",
        SchemaVersion = 1,
        Portability = AuraReplayPresentationPortability.Portable,
        BuildIdentity = typeof(TerriasProjectionReplayPresentationModule).Assembly.GetName().Version + "+"
                        + typeof(TerriasProjectionReplayPresentationModule).Assembly.ManifestModule.ModuleVersionId.ToString("N")
    };

    internal static void Initialize()
    {
        registration?.Dispose();
        registration = AuraReplayPresentationRuntime.Register(new TerriasProjectionReplayPresentationModule());
        if (subscribed) return;
        subscribed = true;
        ProjectionStateStore.Registered += OnRegistered;
        ProjectionStateStore.Retired += OnRetired;
        ProjectionStateStore.IntentPresented += OnIntentPresented;
    }

    private static void OnRegistered(ProjectionState state)
    {
        if (state == null) return;
        Publish(
            state,
            AuraReplayPresentationKinds.VisibilityChanged,
            "spawn",
            Array.Empty<string>(),
            AuraSharedJson.SerializeCompact(new
            {
                visible = true,
                generation = state.Replication.Generation,
                state.SlotIndex,
                state.RoleId
            }),
            persistent: true);
    }

    private static void OnRetired(ProjectionState state)
    {
        if (state == null) return;
        Publish(
            state,
            AuraReplayPresentationKinds.VisibilityChanged,
            "retire",
            Array.Empty<string>(),
            AuraSharedJson.SerializeCompact(new
            {
                visible = false,
                generation = state.Replication.Generation
            }),
            persistent: true);
    }

    private static void OnIntentPresented(ProjectionState state, CompanionIntentPlan plan)
    {
        if (state == null || plan == null) return;
        var battleState = CompanionBattleStateStore.Find(state.StatusId);
        var intent = CompanionIntentResolver.Find(battleState, plan.IntentId);
        var intentType = CompanionIntentResolver.IntentType(battleState, intent).ToString();
        var source = AuraGameDataHostApi.Resolve(DataType.EnemyCard, plan.EnemyCardId);
        var iconPath = plan.IsWait ? "" : TerriasReplayIntentVisualApi.Icon(
            source?.Fields.TryGetValue("Icon", out var icon) == true ? icon : "");
        var backIconPath = plan.IsWait ? "" : TerriasReplayIntentVisualApi.Background(
            source?.Fields.TryGetValue("BackIcon", out var backIcon) == true ? backIcon : "");
        Publish(
            state,
            AuraReplayPresentationKinds.IntentChanged,
            plan.PlanId,
            plan.OrderedTargetIds ?? new List<string>(),
            AuraSharedJson.SerializeCompact(new
            {
                planId = plan.PlanId ?? "",
                intentId = plan.IntentId ?? "",
                intentType,
                isWait = plan.IsWait,
                visualResourceContract = TerriasReplayIntentVisualApi.Contract,
                iconResourcePath = iconPath,
                backIconResourcePath = backIconPath,
                displayValue = plan.ResolvedValue == 0 ? "" : plan.ResolvedValue.ToString(),
                targets = plan.OrderedTargetIds ?? new List<string>()
            }),
            persistent: true);
    }

    private static void Publish(
        ProjectionState state,
        string kind,
        string sourceIdentity,
        IEnumerable<string> targets,
        string payload,
        bool persistent)
    {
        var sequence = Interlocked.Increment(ref eventSequence);
        var eventId = TerriasIds.ModId + ":projection:" + state.StatusId + ":g"
                      + state.Replication.Generation + ":" + kind + ":" + sequence;
        AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
        {
            EventId = eventId,
            DuplicateKey = eventId,
            OwnerModId = TerriasIds.ModId,
            TypeId = "ProjectionBattlePresentation",
            SchemaVersion = 1,
            Kind = kind,
            ActorEntityId = state.StatusId,
            OwnerEntityId = state.OwnerStatusId,
            IssuerPlayerId = state.OwnerPlayerId,
            TargetEntityIds = (targets ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            DisplayText = sourceIdentity ?? "",
            PayloadJson = payload,
            DurationMicroseconds = 1L,
            Persistent = persistent
        });
    }
}

internal sealed class TerriasSpiritReplayEntityPresentationProvider : IAuraReplayEntityPresentationProvider
{
    private static IDisposable? registration;

    internal static void Initialize()
    {
        registration?.Dispose();
        AuraReplayEntityPresentationRuntime.ClearOwner(TerriasIds.ModId);
        registration = AuraReplayEntityPresentationRuntime.Register(
            new TerriasSpiritReplayEntityPresentationProvider());
    }

    public string OwnerModId => TerriasIds.ModId;
    public int SchemaVersion => 1;

    public IReadOnlyList<AuraReplayEntityPresentationItem> Capture(AuraReplayVisibleCaptureContext context)
    {
        return SpiritStateStore.Active()
            .Where(state => state?.Spirit?.Status != null
                            && !string.IsNullOrWhiteSpace(state.StatusId)
                            && !string.IsNullOrWhiteSpace(state.OwnerStatusId))
            .OrderBy(state => state.StatusId, StringComparer.Ordinal)
            .Select(state => new AuraReplayEntityPresentationItem
            {
                EntityId = state.StatusId,
                PresentationMode = AuraReplayEntityPresentationModes.OwnerAttachedProxy,
                OwnerEntityId = state.OwnerStatusId,
                ReferenceHeightPixels = 120,
                HorizontalOverlapQ16 = 21_845,
                SortingOrderOffset = -1,
                HudMode = AuraReplayEntityHudModes.DetachedRightVertical,
                HudScaleQ16 = 47_186,
                HudRotationQ16 = -90 * 65_536,
                AttackFocusTravelPixels = 70,
                InterferenceFocusTravelPixels = 45,
                SupportFocusTravelPixels = 12
            }).ToList();
    }
}
