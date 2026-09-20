using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace Terrias.Dll.GameApi;

public static class SolarMemoryMapPreviewApi
{
    private static readonly Dictionary<string, bool> NativePreviewFrameCache = new(StringComparer.Ordinal);

    public static bool TryGetNativePreviewEnemy(
        MapTree.Node node,
        out string levelId,
        out string selectedEnemyId,
        out IDictionary<string, string>? selectedRow,
        out string reason)
    {
        levelId = "";
        selectedEnemyId = "";
        selectedRow = null;
        reason = "";
        if (node?.data == null
            || !string.Equals(DictionaryUtil.Get(node.data, "Type"), "Fight", StringComparison.Ordinal))
        {
            reason = "not-a-fight-node";
            return false;
        }

        levelId = DictionaryUtil.Get(node.data, "NodeId");
        var levelRow = LiveRow(DataType.Level, levelId);
        var enemyIds = DictionaryUtil.Get(levelRow, "EnemyIds")
            .Replace(" ", "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (enemyIds.Length == 0)
        {
            reason = "level-has-no-enemy";
            return false;
        }

        // Native DataConfig wraps the table row in a ReadOnlyDictionary. Mutate
        // its backing table row, not that read-only view or a cached row copy.
        var bestHp = 0;
        foreach (var enemyId in enemyIds)
        {
            var candidate = LiveRow(DataType.Enemy, enemyId);
            if (candidate == null)
            {
                continue;
            }

            var hp = DictionaryUtil.GetInt(candidate, "Hp", 0);
            if (hp <= bestHp)
            {
                continue;
            }

            selectedRow = candidate;
            selectedEnemyId = enemyId;
            bestHp = hp;
        }

        if (selectedRow == null)
        {
            reason = "enemy-row-unavailable";
            return false;
        }

        return true;
    }

    public static void ClearProbeCache()
    {
        NativePreviewFrameCache.Clear();
    }

    private static Dictionary<string, string>? LiveRow(DataType type, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var manager = Singleton<GameConfigManager>.Instance;
        if (manager == null)
        {
            return null;
        }

        foreach (var candidate in TerriasContentIdCompatibility.LookupCandidates(id, "terrias"))
        {
            var row = manager.GetOne(type, candidate);
            if (row != null)
            {
                return row;
            }
        }

        return null;
    }

    public static bool HasNativePreviewFrames(string animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath))
        {
            return false;
        }

        if (NativePreviewFrameCache.TryGetValue(animationPath, out var cached))
        {
            return cached;
        }

        // Deliberately use MapItem.Init's exact generic loader. The shared cache
        // adapts Texture2D to Texture for MOD PNGs, which native MapItem does not.
        // Using that adapter here reports a capability the native consumer lacks.
        var map = TerriasResourceCache.LoadAllNative<Texture2D>(animationPath + "/Map");
        var result = map != null && map.Length > 0 && map[0] != null;
        if (!result)
        {
            var idle = TerriasResourceCache.LoadAllNative<Texture2D>(animationPath + "/Idle");
            result = idle != null && idle.Length > 0 && idle[0] != null;
        }
        NativePreviewFrameCache[animationPath] = result;
        return result;
    }
}
