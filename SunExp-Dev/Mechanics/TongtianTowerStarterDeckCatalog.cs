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
        string preview,
        IReadOnlyList<string> cardIds)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Description = description;
        Preview = preview;
        CardIds = cardIds;
    }

    public string Id { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Description { get; }

    public string Preview { get; }

    public IReadOnlyList<string> CardIds { get; }
}

public static class TongtianTowerStarterDeckCatalog
{
    public const int DeckSize = 11;

    private static readonly TongtianTowerStarterDeckProfile[] ProfilesInternal =
    {
        new(
            "steady",
            "日轮稳阵",
            "防守、续航、低费启动",
            "适合第一次挑战通天之塔。前几层优先保证手牌循环与护盾，容错最高。",
            "火花 x2 / 晨光壁垒 / 太阳归返 / 污秽净除",
            new[]
            {
                "spark",
                "spark",
                "radiant_flame_slash",
                "ember_cloak_card",
                "morning_light_bulwark",
                "solar_prayer",
                "solar_return",
                "solar_origin_core",
                "scorching_canopy_card",
                "draw_flame",
                "impurity_purge"
            }),
        new(
            "burst",
            "烬冠强袭",
            "爆发、攻击、快速清场",
            "用高密度攻击压低战斗回合数。奖励牌会带焚毁限制，爆发套需要更主动补牌。",
            "火花 / 燃星咒 / 灼流回收 / 烈冠崩坠",
            new[]
            {
                "spark",
                "radiant_flame_slash",
                "draw_flame",
                "burning_star_hex",
                "burning_calamity",
                "scorching_flow_reclaim",
                "solar_ignition",
                "ember_tower",
                "burning_crown_oath",
                "blazing_crown_collapse",
                "solar_coronation"
            }),
        new(
            "operate",
            "星谱调度",
            "运营、调律、长期成长",
            "开局节奏稍慢，但更适合处理高层资源压力。保留星谱与百变作为长期运营轴。",
            "日相调律 / 炎轮复归 / 星图 / 空白星谱 / 百变",
            new[]
            {
                "spark",
                "ember_cloak_card",
                "solar_prayer",
                "solar_phase_tuning",
                "solar_return",
                "solar_origin_core",
                "flamewheel_recurrence",
                "gathered_flame_shield",
                "star_map",
                "blank_star_score",
                "polymorph"
            })
    };

    public static IReadOnlyList<TongtianTowerStarterDeckProfile> Profiles => ProfilesInternal;

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

        var row = SunExpConfigIndex.Row(DataType.Card, cardId);
        return row == null || !string.Equals(DictionaryUtil.Get(row, "Id"), cardId, StringComparison.Ordinal);
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
}
