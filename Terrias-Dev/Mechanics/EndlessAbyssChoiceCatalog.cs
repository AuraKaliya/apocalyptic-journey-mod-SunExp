using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public sealed class EndlessAbyssChoiceOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Available { get; set; } = true;
    public string UnavailableReason { get; set; } = "";
}

public static class EndlessAbyssChoiceCatalog
{
    public static readonly string[] ShockIds = { "sacrifice", "crack-cards", "increase-gaze", "evolution" };
    public static readonly string[] MilestoneIds = { "relic", "other-dimension-card", "remove-burnout", "add-extinction" };

    public static IReadOnlyList<EndlessAbyssChoiceOption> ShockOptions()
    {
        var config = EndlessAbyssConfigStore.Current.Shock;
        return new[]
        {
            Option(ShockIds[0], "献祭", "随机销毁 1 件已装备遗物，获得 " + config.SacrificeCardRewardCount + " 张随机卡牌。\n没有遗物时，深渊注视 +1。"),
            Option(ShockIds[1], "裂痕", "为卡组中随机 " + config.CrackCardCount + " 张无裂痕卡牌添加裂痕，获得 " + config.CrackGold + " 金币。\n无可用目标时，深渊注视 +1。"),
            Option(ShockIds[2], "注视", "深渊注视 +1。\n生命上限 +" + config.GazeMaxHpReward + "。\n随机 1 个本源 +" + config.GazeOriginReward + "。"),
            Option(ShockIds[3], "进化", "敌人额外获得 1 个高级特性。\n获得 1 个随机祝福。")
        };
    }

    public static IReadOnlyList<EndlessAbyssChoiceOption> MilestoneOptions() => new[]
    {
        Option(MilestoneIds[0], "遗物馈赠", "从当前可用的 1～3 阶遗物中，任选 1 件。", EndlessAbyssMilestoneRewardService.RelicCandidates().Count > 0, "没有可选遗物"),
        Option(MilestoneIds[1], "异界来客", "随机获得 1 张异次元卡牌。", EndlessAbyssMilestoneRewardService.OtherDimensionCardCandidates().Count > 0, "异次元卡池为空"),
        Option(MilestoneIds[2], "焚毁净化", "选择卡组中的 1 张卡牌，永久清除其焚毁。", EndlessAbyssMilestoneRewardService.BurnoutCards().Count > 0, "卡组中没有可净化的卡牌"),
        Option(MilestoneIds[3], "绝灭铭刻", "选择卡组中的 1 张卡牌，为其附加绝灭。", EndlessAbyssMilestoneRewardService.ExtinctionTargets().Count > 0, "卡组中没有可附加绝灭的卡牌")
    };

    private static EndlessAbyssChoiceOption Option(string id, string name, string description, bool available = true, string reason = "") =>
        new() { Id = id, Name = name, Description = description, Available = available, UnavailableReason = available ? "" : reason };
}
