using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryMapItemAnimationRuntime
{
    private const string SecondSunMapFallbackAnimation = "AnimationLib/\u707e\u5384\u5148\u5146";
    private const string SaintWunaMapFallbackAnimation = "AnimationLib/\u5931\u5fc3\u9b54\u5973";

    private static readonly Dictionary<MapTree.Node, AnimationRestore> PendingRestores = new();

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "MapItem.Init", PrepareMapItemAnimation);
        RegisterAfter(modConfig, "MapItem.Init", RestoreMapItemAnimation);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            SunExpLog.Debug("Solar memory map item animation hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Solar memory map item animation hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Solar memory map item animation hook registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Solar memory map item animation hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void PrepareMapItemAnimation(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node
                || !TryResolvePatch(node, out var enemyId, out var fallbackAnimation))
            {
                return;
            }

            var row = EnemyRow(enemyId);
            if (row == null)
            {
                SunExpLog.Warn("[SolarMemoryMapItem] missing enemy row for map fallback: " + enemyId);
                return;
            }

            var original = DictionaryUtil.Get(row, "Animation");
            if (string.Equals(original, fallbackAnimation, StringComparison.Ordinal))
            {
                return;
            }

            PendingRestores[node] = new AnimationRestore(row, original);
            row["Animation"] = fallbackAnimation;
            SunExpLog.Debug("[SolarMemoryMapItem] applied map animation fallback for " + enemyId + ".");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map item animation prepare failed", ex);
        }
    }

    private static void RestoreMapItemAnimation(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node
                || !PendingRestores.TryGetValue(node, out var restore))
            {
                return;
            }

            restore.Row["Animation"] = restore.Animation;
            PendingRestores.Remove(node);
            SunExpLog.Debug("[SolarMemoryMapItem] restored map animation fallback.");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map item animation restore failed", ex);
        }
    }

    private static bool TryResolvePatch(MapTree.Node node, out string enemyId, out string fallbackAnimation)
    {
        enemyId = "";
        fallbackAnimation = "";
        if (node.data == null || !string.Equals(DictionaryUtil.Get(node.data, "Type"), "Fight", StringComparison.Ordinal))
        {
            return false;
        }

        var levelId = DictionaryUtil.Get(node.data, "NodeId");
        if (string.Equals(levelId, SunExpIds.SolarBossSecondSunLevelId, StringComparison.Ordinal))
        {
            enemyId = SunExpIds.SolarBossSecondSunEnemyId;
            fallbackAnimation = SecondSunMapFallbackAnimation;
            return true;
        }

        if (string.Equals(levelId, SunExpIds.SolarBossSaintWunaLevelId, StringComparison.Ordinal))
        {
            enemyId = SunExpIds.SolarBossSaintWunaEnemyId;
            fallbackAnimation = SaintWunaMapFallbackAnimation;
            return true;
        }

        return false;
    }

    private static IDictionary<string, string>? EnemyRow(string fullEnemyId)
    {
        var row = Singleton<GameConfigManager>.Instance.GetOne(DataType.Enemy, fullEnemyId);
        if (row != null)
        {
            return row;
        }

        const string prefix = "SunExp_sunexp_";
        var shortId = fullEnemyId.StartsWith(prefix, StringComparison.Ordinal)
            ? fullEnemyId.Substring(prefix.Length)
            : fullEnemyId;
        return Singleton<GameConfigManager>.Instance.GetOne(DataType.Enemy, shortId);
    }

    private readonly struct AnimationRestore
    {
        public AnimationRestore(IDictionary<string, string> row, string animation)
        {
            Row = row;
            Animation = animation;
        }

        public IDictionary<string, string> Row { get; }

        public string Animation { get; }
    }
}
