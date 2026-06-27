using System;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

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
            SunExpLog.Warn("[BattleRewardAdjustmentRuntime] apply failed: " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Battle reward adjustment " + message));
    }
}
