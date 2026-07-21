using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public sealed class ConstellationLocalizedText
{
    public ConstellationLocalizedText(string zhHans, string zhHant, string english, string japanese)
    {
        ZhHans = zhHans ?? "";
        ZhHant = zhHant ?? "";
        English = english ?? "";
        Japanese = japanese ?? "";
    }

    public string ZhHans { get; }
    public string ZhHant { get; }
    public string English { get; }
    public string Japanese { get; }
}

public sealed class ConstellationTierDefinition
{
    public ConstellationTierDefinition(
        int level,
        ConstellationLocalizedText description,
        int oneTimeExtraordinary = 0)
    {
        Level = level;
        Description = description;
        OneTimeExtraordinary = Math.Max(0, oneTimeExtraordinary);
    }

    public int Level { get; }
    public ConstellationLocalizedText Description { get; }
    public int OneTimeExtraordinary { get; }
}

public sealed class ConstellationPoolDefinition
{
    public ConstellationPoolDefinition(
        string id,
        ConstellationLocalizedText name,
        ConstellationLocalizedText buffName,
        IReadOnlyList<ConstellationTierDefinition> tiers)
    {
        Id = id ?? "";
        Name = name;
        BuffName = buffName;
        Tiers = tiers ?? Array.Empty<ConstellationTierDefinition>();
        PresentationFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Name"] = buffName.ZhHans,
            ["Name_zh-Hant"] = buffName.ZhHant,
            ["Name_en"] = buffName.English,
            ["Name_ja"] = buffName.Japanese,
            ["Description"] = BuildDescription(Tiers, text => text.ZhHans),
            ["Description_zh-Hant"] = BuildDescription(Tiers, text => text.ZhHant),
            ["Description_en"] = BuildDescription(Tiers, text => text.English),
            ["Description_ja"] = BuildDescription(Tiers, text => text.Japanese)
        };
    }

    public string Id { get; }
    public ConstellationLocalizedText Name { get; }
    public ConstellationLocalizedText BuffName { get; }
    public IReadOnlyList<ConstellationTierDefinition> Tiers { get; }
    public IReadOnlyDictionary<string, string> PresentationFields { get; }

    public ConstellationTierDefinition? Tier(int level)
    {
        return Tiers.FirstOrDefault(tier => tier.Level == level);
    }

    private static string BuildDescription(
        IEnumerable<ConstellationTierDefinition> tiers,
        Func<ConstellationLocalizedText, string> select)
    {
        return string.Join(Environment.NewLine, tiers
            .OrderBy(tier => tier.Level)
            .Select(tier => select(tier.Description)));
    }
}

public static class ConstellationPoolCatalog
{
    public const int MaxLevel = 6;
    public const string TravelerPoolId = "traveler";
    public const string ColumbinaPoolId = "columbina";

    private static readonly ConstellationPoolDefinition Traveler = new(
        TravelerPoolId,
        Text("旅人座", "旅人座", "Traveler", "旅人座"),
        Text("命之座-旅人座", "命之座-旅人座", "Constellation - Traveler", "命ノ星座・旅人座"),
        new[]
        {
            Tier(1,
                "1层：获得50层超凡。",
                "1層：獲得50層超凡。",
                "Level 1: Gain 50 Transcendence.",
                "1層：超凡を50得る。",
                50),
            Tier(2,
                "2层：每回合开始时获得10%最大生命值护盾。",
                "2層：每回合開始時獲得最大生命值10%的護盾。",
                "Level 2: At the start of each round, gain a shield equal to 10% of Max HP.",
                "2層：各ターン開始時、最大HP10%分のシールドを得る。"),
            Tier(3,
                "3层：获得100层超凡。",
                "3層：獲得100層超凡。",
                "Level 3: Gain 100 Transcendence.",
                "3層：超凡を100得る。",
                100),
            Tier(4,
                "4层：每回合开始时获得1点魔能。",
                "4層：每回合開始時獲得1點魔能。",
                "Level 4: At the start of each round, gain 1 Power.",
                "4層：各ターン開始時、魔力を1得る。"),
            Tier(5,
                "5层：获得100层超凡。",
                "5層：獲得100層超凡。",
                "Level 5: Gain 100 Transcendence.",
                "5層：超凡を100得る。",
                100),
            Tier(6,
                "6层：每回合开始时全队获得300层超凡、20%最大生命值的护盾、2点魔能。",
                "6層：每回合開始時全隊獲得300層超凡、最大生命值20%的護盾、2點魔能。",
                "Level 6: At the start of each round, all player characters gain 300 Transcendence, a shield equal to 20% of Max HP, and 2 Power.",
                "6層：各ターン開始時、味方キャラクター全員が超凡を300、最大HP20%分のシールド、魔力を2得る。")
        });

