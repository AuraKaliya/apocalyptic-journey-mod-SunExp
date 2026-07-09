using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
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
            FightInitialized = OnFightInitialized,
            FightEnding = context => ResetFightState("FightEnding"),
            FightEnded = context => ResetFightState("FightEnded")
        });
        SunExpHookRegistry.Before(modConfig, SunExpHookTargets.FightPlayerTurnInit, OnPlayerTurnStart, "FieldRuntime");
        SunExpLog.Info("Field runtime initialized");
    }

    private static void OnFightInitialized(ModHookContext context)
    {
        try
        {
            var executor = FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
            var applied = FieldStartSourceService.ApplyFightStartSources(executor, "FightInitialized");
            if (applied > 0)
            {
                FieldBuffHudRuntime.RequestRefresh("FightInitialized");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Field runtime start-source replay failed", ex);
        }
    }

    private static void OnPlayerTurnStart(ModHookContext context)
    {
        try
        {
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
        FieldApi.ClearAllFields(source);
        FieldBuffHudRuntime.Close(source);
    }

    private static void OnFieldChanged(FieldBuffSnapshot snapshot)
    {
        FieldBuffHudRuntime.RequestRefresh("FieldApi.Changed");
    }
}
