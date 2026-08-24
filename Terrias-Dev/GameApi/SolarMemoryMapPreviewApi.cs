using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using Witch.Core;

namespace Terrias.Dll.GameApi;

public sealed class SolarMemoryMapPreviewOverride
{
    private readonly Dictionary<string, string> liveEnemyRow;
    private bool restored;

    internal SolarMemoryMapPreviewOverride(
        Dictionary<string, string> liveEnemyRow,
        string enemyId,
        string originalAnimation,
        string fallbackAnimation)
    {
        this.liveEnemyRow = liveEnemyRow;
        EnemyId = enemyId;
        OriginalAnimation = originalAnimation;
        FallbackAnimation = fallbackAnimation;
    }

    public string EnemyId { get; }
    public string OriginalAnimation { get; }
    public string FallbackAnimation { get; }

    public bool Restore()
    {
        if (restored)
        {
            return false;
        }

        restored = true;
        if (!string.Equals(
                DictionaryUtil.Get(liveEnemyRow, "Animation"),
                FallbackAnimation,
                StringComparison.Ordinal))
        {
            return false;
        }

        liveEnemyRow["Animation"] = OriginalAnimation;
        return true;
    }
}

public static class SolarMemoryMapPreviewApi
{
    private static readonly Dictionary<string, bool> NativePreviewFrameCache = new(StringComparer.Ordinal);

    public static bool TryApplyAnimationOverride(
        MapTree.Node node,
        out SolarMemoryMapPreviewOverride? applied,
        out string reason)
    {
        applied = null;
        reason = "";
        if (node?.data == null
            || !string.Equals(DictionaryUtil.Get(node.data, "Type"), "Fight", StringComparison.Ordinal))
        {
            reason = "not-a-fight-node";
            return false;
        }

        var levelId = DictionaryUtil.Get(node.data, "NodeId");
        var levelRow = LiveRow(DataType.Level, levelId);
        var enemyIds = DictionaryUtil.Get(levelRow, "EnemyIds")
            .Replace(" ", "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (enemyIds.Length == 0)
        {
            reason = "level-has-no-enemy";
            return false;
        }

        Dictionary<string, string>? selectedRow = null;
        var selectedEnemyId = "";
        var bestHp = int.MinValue;
        foreach (var enemyId in enemyIds)
        {
            var candidate = LiveRow(DataType.Enemy, enemyId);
            if (candidate == null)
            {
                continue;
            }

            var hp = DictionaryUtil.GetInt(candidate, "Hp", 0);
            if (selectedRow != null && hp <= bestHp)
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

        var original = DictionaryUtil.Get(selectedRow, "Animation");
        var fallback = SolarMemoryMapPreviewPolicy.ResolveFallback(
            levelId,
            original,
            HasNativePreviewFrames);
        if (fallback.Length == 0)
        {
            reason = HasNativePreviewFrames(original)
                ? "native-preview-already-valid"
                : "no-validated-native-fallback";
            return false;
        }

        selectedRow["Animation"] = fallback;
        applied = new SolarMemoryMapPreviewOverride(
            selectedRow,
            selectedEnemyId,
            original,
            fallback);
        reason = "applied";
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

    private static bool HasNativePreviewFrames(string animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath))
        {
            return false;
        }

        if (NativePreviewFrameCache.TryGetValue(animationPath, out var cached))
        {
            return cached;
        }

        var result = (TerriasResourceCache.LoadAll<Texture2D>(animationPath + "/Map")?.Length ?? 0) > 0
                     || (TerriasResourceCache.LoadAll<Texture2D>(animationPath + "/Idle")?.Length ?? 0) > 0;
        NativePreviewFrameCache[animationPath] = result;
        return result;
    }
}
