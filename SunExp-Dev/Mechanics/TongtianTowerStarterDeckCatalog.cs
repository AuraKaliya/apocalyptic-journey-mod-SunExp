using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class TongtianTowerStarterDeckProfile
{
    public TongtianTowerStarterDeckProfile(
        string id,
        string title,
        string subtitle,
        string description,
        IReadOnlyList<string> themeCardIds,
        string? requiredPackId = null,
        string? coverPackId = null)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Description = description;
        ThemeCardIds = themeCardIds;
        RequiredPackId = requiredPackId ?? "";
        CoverPackId = !string.IsNullOrWhiteSpace(coverPackId) ? coverPackId! : RequiredPackId;
        CardIds = TongtianTowerStarterDeckCatalog.FixedCardIds.Concat(themeCardIds).ToList();
        Preview = TongtianTowerStarterDeckCatalog.BuildPreview(themeCardIds);
    }

    public string Id { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Description { get; }

    public string Preview { get; }

    public string RequiredPackId { get; }

    public string CoverPackId { get; }

    public IReadOnlyList<string> ThemeCardIds { get; }

    public IReadOnlyList<string> CardIds { get; }
}

public static class TongtianTowerStarterDeckCatalog
{
    public const int FixedDeckSize = 11;
    public const int ThemeDeckSize = 4;
    public const int DeckSize = FixedDeckSize + ThemeDeckSize;

    public static readonly IReadOnlyList<string> FixedCardIds = new[]
    {
        "card_1",
        "card_1",
        "card_1",
        "card_2",
        "card_2",
        "card_2",
        "card_2",
        "card_3",
        "card_3",
        "card_4",
        "burningcard_1"
    };

    private static readonly TongtianTowerStarterDeckProfile[] ProfilesInternal =
    {
        new(
            "academy_required",
            "学院必修",
            "默认主题卡包",
            "稳定的学院基础配置，适合无尽之渊第一层起步。",
            new[] { "card_14", "healcard_3", "healcard_1", "healcard_1" }),
        new(
            "church_defense_tactics",
            "教廷防卫技战术",
            "反击与续航",
            "围绕蓄势、恢复和低费攻击建立早期节奏。",
            new[] { "counterattackcard_8", "counterattackcard_2", "counterattackcard_4", "card_14" },
            "cardpack_3"),
        new(
            "chrono_journey",
            "时空奇旅",
            "焚毁与时之笼",
            "通过瞬闪、封装和残响快速处理手牌与防御。",
            new[] { "timekeeper_1", "timekeeper_12", "timekeeper_13", "luckycard_9" },
            "cardpack_4"),
        new(
            "black_witchcraft",
            "黑色巫术",
            "复还与结晶",
            "保留复还启动件，同时补入基础护盾。",
            new[] { "ReturnAgain_9", "ReturnAgain_2", "ReturnAgain_2", "card_6" },
            "cardpack_6"),
        new(
            "serpent_from_shadows",
            "蛇自阴影中袭来",
            "生命换取爆发",
            "用绯咏弦奏和瞳映岐影推动重生与复制。",
            new[] { "Crowdfundingcard_49", "Crowdfundingcard_7", "healcard_1", "healcard_1" },
            "cardpack_7"),
        new(
            "aldrin_oracles",
            "奥尔德林诸神谕",
            "唤神与终结",
            "补入圣裁、屏障和高阶终结件。",
            new[] { "combo_1", "combo_8", "Crowdfundingcard_16", "card_8" },
            "cardpack_9"),
        new(
            "chaos_ensemble",
            "混沌乐团",
            "混沌与幸运",
            "用终曲、猫猫和雷鸣加护打开高风险运营。",
            new[] { "Crowdfundingcard_43", "Crowdfundingcard_41", "luckycard_8", "luckycard_8" },
            "cardpack_12"),
        new(
            "bloodfiend_lineage",
            "血鬼谱系综述",
            "流血与升华",
            "通过流血、自损和禁果换取成长空间。",
            new[] { "blood_9", "blood_11", "healcard_8", "perceivecard_5" },
            "cardpack_14"),
        new(
            "origin_of_elements",
            "万物元素之始",
            "元素累积",
            "围绕元素、防御和多段法术建立中期爆发。",
            new[] { "elementscard_1", "elementscard_6", "elementscard_13", "card_15" },
            "cardpack_15"),
        new(
            "spell_sequence",
            "法术序列",
            "充能与复还",
            "用冥想、咒闪刃、幻梦和升华维持资源循环。",
            new[] { "universalcard_1", "SpellCard_8", "universalcard_20", "perceivecard_5" },
            "cardpack_18"),
        new(
            "supreme_rituals",
            "超位仪式",
            "仪式启动",
            "用引思、唤醒仪式和魔能之心寻找超位路线。",
            new[] { "ritualcard_3", "ritualcard_15", "ritualcard_1", "card_7" },
            "cardpack_19")
    };

