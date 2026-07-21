using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryMapItemAnimationRuntime
{
    private const string SecondSunMapFallbackAnimation = "AnimationLib/\u707e\u5384\u5148\u5146";
    private const string SaintWunaMapFallbackAnimation = "AnimationLib/\u5931\u5fc3\u9b54\u5973";
    private const string GenericFightMapFallbackAnimation = "AnimationLib/\u707e\u5384\u5148\u5146";

    private static readonly Dictionary<MapTree.Node, AnimationRestore> PendingRestores = new();
    private static readonly Dictionary<string, bool> MapPreviewFrameCache = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "MapItem.Init", PrepareMapItemAnimation);
        RegisterAfter(modConfig, "MapItem.Init", RestoreMapItemAnimation);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "SolarMemoryMapItemAnimation");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "SolarMemoryMapItemAnimation");
    }

    private static void PrepareMapItemAnimation(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node
                || !TryResolveEnemyRow(node, out var enemyId, out var row))
            {
                return;
            }

            var original = DictionaryUtil.Get(row, "Animation");
            var fallbackAnimation = ResolveFallbackAnimation(node, original);
            if (string.IsNullOrWhiteSpace(fallbackAnimation))
            {
                return;
            }

            if (string.Equals(original, fallbackAnimation, StringComparison.Ordinal))
            {
                return;
            }

            PendingRestores[node] = new AnimationRestore(row, original);
            row["Animation"] = fallbackAnimation;
            TerriasLog.Info("[SolarMemoryMapItem] applied safe map animation fallback for "
                + enemyId
                + "; original="
                + original
                + "; fallback="
                + fallbackAnimation
                + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory map item animation prepare failed", ex);
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
            TerriasLog.Debug("[SolarMemoryMapItem] restored map animation fallback.");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory map item animation restore failed", ex);
        }
    }

    private static bool TryResolveEnemyRow(MapTree.Node node, out string enemyId, out IDictionary<string, string> row)
    {
        enemyId = "";
        row = null!;
        if (node.data == null || !string.Equals(DictionaryUtil.Get(node.data, "Type"), "Fight", StringComparison.Ordinal))
        {
            return false;
        }

        var levelId = DictionaryUtil.Get(node.data, "NodeId");
        var level = ConfigRow(DataType.Level, levelId);
        var enemyIds = DictionaryUtil.Get(level, "EnemyIds")
            .Replace(" ", "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (enemyIds.Length == 0)
        {
            return false;
        }

        var bestHp = int.MinValue;
        foreach (var id in enemyIds)
        {
            var candidate = EnemyRow(id);
            if (candidate == null)
            {
                continue;
            }

            var hp = DictionaryUtil.GetInt(candidate, "Hp", 0);
            if (hp <= bestHp)
            {
                continue;
            }

            bestHp = hp;
            enemyId = id;
            row = candidate;
        }

        return row != null;
    }

    private static string ResolveFallbackAnimation(MapTree.Node node, string originalAnimation)
    {
        var levelId = DictionaryUtil.Get(node.data, "NodeId");
        if (IsLevel(levelId, TerriasIds.SolarBossSecondSunLevelId, "level_second_sun_last_day"))
        {
            return SecondSunMapFallbackAnimation;
        }

        if (IsLevel(levelId, TerriasIds.SolarBossSaintWunaLevelId, "level_saint_wuna"))
        {
            return SaintWunaMapFallbackAnimation;
        }

        if (!HasMapPreviewFrames(originalAnimation))
        {
            return GenericFightMapFallbackAnimation;
        }

        return "";
    }

    private static bool HasMapPreviewFrames(string animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath))
        {
            return false;
        }

        if (MapPreviewFrameCache.TryGetValue(animationPath, out var cached))
        {
            return cached;
        }

        var result = false;
        try
        {
            if ((TerriasResourceCache.LoadAll<Texture2D>(animationPath + "/Map")?.Length ?? 0) > 0)
            {
                result = true;
            }
            else
            {
                result = (TerriasResourceCache.LoadAll<Texture2D>(animationPath + "/Idle")?.Length ?? 0) > 0;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SolarMemoryMapItem] map animation probe failed: "
                + animationPath
                + " -> "
                + ex.Message);
            result = false;
        }

        MapPreviewFrameCache[animationPath] = result;
        return result;
    }

    private static bool IsLevel(string actual, string fullId, string shortId)
    {
        return string.Equals(actual, fullId, StringComparison.Ordinal)
            || string.Equals(actual, shortId, StringComparison.Ordinal);
    }

    private static IDictionary<string, string>? EnemyRow(string fullEnemyId)
    {
        var row = ConfigRow(DataType.Enemy, fullEnemyId);
        if (row != null)
        {
            return row;
        }

        const string prefix = "Terrias_terrias_";
        var shortId = fullEnemyId.StartsWith(prefix, StringComparison.Ordinal)
            ? fullEnemyId.Substring(prefix.Length)
            : fullEnemyId;
        return ConfigRow(DataType.Enemy, shortId);
    }

    private static IDictionary<string, string>? ConfigRow(DataType type, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return TerriasConfigIndex.Row(type, id);
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
