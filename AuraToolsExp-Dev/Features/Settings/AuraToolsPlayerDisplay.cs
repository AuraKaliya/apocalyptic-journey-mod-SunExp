using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraMode.Shared;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules.Contracts;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.Settings;

internal readonly struct AuraToolsDisplayOption<T>
{
    internal AuraToolsDisplayOption(T value, string label)
    {
        Value = value;
        Label = label ?? "";
    }

    internal T Value { get; }
    internal string Label { get; }
}

internal static class AuraToolsPlayerDisplay
{
    private static readonly object ContentNameGate = new();
    private static readonly Dictionary<string, string> ContentNameCache =
        new(StringComparer.Ordinal);
    private static long contentNameEpoch = -1;

    internal static string ModuleStatus(AuraToolModuleState state)
    {
        return state.Availability switch
        {
            AuraToolModuleAvailability.Unavailable => "当前不可用",
            AuraToolModuleAvailability.Degraded => "部分功能不可用",
            AuraToolModuleAvailability.Busy => "处理中",
            AuraToolModuleAvailability.RestartRequired => "重启游戏后生效",
            _ => ""
        };
    }

    internal static string OwnerName(string ownerModId)
    {
        var owner = (ownerModId ?? "").Trim();
        if (string.Equals(owner, "Terrias", StringComparison.OrdinalIgnoreCase)) return "Terrias";
        if (string.Equals(owner, AuraToolsIds.ModId, StringComparison.OrdinalIgnoreCase)) return "妙妙工具";
        if (string.Equals(owner, "Witch", StringComparison.OrdinalIgnoreCase)) return "游戏本体";
        return "扩展 MOD";
    }

    internal static string RoleName(string roleId)
    {
        return NonTechnical(RoleCatalog.GetDisplayName(roleId), "未知角色");
    }

    internal static string CardName(string cardId)
    {
        return ContentName(DataType.Card, cardId, "已失效卡牌");
    }

    internal static string PartnerName(string partnerId)
    {
        return ContentName(DataType.Partner, partnerId, "未知使魔");
    }

    internal static string CardPackName(string cardPackId)
    {
        return ContentName(DataType.CardPack, cardPackId, "未知卡包");
    }

    internal static string BuffName(string buffId)
    {
        return ContentName(DataType.Buff, buffId, "已失效状态");
    }

    internal static string RelicName(string relicId)
    {
        return ContentName(DataType.Relic, relicId, "已失效遗物");
    }

    internal static string BlessingName(string blessingId)
    {
        return ContentName(DataType.Bless, blessingId, "已失效祝福");
    }

