using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class MapNodeCardArtRuntime
{
    private static readonly Dictionary<MapItem, MapItemIconBaseline> Baselines =
        new(ReferenceComparer<MapItem>.Instance);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "MapItem.Init", DiagnoseBeforeNativeInit);
        RegisterBefore(modConfig, "MapItem.Init", CaptureMapItemBaseline);
        RegisterAfter(modConfig, "MapItem.Init", ApplyMapNodeCardArt);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "MapNodeCardArt");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "MapNodeCardArt");
    }

    private static void DiagnoseBeforeNativeInit(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node)
            {
                return;
            }

            var data = node.data;
            var id = DictionaryUtil.Get(data, "Id");
            var type = DictionaryUtil.Get(data, "Type");
            var nodeId = DictionaryUtil.Get(data, "NodeId");
            if (!string.Equals(id, SunExpIds.DimensionShopMapId, StringComparison.Ordinal)
                && !string.Equals(id, SunExpIds.DimensionShopMapShortId, StringComparison.Ordinal)
                && !string.Equals(nodeId, SunExpIds.DimensionShopNodeId, StringComparison.Ordinal))
            {
                return;
            }

            var note = DictionaryUtil.Get(data, "Note");
            var level = DictionaryUtil.Get(data, "Level");
            var spec = MapNodeCardArtRegistry.Resolve(data);
            SunExpLog.InfoAlways("[MapNodeDiagnostic] before MapItem.Init; id="
                + ValueOrMissing(id)
                + "; type="
                + ValueOrMissing(type)
                + "; nodeId="
                + ValueOrMissing(nodeId)
                + "; note="
                + ValueOrMissing(note)
                + "; level="
                + ValueOrMissing(level)
                + "; nodeDice="
                + (node.NodeDice != null ? "present" : "missing")
                + "; configuredArt="
                + (spec == null ? "<none>" : ValueOrMissing(spec.TexturePath))
                + "; nativeTexture="
                + NativeTextureExpectation(type, nodeId)
                + FightTextureExpectation(type, nodeId)
                + ".");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[MapNodeDiagnostic] inspection failed before MapItem.Init: " + ex.Message);
        }
    }

    private static string NativeTextureExpectation(string type, string nodeId)
    {
        if (string.Equals(type, "Fight", StringComparison.Ordinal))
        {
            return "enemy.Animation/Map -> enemy.Animation/Idle";
        }

        if (!string.Equals(type, "Build", StringComparison.Ordinal))
        {
            return nodeId == "event_2006" || nodeId == "event_2015"
                ? "Icon/Map/处刑天使"
                : "event icon selected by native lock state";
        }

        return nodeId switch
        {
            "Breaks" => "Icon/Map/建筑  一息安隅",
            "shop" => "Icon/Map/旅行商人",
            "tree" => "Icon/Map/天界赐福",
            "ench" => "Icon/Map/血脉铭刻",
            _ => "Icon/Map/建筑  新的起点 (unknown Build fallback)"
        };
    }

    private static string FightTextureExpectation(string type, string levelId)
    {
        if (!string.Equals(type, "Fight", StringComparison.Ordinal))
        {
            return "";
        }

        var level = SunExpConfigIndex.Row(DataType.Level, levelId);
        if (level == null)
        {
            return "; levelRow=<missing>";
        }

        var enemyIds = DictionaryUtil.Get(level, "EnemyIds")
            .Replace(" ", "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        var selectedEnemy = "";
        var selectedAnimation = "";
        var bestHp = int.MinValue;
        foreach (var enemyId in enemyIds)
        {
            var enemy = SunExpConfigIndex.Row(DataType.Enemy, enemyId);
            if (enemy == null)
            {
                continue;
            }

            var hp = DictionaryUtil.GetInt(enemy, "Hp", 0);
            if (hp <= bestHp)
            {
                continue;
            }

            bestHp = hp;
            selectedEnemy = enemyId;
            selectedAnimation = DictionaryUtil.Get(enemy, "Animation");
        }

        return "; enemyIds="
            + (enemyIds.Length == 0 ? "<none>" : string.Join("|", enemyIds))
            + "; previewEnemy="
            + ValueOrMissing(selectedEnemy)
            + "; animation="
            + ValueOrMissing(selectedAnimation);
    }

    private static string ValueOrMissing(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<missing>" : value;
    }

    private static void CaptureMapItemBaseline(ModHookContext context)
    {
        try
        {
            if (!IsConfiguredNode(context, out _) || context.Target is not MapItem item)
            {
                return;
            }

            if (MapItemApi.TryCaptureIconBaseline(item, out var baseline))
            {
                Baselines[item] = baseline;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[MapNodeCardArt] baseline capture failed: " + ex.Message);
        }
    }

    private static void ApplyMapNodeCardArt(ModHookContext context)
    {
        try
        {
            if (!IsConfiguredNode(context, out var spec) || context.Target is not MapItem item || spec == null)
            {
                return;
            }

            Baselines.TryGetValue(item, out var baseline);
            Baselines.Remove(item);

            var texture = SunExpResourceCache.Load<Texture>(spec.TexturePath, true);
            if (texture == null)
            {
                SunExpLog.Warn("[MapNodeCardArt] texture missing: " + spec.TexturePath);
                return;
            }

            if (!MapItemApi.ApplyTexture(item, texture, spec, baseline))
            {
                SunExpLog.Warn("[MapNodeCardArt] skipped: Front/icon missing for " + spec.TexturePath);
                return;
            }

            SunExpLog.Info("[MapNodeCardArt] applied texture: " + spec.TexturePath);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Map node card art apply failed", ex);
        }
    }

    private static bool IsConfiguredNode(ModHookContext context, out MapNodeCardArtSpec? spec)
    {
        spec = null;
        if (context.Arguments == null
            || context.Arguments.Length == 0
            || context.Arguments[0] is not MapTree.Node node)
        {
            return false;
        }

        spec = MapNodeCardArtRegistry.Resolve(node.data);
        return spec != null;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? left, T? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(T value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
