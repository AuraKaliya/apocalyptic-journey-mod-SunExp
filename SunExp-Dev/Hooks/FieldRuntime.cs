using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Network;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class FieldRuntime
{
    private const string RoundSequenceKey = "SunExpField_RoundSequence";
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        FieldApi.Changed -= OnFieldChanged;
        FieldApi.Changed += OnFieldChanged;
        SunExpBattleLifecycleRouter.Register("FieldRuntime", new SunExpBattleLifecycleSubscription
        {
            FightInitializing = context => ResetFightState("FightInitializing"),
            FightOpening = OnFightOpening,
            FightEnding = context => ResetFightState("FightEnding"),
            FightEnded = context => ResetFightState("FightEnded")
        });
        SunExpHookRegistry.Before(modConfig, SunExpHookTargets.FightPlayerTurnInit, OnPlayerTurnStart, "FieldRuntime");
        SunExpLog.Info("Field runtime initialized");
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
            SunExpLog.Error("Field runtime opening-field coordination failed", ex);
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
            SunExpLog.Error("Field opening step failed: " + step, ex);
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
            SunExpLog.Error("Field runtime round-start failed", ex);
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
}
