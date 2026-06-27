using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryCombatRuntime
{
    private const int EnemyHpMultiplier = 3;
    private const string AppliedKey = "SunExpSolarMemoryHpScaled";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "Enemy.Init", ScaleEnemyHpAfterInit);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Solar memory combat " + message));
    }

    private static void ScaleEnemyHpAfterInit(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
                || context.Target is not Enemy enemy
                || enemy.Status is not StatusManager status
                || AlreadyScaled(enemy))
            {
                return;
            }

            var oldMaxHp = Math.Max(1, enemy.MaxHp);
            var oldCurHp = Math.Max(1, enemy.CurHp);
            var nextMaxHp = checked(oldMaxHp * EnemyHpMultiplier);
            var nextCurHp = Math.Min(nextMaxHp, Math.Max(1, oldCurHp * EnemyHpMultiplier));

            enemy.MaxHp = nextMaxHp;
            enemy.CurHp = nextCurHp;
            status.MaxHp = nextMaxHp;
            status.CurHp = nextCurHp;
            MarkScaled(status);
            RefreshStatusTransfer(enemy, status);

            SunExpLog.Info("[SolarMemoryCombat] scaled enemy HP x"
                + EnemyHpMultiplier
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
        catch (OverflowException ex)
        {
            SunExpLog.Error("Solar memory enemy HP scaling overflowed", ex);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory enemy HP scaling failed", ex);
        }
    }

    private static bool AlreadyScaled(Enemy enemy)
    {
        return enemy.Status is StatusManager status
            && status.dynamicVariables != null
            && status.dynamicVariables.TryGetValue(AppliedKey, out var value)
            && value > 0.5f;
    }

    private static void MarkScaled(StatusManager status)
    {
        status.dynamicVariables ??= new Dictionary<string, float>();
        status.dynamicVariables[AppliedKey] = 1f;
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
            SunExpLog.Warn("Solar memory enemy HP status transfer refresh failed: " + ex.Message);
        }
    }
}