    public static IReadOnlyList<TongtianTowerStarterDeckProfile> Profiles => ProfilesInternal;

    public static IReadOnlyList<TongtianTowerStarterDeckProfile> AvailableProfiles()
    {
        var enabledPacks = EnabledCardPacks();
        var result = ProfilesInternal
            .Where(profile => IsAvailable(profile, enabledPacks))
            .ToList();
        SunExpLog.Info("[TongtianStarterDeck] available profiles="
            + result.Count
            + "; selectedPacks="
            + string.Join("|", enabledPacks.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)));
        return result;
    }

    public static TongtianTowerStarterDeckProfile? ProfileById(string id)
    {
        return ProfilesInternal.FirstOrDefault(profile =>
            string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> AllCardIds()
    {
        return ProfilesInternal
            .SelectMany(profile => profile.CardIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsInvalidCardId(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId) || cardId.StartsWith("*", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return data == null || string.IsNullOrWhiteSpace(DictionaryUtil.Get(data, "Id"));
        }
        catch
        {
            return true;
        }
    }

    public static string CardDisplayName(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var localizedName = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != "Name")
            {
                return localizedName;
            }

            var rawName = DictionaryUtil.Get(data, "Name");
            return string.IsNullOrWhiteSpace(rawName) ? cardId : rawName;
        }
        catch
        {
            return cardId;
        }
    }

    public static string BuildPreview(IEnumerable<string> themeCardIds)
    {
        return string.Join(" / ", themeCardIds.Select(CardDisplayName));
    }

    private static bool IsAvailable(TongtianTowerStarterDeckProfile profile, HashSet<string> enabledPacks)
    {
        if (profile.CardIds.Count != DeckSize)
        {
            SunExpLog.Warn("[TongtianStarterDeck] hidden profile "
                + profile.Id
                + ": expected "
                + DeckSize
                + " cards but got "
                + profile.CardIds.Count
                + ".");
            return false;
        }

        var invalidCards = profile.CardIds
            .Where(IsInvalidCardId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (invalidCards.Count > 0)
        {
            SunExpLog.Warn("[TongtianStarterDeck] hidden profile "
                + profile.Id
                + ": invalidCards="
                + string.Join("|", invalidCards));
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.RequiredPackId))
        {
            SunExpLog.Info("[TongtianStarterDeck] visible profile "
                + profile.Id
                + ": default theme.");
            return true;
        }

        var available = enabledPacks.Contains(profile.RequiredPackId);
        SunExpLog.Info("[TongtianStarterDeck] "
            + (available ? "visible" : "hidden")
            + " profile "
            + profile.Id
            + ": requiredPack="
            + profile.RequiredPackId
            + "; selected="
            + available);
        return available;
    }

    private static HashSet<string> EnabledCardPacks()
    {
        try
        {
            var packs = Singleton<GameRuntimeData>.Instance.UseCardPack;
            return new HashSet<string>(
                packs.Where(pack => !string.IsNullOrWhiteSpace(pack)),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianStarterDeck] failed to read selected card packs: " + ex.Message);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