    private static readonly ConstellationPoolDefinition Columbina = new(
        ColumbinaPoolId,
        Text("御月鸽座", "御月鴿座", "Lunar Dove", "御月鳩座"),
        Text("命之座-御月鸽座", "命之座-御月鴿座", "Constellation - Lunar Dove", "命ノ星座・御月鳩座"),
        new[]
        {
            Tier(1,
                "1层：触发【月】反应时，全队获得1点魔能、1%最大生命的生命上限。",
                "1層：觸發【月】反應時，全隊獲得1點魔能、1%最大生命的生命上限。",
                "Level 1: When a Lunar Reaction triggers, all player characters gain 1 Power and Max HP equal to 1% of their starting Max HP.",
                "1層：【月】反応発動時、味方キャラクター全員が魔力を1得て、戦闘開始時の最大HP1%分だけ最大HPが増加する。"),
            Tier(2,
                "2层：引力值达到50时，额外触发一次引力干涉。",
                "2層：引力值達到50時，額外觸發一次引力干涉。",
                "Level 2: When Gravity Value reaches 50, trigger Gravity Interference one additional time.",
                "2層：引力値が50に達した時、引力干渉を追加で1回発動する。"),
            Tier(3,
                "3层：获得100层超凡。",
                "3層：獲得100層超凡。",
                "Level 3: Gain 100 Transcendence.",
                "3層：超凡を100得る。",
                100),
            Tier(4,
                "4层：触发引力干涉时，增加自身50点血量上限。",
                "4層：觸發引力干涉時，增加自身50點生命上限。",
                "Level 4: When Gravity Interference triggers, increase this character's Max HP by 50.",
                "4層：引力干渉発動時、自身の最大HPが50増加する。"),
            Tier(5,
                "5层：获得100层超凡。",
                "5層：獲得100層超凡。",
                "Level 5: Gain 100 Transcendence.",
                "5層：超凡を100得る。",
                100),
            Tier(6,
                "6层：处于月之领域时，全队每次行动获得80层超凡。",
                "6層：處於月之領域時，全隊每次行動獲得80層超凡。",
                "Level 6: While Moon Domain is active, each party action grants 80 Transcendence.",
                "6層：月の領域展開中、味方が行動するたびに超凡を80得る。")
        });

    private static readonly IReadOnlyDictionary<string, ConstellationPoolDefinition> Pools =
        new Dictionary<string, ConstellationPoolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [Traveler.Id] = Traveler,
            [Columbina.Id] = Columbina
        };

    private static readonly IReadOnlyDictionary<string, string> RoleToPoolId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["columbina"] = ColumbinaPoolId,
            ["Terrias_columbina_columbina"] = ColumbinaPoolId
        };

    public static ConstellationPoolDefinition PoolForRole(string? roleId)
    {
        var normalized = (roleId ?? "").Trim();
        return RoleToPoolId.TryGetValue(normalized, out var poolId)
               && Pools.TryGetValue(poolId, out var pool)
            ? pool
            : Traveler;
    }

    public static bool IsColumbina(string? roleId)
    {
        return string.Equals(PoolForRole(roleId).Id, ColumbinaPoolId, StringComparison.OrdinalIgnoreCase);
    }

    public static int Clamp(int level)
    {
        return Math.Max(0, Math.Min(MaxLevel, level));
    }

    private static ConstellationLocalizedText Text(
        string zhHans,
        string zhHant,
        string english,
        string japanese)
    {
        return new ConstellationLocalizedText(zhHans, zhHant, english, japanese);
    }

    private static ConstellationTierDefinition Tier(
        int level,
        string zhHans,
        string zhHant,
        string english,
        string japanese,
        int oneTimeExtraordinary = 0)
    {
        return new ConstellationTierDefinition(
            level,
            Text(zhHans, zhHant, english, japanese),
            oneTimeExtraordinary);
    }
}
