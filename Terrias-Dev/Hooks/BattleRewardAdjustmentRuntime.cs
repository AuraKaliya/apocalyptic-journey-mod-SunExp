using System;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class BattleRewardAdjustmentRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BattleRewardsUI.ModeSetReward", ApplyRewardAdjustments);
    }

    private static void ApplyRewardAdjustments(ModHookContext context)
    {
        try
        {
            BattleRewardAdjustmentService.ApplyAll(context.Target as BattleRewardsUI);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[BattleRewardAdjustmentRuntime] apply failed: " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "BattleRewardAdjustment");
    }
}
