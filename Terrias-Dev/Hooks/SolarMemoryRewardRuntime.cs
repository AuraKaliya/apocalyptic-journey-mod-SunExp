using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryRewardRuntime
{
    private const string ExtraRandomRelicRuleId = "Terrias.SolarMemory.ExtraRandomRelic";

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
