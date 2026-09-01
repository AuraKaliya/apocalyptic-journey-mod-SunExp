using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.GameApi;

public static class EnemyCatalogApi
{
    public static SpiritEligibilityResult Inspect(IStatusManager? target, string captureOrigin, bool requireDictionaryVisible = true)
    {
        var started = TerriasPerformanceCounters.Timestamp();
        var result = InspectCore(target, captureOrigin, requireDictionaryVisible);
        TerriasPerformanceCounters.RecordHotspot(
            "Spirit.Catalog.Inspect",
            started,
            "target=" + (target?.InstanceId ?? "<none>")
            + ", eligible=" + result.Eligible
            + ", origin=" + (captureOrigin ?? ""),
            logFirstSample: true);
        return result;
    }

    public static SpiritEligibilityResult InspectConfiguredProfile(
        string sourceModId,
        string enemyId,
        string variantId,
        string captureOrigin)
    {
        var normalizedEnemyId = (enemyId ?? "").Trim().TrimStart('*');
        if (normalizedEnemyId.Length == 0 || normalizedEnemyId == "*")
        {
            return SpiritEligibilityResult.Reject("初始精灵 Profile 缺少具体敌人标识。");
        }

        var resolved = AuraGameDataHostApi.Resolve(
            DataType.Enemy,
            ConfiguredEnemyLookupCandidates(sourceModId, normalizedEnemyId).ToArray());
        if (resolved == null)
        {
            return SpiritEligibilityResult.Reject("初始精灵 Profile 无法解析敌人配置：" + normalizedEnemyId);
        }

        var data = resolved.Fields.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var resolvedEnemyId = FirstNonEmpty(DictionaryUtil.Get(data, "Id"), resolved.Id);

        var animation = DictionaryUtil.Get(data, "Animation").TrimEnd('/');
        if (animation.Length == 0)
        {
            return SpiritEligibilityResult.Reject("初始精灵 Profile 没有动画资源：" + normalizedEnemyId);
        }

        var normalizedVariantId = (variantId ?? "").Trim();
        if (normalizedVariantId.Length == 0 || normalizedVariantId == "*")
        {
            normalizedVariantId = FirstNonEmpty(DictionaryUtil.Get(data, "VariantId"), resolvedEnemyId);
        }

        return SpiritEligibilityResult.Allow(BuildSnapshot(
            data,
            resolvedEnemyId,
            normalizedVariantId,
            string.IsNullOrWhiteSpace(sourceModId) ? SourceModId(normalizedEnemyId) : sourceModId.Trim(),
            "",
            captureOrigin));
    }

    public static IReadOnlyList<string> ConfiguredEnemyLookupCandidates(
        string sourceModId,
        string enemyId)
    {
        var source = (sourceModId ?? "").Trim();
        var value = (enemyId ?? "").Trim().TrimStart('*');
        if (value.Length == 0 || value == "*")
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        if (string.Equals(source, "base-game", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "BaseGame", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "base_game", StringComparison.OrdinalIgnoreCase))
        {
            var local = value.StartsWith("enemy_", StringComparison.OrdinalIgnoreCase)
                ? value.Substring("enemy_".Length)
                : value;
            AddCandidate(result, "enemy_" + local);
            AddCandidate(result, value);
            AddCandidate(result, local);
            return result;
        }

        if (string.Equals(source, "terrias", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "Terrias", StringComparison.OrdinalIgnoreCase)
            || TerriasContentIdCompatibility.HasKnownPrefix(value))
        {
            foreach (var candidate in TerriasContentIdCompatibility.LookupCandidates(value, "terrias")
                         .OrderByDescending(candidate => candidate.StartsWith(
                             TerriasContentIdCompatibility.CurrentMainPrefix,
                             StringComparison.OrdinalIgnoreCase)))
            {
                AddCandidate(result, candidate);
            }
            return result;
        }

        if (source.Length > 0)
        {
            AddCandidate(result, source.TrimEnd('_') + "_" + value);
        }
        AddCandidate(result, value);
        return result;
    }

