using System;
using System.Collections.Generic;
using AuraDirector.Detour;
using AuraDirector.Shared;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Mod;

namespace Terrias.Dll.Features.Director;

public static class TerriasDirectorRuntime
{
    private const string OpeningFeatureId = "Battle.OpeningDirector";
    private static AuraDirectorReadyToStartDetourBackend? startGateProvider;

    public static void Initialize(ModConfig modConfig)
    {
        AuraDirectorRuntime.Initialize(modConfig, TerriasIds.ModId);
        AuraDirectorRuntime.RegisterRequestSource(
            TerriasIds.ModId,
            new TerriasBattleOpeningRequestSource());

        startGateProvider ??= new AuraDirectorReadyToStartDetourBackend(
            message => TerriasLog.Warn("[AuraDirector] " + message));
        var result = AuraDirectorRuntime.RegisterStartGateProvider(TerriasIds.ModId, startGateProvider);
        if (result.Supported)
        {
            TerriasLog.Info("AuraDirector local opening enabled: " + result.Code);
        }
        else
        {
            TerriasLog.Warn("AuraDirector local opening unavailable: " + result.Code + "; " + result.Detail);
        }
    }

    private sealed class TerriasBattleOpeningRequestSource : IAuraDirectorRequestSource
    {
        public string SourceId => "BattleOpening.LocalCast.v1";

        public int Priority => 100;

        public AuraDirectorRequest? BuildRequest(object nativeBattleTarget, long battleSessionId)
        {
            if (nativeBattleTarget is not FightManager
                || !AuraFeatureSwitchRuntime.IsEnabled(TerriasIds.ModId, OpeningFeatureId))
            {
                return null;
            }

            var friendlyStatuses = CompanionFriendlyRosterService.Snapshot(includeControlled: false);
            var enemies = EnemyManager.Instance?.enemyList;
            if (friendlyStatuses.Count == 0 || enemies == null || enemies.Count == 0)
            {
                return null;
            }

            var actors = new List<AuraDirectorActorRef>(friendlyStatuses.Count + enemies.Count);
            foreach (var status in friendlyStatuses)
            {
                if (!StatusApi.IsAlive(status))
                {
                    continue;
                }

                actors.Add(CreateActor(status, AuraDirectorActorKind.Player, AuraDirectorActorSide.Friendly));
            }

            var friendlyCount = actors.Count;

            foreach (var enemy in enemies)
            {
                if (enemy?.Status is not StatusManager status)
                {
                    continue;
                }

                actors.Add(CreateActor(status, AuraDirectorActorKind.Enemy, AuraDirectorActorSide.Hostile));
            }

            var hostileCount = actors.Count - friendlyCount;
            if (friendlyCount == 0 || hostileCount == 0)
            {
                return null;
            }

            TerriasLog.Info("[AuraDirector] opening roster captured; battleSession="
                + battleSessionId
                + ", friendlyCount="
                + friendlyCount
                + ", hostileCount="
                + hostileCount
                + ", actors="
                + string.Join(",", actors.ConvertAll(actor => actor.ActorKey))
                + ".");

            return new AuraDirectorRequest
            {
                ContractId = AuraDirectorProtocol.ContractId,
                SchemaVersion = AuraDirectorProtocol.CurrentSchemaVersion,
                MinimumReaderSchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion,
                OwnerModId = TerriasIds.ModId,
                RequestId = "battle-opening:" + battleSessionId,
                BattleSessionId = battleSessionId,
                Actors = actors,
                Strategy = new AuraDirectorStrategyRef
                {
                    StrategyId = AuraDirectorPlanCompiler.SidePortraitStrategyId,
                    StrategyVersion = AuraDirectorPlanCompiler.SidePortraitStrategyVersion,
                    ProfileId = AuraDirectorPlanCompiler.SidePortraitOpeningProfileId
                },
                BlockingMode = AuraDirectorBlockingMode.InputAndProgression,
                FailurePolicy = AuraDirectorFailurePolicy.ContinueWithSilentCue,
                HardTimeoutSeconds = Math.Min(30d, Math.Max(12d, 3d + actors.Count * 0.85d))
            };
        }

        private static AuraDirectorActorRef CreateActor(
            IStatusManager status,
            AuraDirectorActorKind kind,
            AuraDirectorActorSide side)
        {
            var actorId = string.IsNullOrWhiteSpace(status.InstanceId)
                ? "status-" + status.GetHashCode()
                : status.InstanceId;
            return new AuraDirectorActorRef
            {
                ActorKey = actorId,
                ActorKind = kind,
                Side = side,
                OwnerPlayerId = side == AuraDirectorActorSide.Friendly ? actorId : "",
                ContentOwnerModId = "Witch",
                ContentId = actorId,
                Resource = new AuraDirectorResourceRef
                {
                    ProviderId = AuraDirectorRuntime.NativeBattleSpriteProviderId,
                    OwnerModId = "Witch",
                    ResourceId = actorId
                }
            };
        }
    }
}
