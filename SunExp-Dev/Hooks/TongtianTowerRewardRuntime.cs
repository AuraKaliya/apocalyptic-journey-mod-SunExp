using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerRewardRuntime
{
    private const string ExtraRandomCardsRuleId = "SunExp.TongtianTower.ExtraRandomCards";

    public static void Initialize()
    {
        BattleRewardAdjustmentService.Register(new BattleRewardAdjustmentRule(
            ExtraRandomCardsRuleId,
            AppliesToTongtianTowerBattleReward,
            context => BattleRewardApi.AppendRandomCardRewards(context.RewardUi, ExtraCardCount(), ExtraRandomCardsRuleId)));
    }

    private static bool AppliesToTongtianTowerBattleReward(BattleRewardAdjustmentContext context)
    {
        return TongtianTowerModeRuntime.IsTongtianTowerRun()
            && BattleRewardApi.IsCurrentBattleReward();
    }

    private static int ExtraCardCount()
    {
        var floor = TongtianTowerModeRuntime.CurrentFloor();
        return Math.Min(4, 1 + ((Math.Max(1, floor) - 1) / 8));
    }
}
