using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.GameApi;

public static class EnemyCatalogApi
{
    public static SpiritEligibilityResult Inspect(IStatusManager? target, string captureOrigin, bool requireDictionaryVisible = true)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        var result = InspectCore(target, captureOrigin, requireDictionaryVisible);
        SunExpPerformanceCounters.RecordHotspot(
            "Spirit.Catalog.Inspect",
            started,
            "target=" + (target?.InstanceId ?? "<none>")
            + ", eligible=" + result.Eligible
            + ", origin=" + (captureOrigin ?? ""),
            logFirstSample: true);
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
            SunExpLog.Debug("[SpiritCatalog] dictionary lock fallback used: " + ex.Message);
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

        var variantId = FirstNonEmpty(DictionaryUtil.Get(data, "VariantId"), enemyId);
        var description = string.Join("\n", new[]
        {
            Localized(data, "Description1"),
            Localized(data, "Description2")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var snapshot = new CapturedEnemySnapshot
        {
            SpiritUid = Guid.NewGuid().ToString("N"),
            SourceModId = SourceModId(enemyId),
            EnemyId = enemyId,
            VariantId = variantId,
            InstanceId = target.InstanceId ?? "",
            DisplayName = FirstNonEmpty(Localized(data, "Name"), enemyId),
            Description = description,
            AnimationPath = animation,
            DictPath = dictPath,
            IdlePath = idlePath,
            CaptureOrigin = captureOrigin ?? "",
            CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
            BaseHp = DictionaryUtil.GetInt(data, "Hp"),
            BaseAttack = DictionaryUtil.GetInt(data, "Attack"),
            BaseArmor = DictionaryUtil.GetInt(data, "Defend"),
            Rarity = DictionaryUtil.GetInt(data, "Rarity"),
            SourceEnemyCardIds = SplitIds(DictionaryUtil.Get(data, "CardList"))
        };
        return SpiritEligibilityResult.Allow(snapshot);
    }

    private static bool HasSprite(string path, string hotspotName)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        var found = false;
        try
        {
            found = SunExpResourceCache.LoadAll<Sprite>(path, "spirit-catalog")?.Length > 0;
            return found;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[SpiritCatalog] sprite inspection failed for " + path + ": " + ex.Message);
            return false;
        }
        finally
        {
            SunExpPerformanceCounters.RecordHotspot(
                hotspotName,
                started,
                "found=" + found + ", path=" + path,
                logFirstSample: true);
        }
    }

    private static string Localized(IDictionary<string, string> data, string key)
    {
        try
        {
            return data.Localize(key) ?? "";
        }
        catch
        {
            return DictionaryUtil.Get(data, key);
        }
    }

    private static string SourceModId(string enemyId)
    {
        var normalized = enemyId ?? "";
        if (normalized.StartsWith("enemy_", StringComparison.Ordinal))
        {
            return "BaseGame";
        }
        if (normalized.StartsWith("SunExp_sunexp_", StringComparison.Ordinal))
        {
            return "SunExp";
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
}