    internal static string LevelName(string levelId)
    {
        var explicitName = ContentName(DataType.Level, levelId, "");
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
        try
        {
            var identity = AuraToolsContentIdentity.Parse(levelId);
            var level = AuraGameDataHostApi.AcquireSnapshot()
                .Resolve(DataType.Level.ToString(), new[] { identity.ContentId });
            if (level != null)
            {
                if (level.Fields.TryGetValue("Note", out var note)
                    && !string.IsNullOrWhiteSpace(note)
                    && !LooksTechnical(note))
                {
                    var normalized = note.Trim();
                    if (string.Equals(normalized, "Normal", StringComparison.OrdinalIgnoreCase))
                        return "普通战斗";
                    if (normalized.Length <= 40) return normalized;
                }
                if (level.Fields.TryGetValue("EnemyIds", out var enemyIds))
                {
                    var names = (enemyIds ?? "")
                        .Split(new[] { ',', ';', '|', '&' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => ContentName(DataType.Enemy, value.Trim(), ""))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .Take(3)
                        .ToArray();
                    if (names.Length > 0) return string.Join("、", names) + "战";
                }
            }
        }
        catch
        {
        }
        return "战斗记录";
    }

    internal static string ModeName(string modeId)
    {
        var value = (modeId ?? "").Trim();
        if (value.Length == 0 || value.Equals("Normal", StringComparison.OrdinalIgnoreCase)) return "默认模式";
        if (value.Equals("Sublimation", StringComparison.OrdinalIgnoreCase)) return "升华模式";
        if (value.Equals("SlotMachine", StringComparison.OrdinalIgnoreCase)) return "命运轮盘";
        try
        {
            var active = AuraModeRuntime.Current(AuraToolsIds.ModId);
            var activeName = active?.Display?.FallbackName;
            if (active != null
                && string.Equals(active.ModeId, value, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(activeName))
            {
                return activeName!.Trim();
            }
            var registered = AuraModeRuntime.ReadMode(AuraToolsIds.ModId, value);
            var registeredName = registered.Value?.Display?.FallbackName;
            if (registered.Found
                && !string.IsNullOrWhiteSpace(registeredName))
            {
                return registeredName!.Trim();
            }
        }
        catch
        {
        }
        return "自定义模式";
    }

    internal static string AdventureStage(string stage)
    {
        var value = (stage ?? "").Trim().ToLowerInvariant();
        if (value.Contains("battle")) return "战斗";
        if (value.Contains("map")) return "地图";
        if (value.Contains("reward")) return "奖励";
        if (value.Contains("event")) return "事件";
        if (value.Contains("complete") || value.Contains("end")) return "已结束";
        return LooksTechnical(value) ? "冒险中" : (stage ?? "").Trim();
    }

    internal static string BattleResult(string result)
    {
        var value = (result ?? "").Trim();
        if (value.Equals("Win", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Victory", StringComparison.OrdinalIgnoreCase)) return "胜利";
        if (value.Equals("Lose", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Defeat", StringComparison.OrdinalIgnoreCase)) return "失败";
        return value.Length == 0 ? "已结束" : "已结算";
    }

    internal static string AudioTrigger(string kind, string stage)
    {
        var normalized = (kind ?? "").Trim();
        if (normalized.Equals("CareerSelected", StringComparison.OrdinalIgnoreCase)) return "选择角色后";
        if (normalized.Equals("SkillVoice", StringComparison.OrdinalIgnoreCase)) return "技能成功使用后";
        if (normalized.Equals("CardUse", StringComparison.OrdinalIgnoreCase)) return "卡牌成功使用后";
        if (normalized.Equals("BuffApplied", StringComparison.OrdinalIgnoreCase)) return "获得状态后";
        if (normalized.Equals("VocalState", StringComparison.OrdinalIgnoreCase)) return "角色语音状态变化时";
        if (normalized.Equals("LowHealth", StringComparison.OrdinalIgnoreCase)) return "生命值降至阈值时";
        if (normalized.Equals("BattleCompleted", StringComparison.OrdinalIgnoreCase)) return "战斗结束后";
        return "触发条件由内容 MOD 提供";
    }

    internal static string CgKind(string kind)
    {
        if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase)) return "技能 CG";
        if (string.Equals(kind, "cardUse", StringComparison.OrdinalIgnoreCase)) return "卡牌使用 CG";
        if (string.Equals(kind, "feast", StringComparison.OrdinalIgnoreCase)) return "美餐 CG";
        return "CG";
    }

    internal static string SelectionMode(string value)
    {
        if (string.Equals(value, AuraSharedSelectionModes.Random, StringComparison.OrdinalIgnoreCase)) return "随机播放";
        if (string.Equals(value, AuraSharedSelectionModes.Sequential, StringComparison.OrdinalIgnoreCase)) return "依次播放";
        if (string.Equals(value, AuraSharedSelectionModes.All, StringComparison.OrdinalIgnoreCase)) return "全部播放";
        return "优先级最高";
    }

    internal static string PresentationMode(string value)
    {
        return (value ?? "").Trim() switch
        {
            "slide" => "侧边滑入",
            "fullscreenFade" => "全屏淡入淡出",
            "centerFade" => "居中淡入淡出",
            "" => "沿用资源设置",
            _ => "沿用资源设置"
        };
    }

    internal static string FitMode(string value)
    {
        return (value ?? "").Trim() switch
        {
            "cover" => "铺满画面",
            "stretch" => "拉伸填充",
            "contain" => "完整显示",
            "" => "沿用资源设置",
            _ => "沿用资源设置"
        };
    }

    internal static string AlphaMode(string value)
    {
        return string.Equals(value, "blackKey", StringComparison.OrdinalIgnoreCase)
            ? "移除黑色背景"
            : string.IsNullOrWhiteSpace(value) ? "沿用资源设置" : "保留原图";
    }

    internal static string FlashMode(string value)
    {
        return (value ?? "").Trim() switch
        {
            "screen" => "全屏闪光",
            "maskedInvert" => "主体反相闪光",
            "screenBwPulse" => "黑白脉冲",
            "hybridBwPulse" => "混合黑白脉冲",
            "" => "沿用资源设置",
            _ => "关闭闪光"
        };
    }

    internal static string OriginKind(string value)
    {
        return (value ?? "").Trim() switch
        {
            AuraSharedOriginKinds.ContentRegistered => "内容 MOD 自带",
            AuraSharedOriginKinds.ToolRegistered => "妙妙工具自带",
            AuraSharedOriginKinds.ToolDefault => "妙妙工具默认",
            AuraSharedOriginKinds.FoundationDefault => "共享默认",
            AuraSharedOriginKinds.UserManual => "玩家导入",
            _ => "共享资源"
        };
    }

    internal static string ResourceName(string path)
    {
        var normalized = (path ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0) return "未设置资源";
        var file = Path.GetFileName(normalized);
        if (file.StartsWith("content.", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(normalized)?.Replace('\\', '/').TrimEnd('/'));
            return string.IsNullOrWhiteSpace(parent) ? file : parent;
        }
        return string.IsNullOrWhiteSpace(file) ? "已配置资源" : file;
    }

    internal static string TimelineKind(string kind)
    {
        return (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "battle" => "战斗",
            "event" => "事件",
            "reward" => "奖励",
            "shop" => "商店",
            "map" => "地图",
            "event-choice" => "事件选择",
            "card-change" => "卡牌变化",
            "relic-change" => "遗物变化",
            "blessing-change" => "祝福变化",
            "resource-change" => "资源变化",
            "adventure-start" => "启程",
            "adventure-ready" => "准备完成",
            "adventure-end" => "冒险结束",
            "snapshot" => "关键记录",
            _ => "冒险记录"
        };
    }

    private static string ContentName(DataType type, string id, string fallback)
    {
        var raw = (id ?? "").Trim();
        if (raw.Length == 0) return fallback;
        var identity = AuraToolsContentIdentity.Parse(raw);
        try
        {
            var catalog = AuraGameDataHostApi.AcquireSnapshot();
            var epoch = catalog.Version.Epoch;
            var cacheKey = type + "|" + raw;
            lock (ContentNameGate)
            {
                if (contentNameEpoch != epoch)
                {
                    ContentNameCache.Clear();
                    contentNameEpoch = epoch;
                }
                if (ContentNameCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }
            }

            var resolved = catalog.Resolve(type.ToString(), new[] { identity.ContentId });
            if (identity.IsQualified
                && resolved != null
                && !string.Equals(resolved.OwnerModId, identity.OwnerModId, StringComparison.OrdinalIgnoreCase))
            {
                resolved = catalog.GetTable(type.ToString()).FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, identity.ContentId, StringComparison.Ordinal)
                    && string.Equals(candidate.OwnerModId, identity.OwnerModId, StringComparison.OrdinalIgnoreCase));
            }
            var row = resolved?.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (row != null)
            {
                var localized = row.Localize("Name");
                if (!string.IsNullOrWhiteSpace(localized)
                    && !string.Equals(localized, "Name", StringComparison.OrdinalIgnoreCase)
                    && !LooksTechnical(localized))
                {
                    return CacheContentName(cacheKey, localized.Trim());
                }
                if (row.TryGetValue("Name", out var name) && !LooksTechnical(name))
                {
                    return CacheContentName(cacheKey, name.Trim());
                }
            }
        }
        catch
        {
        }
        return fallback;
    }

    private static string CacheContentName(string key, string value)
    {
        lock (ContentNameGate)
        {
            ContentNameCache[key] = value;
        }
        return value;
    }

    private static string NonTechnical(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) || LooksTechnical(value) ? fallback : value.Trim();
    }

    private static bool LooksTechnical(string value)
    {
        var text = (value ?? "").Trim();
        return text.Length == 0
               || text.Contains("_")
               || text.Contains("::");
    }
}
