using System;
using System.Collections.Generic;
using AuraDirector.Detour;
using AuraDirector.Shared;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Features.Director;

public static class SunExpDirectorRuntime
{
    private const string OpeningFeatureId = "Battle.OpeningDirector";
    private static AuraDirectorReadyToStartDetourBackend? startGateProvider;

    public static void Initialize(ModConfig modConfig)
    {
        AuraDirectorRuntime.Initialize(modConfig, SunExpIds.ModId);
        AuraDirectorRuntime.RegisterRequestSource(
            SunExpIds.ModId,
            new SunExpBattleOpeningRequestSource());

        startGateProvider ??= new AuraDirectorReadyToStartDetourBackend(
            message => SunExpLog.Warn("[AuraDirector] " + message));
        var result = AuraDirectorRuntime.RegisterStartGateProvider(SunExpIds.ModId, startGateProvider);
        if (result.Supported)
        {
            SunExpLog.Info("AuraDirector local opening enabled: " + result.Code);
        }
        else
        {
            SunExpLog.Warn("AuraDirector local opening unavailable: " + result.Code + "; " + result.Detail);
        }
    }

    private sealed class SunExpBattleOpeningRequestSource : IAuraDirectorRequestSource
    {
        public string SourceId => "BattleOpening.LocalCast.v1";

        public int Priority => 100;

        public AuraDirectorRequest? BuildRequest(object nativeBattleTarget, long battleSessionId)
        {
            if (nativeBattleTarget is not FightManager
                || !AuraFeatureSwitchRuntime.IsEnabled(SunExpIds.ModId, OpeningFeatureId))
            {
                return null;
            }

            var localPlayer = FightPlayer.Instance?.Status as StatusManager;
            var enemies = EnemyManager.Instance?.enemyList;
            if (localPlayer == null || enemies == null || enemies.Count == 0)
            {
                return null;
            }

            var actors = new List<AuraDirectorActorRef>(1 + enemies.Count)
            {
                CreateActor(localPlayer, AuraDirectorActorKind.Player, AuraDirectorActorSide.Friendly)
            };

            foreach (var enemy in enemies)
            {
                if (enemy?.Status is not StatusManager status)
                {
                    continue;
                }

                actors.Add(CreateActor(status, AuraDirectorActorKind.Enemy, AuraDirectorActorSide.Hostile));
            }

            if (actors.Count == 1)
            {
                return null;
            }

            return new AuraDirectorRequest
            {
                ContractId = AuraDirectorProtocol.ContractId,
                SchemaVersion = AuraDirectorProtocol.CurrentSchemaVersion,
                MinimumReaderSchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion,
                OwnerModId = SunExpIds.ModId,
                RequestId = "battle-opening:" + battleSessionId,
                BattleSessionId = battleSessionId,
                Actors = actors,
                Strategy = new AuraDirectorStrategyRef(),
                BlockingMode = AuraDirectorBlockingMode.InputAndProgression,
                FailurePolicy = AuraDirectorFailurePolicy.ContinueWithSilentCue,
                HardTimeoutSeconds = Math.Min(30d, Math.Max(12d, 3d + actors.Count * 0.85d))
            };
        }

        private static AuraDirectorActorRef CreateActor(
            StatusManager status,
            AuraDirectorActorKind kind,
            AuraDirectorActorSide side)
        {
            var actorId = string.IsNullOrWhiteSpace(status.InstanceId)
                ? "status-" + status.GetInstanceID()
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