    private static SpiritEligibilityResult InspectCore(IStatusManager? target, string captureOrigin, bool requireDictionaryVisible)
    {
        if (target?.fatherObject is not Enemy enemy || enemy.dataConfig?.data == null)
        {
            return SpiritEligibilityResult.Reject("目标不是敌人。");
        }

        if (target.CurHp <= 0 || target.state == IStatusManager.State.Dead)
        {
            return SpiritEligibilityResult.Reject("目标已经离场。");
        }

        var data = enemy.dataConfig.data;
        var enemyId = DictionaryUtil.Get(data, "Id").Replace("*", "").Trim();
        if (enemyId.Length == 0)
        {
            return SpiritEligibilityResult.Reject("目标缺少敌人标识。");
        }

        try
        {
            if (requireDictionaryVisible && Singleton<GameRuntimeData>.Instance?.IsLocked(enemyId) == true)
            {
                return SpiritEligibilityResult.Reject("目标尚未在图鉴中解锁。");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[SpiritCatalog] dictionary lock fallback used: " + ex.Message);
            return SpiritEligibilityResult.Reject("暂时无法确认目标的图鉴状态。");
        }

        var animation = DictionaryUtil.Get(data, "Animation").TrimEnd('/');
        if (animation.Length == 0)
        {
            return SpiritEligibilityResult.Reject("目标没有动画资源。");
        }

        var dictPath = animation + "/Dict";
        var idlePath = animation + "/Idle";
        if (!HasSprite(dictPath, "Spirit.Catalog.DictProbe"))
        {
            return SpiritEligibilityResult.Reject("目标没有可用的图鉴图片。");
        }

        if (!HasSprite(idlePath, "Spirit.Catalog.IdleProbe"))
        {
            return SpiritEligibilityResult.Reject("目标没有可用的待机动画。");
        }

        return SpiritEligibilityResult.Allow(BuildSnapshot(
            data,
            enemyId,
            FirstNonEmpty(DictionaryUtil.Get(data, "VariantId"), enemyId),
            SourceModId(enemyId),
            target.InstanceId ?? "",
            captureOrigin));
    }

    private static CapturedEnemySnapshot BuildSnapshot(
        IDictionary<string, string> data,
        string enemyId,
        string variantId,
        string sourceModId,
        string instanceId,
        string captureOrigin)
    {
        var animation = DictionaryUtil.Get(data, "Animation").TrimEnd('/');
        var description = string.Join("\n", new[]
        {
            DictionaryUtil.Get(data, "Description1"),
            DictionaryUtil.Get(data, "Description2")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new CapturedEnemySnapshot
        {
            SourceModId = sourceModId ?? "",
            EnemyId = enemyId ?? "",
            VariantId = variantId ?? "",
            InstanceId = instanceId ?? "",
            DisplayName = FirstNonEmpty(DictionaryUtil.Get(data, "Name"), enemyId ?? ""),
            Description = description,
            AnimationPath = animation,
            DictPath = animation + "/Dict",
            IdlePath = animation + "/Idle",
            CaptureOrigin = captureOrigin ?? "",
            CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
            BaseHp = DictionaryUtil.GetInt(data, "Hp"),
            BaseAttack = DictionaryUtil.GetInt(data, "Attack"),
            BaseArmor = DictionaryUtil.GetInt(data, "Defend"),
            Rarity = DictionaryUtil.GetInt(data, "Rarity"),
            SourceEnemyCardIds = SplitIds(DictionaryUtil.Get(data, "CardList"))
        };
    }

    private static bool HasSprite(string path, string hotspotName)
    {
        var started = TerriasPerformanceCounters.Timestamp();
        var found = false;
        try
        {
            found = TerriasResourceCache.LoadAll<Sprite>(path, "spirit-catalog")?.Length > 0;
            return found;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[SpiritCatalog] sprite inspection failed for " + path + ": " + ex.Message);
            return false;
        }
        finally
        {
            TerriasPerformanceCounters.RecordHotspot(
                hotspotName,
                started,
                "found=" + found + ", path=" + path,
                logFirstSample: true);
        }
    }

    private static string SourceModId(string enemyId)
    {
        var normalized = enemyId ?? "";
        if (normalized.StartsWith("enemy_", StringComparison.Ordinal))
        {
            return "BaseGame";
        }
        if (TerriasContentIdCompatibility.HasKnownPrefix(normalized))
        {
            return "Terrias";
        }

        var separator = normalized.IndexOf('_');
        return separator > 0 ? normalized.Substring(0, separator) : "BaseGame";
    }

    private static List<string> SplitIds(string value)
    {
        return (value ?? "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static void AddCandidate(ICollection<string> values, string candidate)
    {
        var normalized = (candidate ?? "").Trim();
        if (normalized.Length > 0 && !values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(normalized);
        }
    }
}
