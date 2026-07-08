using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class EndlessSeaCombatRuntime
{
    private const string AppliedFloorKey = "SunExpEndlessSeaHpScaledFloor";

    public static void Initialize(ModConfig modConfig)
    {
        SunExpBattleLifecycleRouter.Register("EndlessSeaCombat", new SunExpBattleLifecycleSubscription
        {
            FightStarted = ApplyOriginBattleStartEffects
        });
        SunExpStatusLifecycleRouter.Register("EndlessSeaCombat", new SunExpStatusLifecycleSubscription
        {
            AfterEnemyInit = ScaleEnemyHpAfterInit
        });
        RegisterAfter(modConfig, "FightManager.Init", AddEndlessExtraEnemiesAfterFightInit);
        RegisterAfter(modConfig, SunExpHookTargets.FightWinInit, ApplyOriginBattleEndEffects);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "EndlessSeaCombat");
    }

    private static void ScaleEnemyHpAfterInit(ModHookContext context)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun()
                || context.Target is not Enemy enemy
                || enemy.Status is not StatusManager status
                || AlreadyScaled(status))
            {
                return;
            }

            var floor = EndlessSeaModeRuntime.CurrentFloor();
            var multiplier = HpMultiplier(floor);
            var oldMaxHp = Math.Max(1, enemy.MaxHp);
            var oldCurHp = Math.Max(1, enemy.CurHp);
            var nextMaxHp = ScaleHp(oldMaxHp, multiplier);
            var nextCurHp = Math.Min(nextMaxHp, ScaleHp(oldCurHp, multiplier));

            enemy.MaxHp = nextMaxHp;
            enemy.CurHp = nextCurHp;
            status.MaxHp = nextMaxHp;
            status.CurHp = nextCurHp;
            MarkScaled(status, floor);
            RefreshStatusTransfer(enemy, status);
            ApplyEndlessAbyssEnemyModifiers(enemy, floor, "Enemy.Init");

            SunExpLog.Info("[EndlessSeaCombat] scaled enemy HP x"
                + multiplier.ToString("0.###")
                + "; floor="
                + floor
                + "; id="
                + DictionaryUtil.Get(enemy.data, "Id")
                + "; instance="
                + enemy.InstanceId
                + "; max="
                + oldMaxHp
                + "->"
                + nextMaxHp
                + ".");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea enemy HP scaling failed", ex);
        }
    }

    private static void ApplyEndlessAbyssEnemyModifiers(Enemy enemy, int floor, string source)
    {
        var nodeKind = EndlessSeaRewardPlan.CurrentNodeKind();
        EndlessAbyssBlessingService.ApplyOpeningStacks(enemy, source);
        EndlessAbyssRewardService.ApplyEvolutionTraits(enemy, source);
        EndlessAbyssEnemyIntentPoolService.TryAddIntent(enemy, floor, nodeKind, source);
    }

    private static void AddEndlessExtraEnemiesAfterFightInit(ModHookContext context)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                return;
            }

            var floor = EndlessSeaModeRuntime.CurrentFloor();
            var nodeKind = EndlessSeaRewardPlan.CurrentNodeKind();
            EndlessAbyssEnemyInjectionService.TryInjectAfterFightInit(
                floor,
                nodeKind,
                "EndlessSeaCombatRuntime.FightManager.Init");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea endless extra enemy injection failed", ex);
        }
    }

    private static void ApplyOriginBattleStartEffects(ModHookContext context)
    {
        try
        {
            if (EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.InBattle, "Fight_Start.Init");
                EndlessSeaOriginService.ApplyBattleStartEffects("Fight_Start.Init");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea origin battle start failed", ex);
        }
    }

    private static void ApplyOriginBattleEndEffects(ModHookContext context)
    {
        try
        {
            if (EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.Reward, "Fight_Win.Init");
                EndlessSeaOriginService.ApplyBattleEndEffects("Fight_Win.Init");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea origin battle end failed", ex);
        }
    }

    private static double HpMultiplier(int floor)
    {
        var normalized = Math.Max(1, floor);
        var earlyGrowth = Math.Max(0, normalized - 1) * 0.12;
        var lateGrowth = Math.Max(0, normalized - 10) * 0.03;
        var gazeGrowth = Math.Max(0, EndlessAbyssGazeService.CurrentLevel() - 1)
            * EndlessAbyssConfigStore.Current.Gaze.HpGrowthPerGaze;
        return Math.Min(20.0, 1.0 + earlyGrowth + lateGrowth + gazeGrowth);
    }

    private static int ScaleHp(int value, double multiplier)
    {
        var scaled = Math.Round(Math.Max(1, value) * multiplier);
        return (int)Math.Max(1, Math.Min(int.MaxValue, scaled));
    }

    private static bool AlreadyScaled(StatusManager status)
    {
        var floor = EndlessSeaModeRuntime.CurrentFloor();
        return status.dynamicVariables != null
            && status.dynamicVariables.TryGetValue(AppliedFloorKey, out var value)
            && value >= floor;
    }

    private static void MarkScaled(StatusManager status, int floor)
    {
        status.dynamicVariables ??= new Dictionary<string, float>();
        status.dynamicVariables[AppliedFloorKey] = floor;
    }

    private static void RefreshStatusTransfer(Enemy enemy, StatusManager status)
    {
        try
        {
            var manager = FightManager.Instance;
            if (manager == null
                || string.IsNullOrWhiteSpace(enemy.InstanceId)
                || !manager.statusData.ContainsKey(enemy.InstanceId))
            {
                return;
            }

            manager.statusData[enemy.InstanceId] = new StatusDataTransfer(status);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Endless Sea enemy HP status transfer refresh failed: " + ex.Message);
        }
    }
}
