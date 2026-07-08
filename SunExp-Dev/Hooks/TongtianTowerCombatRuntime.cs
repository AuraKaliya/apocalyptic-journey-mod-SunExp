using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerCombatRuntime
{
    private const string AppliedFloorKey = "SunExpTongtianTowerHpScaledFloor";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "Enemy.Init", ScaleEnemyHpAfterInit);
        RegisterAfter(modConfig, "FightManager.Init", AddEndlessExtraEnemiesAfterFightInit);
        RegisterAfter(modConfig, "Fight_Start.Init", ApplyOriginBattleStartEffects);
        RegisterAfter(modConfig, "Fight_Win.Init", ApplyOriginBattleEndEffects);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian tower combat " + message));
    }

    private static void ScaleEnemyHpAfterInit(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun()
                || context.Target is not Enemy enemy
                || enemy.Status is not StatusManager status
                || AlreadyScaled(status))
            {
                return;
            }

            var floor = TongtianTowerModeRuntime.CurrentFloor();
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

            SunExpLog.Info("[TongtianTowerCombat] scaled enemy HP x"
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
            SunExpLog.Error("Tongtian tower enemy HP scaling failed", ex);
        }
    }

    private static void ApplyEndlessAbyssEnemyModifiers(Enemy enemy, int floor, string source)
    {
        var nodeKind = TongtianTowerRewardPlan.CurrentNodeKind();
        EndlessAbyssBlessingService.ApplyOpeningStacks(enemy, source);
        EndlessAbyssRewardService.ApplyEvolutionTraits(enemy, source);
        EndlessAbyssEnemyIntentPoolService.TryAddIntent(enemy, floor, nodeKind, source);
    }

    private static void AddEndlessExtraEnemiesAfterFightInit(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                return;
            }

            var floor = TongtianTowerModeRuntime.CurrentFloor();
            var nodeKind = TongtianTowerRewardPlan.CurrentNodeKind();
            EndlessAbyssEnemyInjectionService.TryInjectAfterFightInit(
                floor,
                nodeKind,
                "TongtianTowerCombatRuntime.FightManager.Init");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower endless extra enemy injection failed", ex);
        }
    }

    private static void ApplyOriginBattleStartEffects(ModHookContext context)
    {
        try
        {
            if (TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                TongtianTowerRunStateStore.MarkPhase(TongtianTowerRunPhase.InBattle, "Fight_Start.Init");
                TongtianTowerOriginService.ApplyBattleStartEffects("Fight_Start.Init");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower origin battle start failed", ex);
        }
    }

    private static void ApplyOriginBattleEndEffects(ModHookContext context)
    {
        try
        {
            if (TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                TongtianTowerRunStateStore.MarkPhase(TongtianTowerRunPhase.Reward, "Fight_Win.Init");
                TongtianTowerOriginService.ApplyBattleEndEffects("Fight_Win.Init");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower origin battle end failed", ex);
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
        var floor = TongtianTowerModeRuntime.CurrentFloor();
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
            SunExpLog.Warn("Tongtian tower enemy HP status transfer refresh failed: " + ex.Message);
        }
    }
}
