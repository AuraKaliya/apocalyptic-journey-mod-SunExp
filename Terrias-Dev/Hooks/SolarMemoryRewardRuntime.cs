using SunExp.Dll.GameApi;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryRewardRuntime
{
    private const string ExtraRandomRelicRuleId = "SunExp.SolarMemory.ExtraRandomRelic";

    public static void Initialize()
    {
        BattleRewardAdjustmentService.Register(new BattleRewardAdjustmentRule(
            ExtraRandomRelicRuleId,
            AppliesToSolarMemoryBattleReward,
            context => BattleRewardApi.AppendRandomRelicReward(context.RewardUi, ExtraRandomRelicRuleId)));
    }

    private static bool AppliesToSolarMemoryBattleReward(BattleRewardAdjustmentContext context)
    {
        return SolarMemoryModeRuntime.IsSolarMemoryRun()
            && BattleRewardApi.IsCurrentBattleReward();
    }
}
