using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class FieldRuntime
{
    private const string RoundSequenceKey = "TerriasField_RoundSequence";
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        FieldEffectRegistry.Changed -= OnFieldEffectConfigChanged;
        FieldEffectRegistry.Changed += OnFieldEffectConfigChanged;
        FieldApi.Changed -= OnFieldChanged;
        FieldApi.Changed += OnFieldChanged;
        TerriasBattleLifecycleRouter.Register("FieldRuntime", new TerriasBattleLifecycleSubscription
        {
            FightInitializing = context => ResetFightState("FightInitializing"),
            FightOpening = OnFightOpening,
            FightRestarting = context => ResetFightState("FightRestarting"),
            FightEnding = context => ResetFightState("FightEnding"),
            FightEnded = context => ResetFightState("FightEnded")
        });
        TerriasHookRegistry.Before(modConfig, TerriasHookTargets.FightPlayerTurnInit, OnPlayerTurnStart, "FieldRuntime");
        TerriasLog.Info("Field runtime initialized");
    }

    private static void OnFightOpening(ModHookContext context)
    {
        try
        {
            if (!FieldApi.IsAuthoritativeFieldWriter())
            {
                FieldNetworkSync.RequestSnapshot("FightOpening");
                FieldBuffHudRuntime.RequestRefresh("FightOpening.ClientSnapshotPending");
                return;
            }

            var executor = FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
            var fieldChanged = false;
            RunOpeningStep(
                "RelicOpeningEffects",
                () => RelicOpeningEffectService.Apply(executor, "FightOpening"));
            RunOpeningStep(
                "FieldStartCoordinator",
                () => fieldChanged = FieldStartCoordinator.ResolveAndCommit(executor, "FightOpening"));
            if (fieldChanged)
            {
                FieldBuffHudRuntime.RequestRefresh("FightOpening");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Field runtime opening-field coordination failed", ex);
        }
    }

    private static void RunOpeningStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Field opening step failed: " + step, ex);
        }
    }

    private static void OnPlayerTurnStart(ModHookContext context)
    {
        try
        {
            if (!FieldApi.CanResolveFieldEffects())
            {
                FieldNetworkSync.RequestSnapshot("Fight_PlayerTurn.Init");
                FieldBuffHudRuntime.RequestRefresh("Fight_PlayerTurn.Init.ClientSnapshotPending");
                return;
            }

            var executor = FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
            var sequence = CombatVarApi.AddInt(RoundSequenceKey, 1);
            FieldApi.ResolveRoundStart(executor, sequence.ToString(), "Fight_PlayerTurn.Init");
            FieldBuffHudRuntime.RequestRefresh("Fight_PlayerTurn.Init");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Field runtime round-start failed", ex);
        }
    }

    private static void ResetFightState(string source)
    {
        CombatVarApi.SetInt(RoundSequenceKey, 0);
        FieldNetworkSync.ResetFightState();
        FieldApi.ResetFightState(source);
        if (FieldApi.IsAuthoritativeFieldWriter())
        {
            FieldNetworkSync.BroadcastSnapshot(source + ":reset");
        }
        FieldBuffHudRuntime.Close(source);
    }

    private static void OnFieldChanged(FieldBuffSnapshot snapshot)
    {
        FieldBuffHudRuntime.RequestRefresh("FieldApi.Changed");
    }

    private static void OnFieldEffectConfigChanged()
    {
        FieldBuffHudRuntime.RequestRefresh("FieldEffectRegistry.Changed");
    }
}
