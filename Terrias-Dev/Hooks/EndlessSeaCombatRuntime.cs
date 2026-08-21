using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class EndlessSeaCombatRuntime
{
    private const string AppliedFloorKey = "TerriasEndlessSeaHpScaledFloor";
    private static IDisposable? statusRegistration;

    public static void Initialize(ModConfig modConfig)
    {
        TerriasBattleLifecycleRouter.Register("EndlessSeaCombat", new TerriasBattleLifecycleSubscription
        {
            BattleInitializing = _ => ActivateStatusHandler(),
            BattleManagerInitialized = AddEndlessExtraEnemiesAfterFightInit,
            BattleMaterialized = MarkEndlessBattleStarted,
            BattleRestarting = _ => ReleaseStatusHandler(),
            BattleSettling = _ => ReleaseStatusHandler(),
            OutcomeEntering = context =>
            {
                if (context.Outcome == AuraShared.Core.AuraBattleOutcome.Win)
                {
                    MarkEndlessBattleWon(context.NativeContext);
                }
            }
        });
    }

    private static void ActivateStatusHandler()
    {
        ReleaseStatusHandler();
        if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
        {
            return;
        }

        statusRegistration = TerriasStatusLifecycleRouter.Register("EndlessSeaCombat", new TerriasStatusLifecycleSubscription
        {
            AfterEnemyInit = ScaleEnemyAfterInit
        });
    }

    private static void ReleaseStatusHandler()
    {
        statusRegistration?.Dispose();
        statusRegistration = null;
    }

    private static void ScaleEnemyAfterInit(ModHookContext context)
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
            var nodeKind = EndlessSeaRewardPlan.CurrentNodeKind();
            var scaling = EndlessAbyssEnemyScalingService.Calculate(
                floor,
                EndlessAbyssGazeService.CurrentLevel(),
                nodeKind,
                EndlessAbyssConfigStore.Current.EnemyScaling);
            var oldMaxHp = Math.Max(1, enemy.MaxHp);
            var oldCurHp = Math.Max(1, enemy.CurHp);
            var oldAttack = Math.Max(0, enemy.Attack);
            var nextMaxHp = ScaleValue(oldMaxHp, scaling.HpMultiplier, 1);
            var nextCurHp = Math.Min(nextMaxHp, ScaleValue(oldCurHp, scaling.HpMultiplier, 1));
            var nextAttack = ScaleValue(oldAttack, scaling.AttackMultiplier, 0);

            enemy.MaxHp = nextMaxHp;
            enemy.CurHp = nextCurHp;
            enemy.Attack = nextAttack;
            status.MaxHp = nextMaxHp;
            status.CurHp = nextCurHp;
            MarkScaled(status, floor);
            RefreshStatusTransfer(enemy, status);
            ApplyEndlessAbyssEnemyModifiers(enemy, floor, "Enemy.Init");

            TerriasLog.Info("[EndlessSeaCombat] scaled enemy HP x"
                + scaling.HpMultiplier.ToString("0.###")
                + ", ATK x"
                + scaling.AttackMultiplier.ToString("0.###")
                + "; floor="
                + floor
                + "; node="
                + nodeKind
                + "; id="
                + DictionaryUtil.Get(enemy.data, "Id")
                + "; instance="
                + enemy.InstanceId
                + "; max="
                + oldMaxHp
                + "->"
                + nextMaxHp
                + "; attack="
                + oldAttack
                + "->"
                + nextAttack
                + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea enemy scaling failed", ex);
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
            TerriasLog.Error("Endless Sea endless extra enemy injection failed", ex);
        }
    }

    private static void MarkEndlessBattleStarted(ModHookContext context)
    {
        try
        {
            if (EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.InBattle, "BattleMaterialized");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea battle start phase failed", ex);
        }
    }

    private static void MarkEndlessBattleWon(ModHookContext context)
    {
        try
        {
            if (EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.Reward, "OutcomeEntering.Win");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea battle win phase failed", ex);
        }
    }

    private static int ScaleValue(int value, double multiplier, int minimum)
    {
        var scaled = Math.Round(Math.Max(minimum, value) * multiplier);
        return (int)Math.Max(minimum, Math.Min(int.MaxValue, scaled));
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
            TerriasLog.Warn("Endless Sea enemy status transfer refresh failed: " + ex.Message);
        }
    }
}
