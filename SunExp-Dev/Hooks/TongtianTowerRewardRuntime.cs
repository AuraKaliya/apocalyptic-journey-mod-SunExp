using System;
using System.Runtime.CompilerServices;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerRewardRuntime
{
    private const string RewardRuleId = "SunExp.TongtianTower.RewardPlan";
    private static readonly ConditionalWeakTable<BattleRewardsUI, RewardReplacementState> ReplacementStates = new();

    public static void Initialize(ModConfig modConfig)
    {
        BattleRewardAdjustmentService.RegisterExclusive(new BattleRewardExclusiveRule(
            RewardRuleId,
            context => AppliesToTongtianTowerBattleReward(context.RewardUi)));
        RegisterAfter(modConfig, "BattleRewardsUI.ModeSetReward", ReplaceRewardPlan);
    }

    public static void InitializePostBattleHooks(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BattleRewardsUI.Entry", ApplyPostBattlePressure);
    }

    private static bool AppliesToTongtianTowerBattleReward(BattleRewardsUI? rewardUi)
    {
        return TongtianTowerModeRuntime.IsTongtianTowerRun()
            && rewardUi != null
            && BattleRewardApi.IsCurrentBattleReward();
    }

    private static void ReplaceRewardPlan(ModHookContext context)
    {
        try
        {
            var rewardUi = context.Target as BattleRewardsUI;
            if (!AppliesToTongtianTowerBattleReward(rewardUi))
            {
                return;
            }

            if (ReplaceRewardPlan(rewardUi, "BattleRewardsUI.ModeSetReward"))
            {
                SunExpFrameDispatcher.RunOnceNextFrame(
                    "TongtianTowerReward.VerifyExclusive",
                    () => ReplaceRewardPlan(rewardUi, "BattleRewardsUI.ModeSetReward:verify"));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower reward plan failed", ex);
        }
    }

    private static bool ReplaceRewardPlan(BattleRewardsUI? rewardUi, string source)
    {
        if (!AppliesToTongtianTowerBattleReward(rewardUi))
        {
            return false;
        }

        var boss = TongtianTowerRewardPlan.IsCurrentNodeBoss();
        var floor = TongtianTowerModeRuntime.CurrentFloor();
        var state = ReplacementStates.GetValue(rewardUi!, _ => new RewardReplacementState());
        var generation = floor + ":" + boss + ":" + BattleRewardApi.GeneratedRewardSnapshot(rewardUi);
        if (state.LastGeneration == generation && state.Verified)
        {
            return false;
        }

        var spec = TongtianTowerRewardPlan.ForCurrentNode(floor, boss);
        var before = BattleRewardApi.GeneratedRewardSnapshot(rewardUi);
        var applied = BattleRewardApi.ReplaceWithRewardSpec(rewardUi, spec, RewardRuleId);
        var after = BattleRewardApi.GeneratedRewardSnapshot(rewardUi);
        state.LastGeneration = floor + ":" + boss + ":" + after;
        state.Verified = source.EndsWith(":verify", StringComparison.Ordinal);
        TongtianTowerRunStateStore.MarkPhase(TongtianTowerRunPhase.Reward, source);
        SunExpLog.Info("[TongtianTowerReward] replaced native battle rewards from "
            + source
            + "; floor="
            + floor
            + "; boss="
            + boss
            + "; before="
            + before
            + "; after="
            + after
            + ".");
        return applied;
    }

    private static void ApplyPostBattlePressure(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                return;
            }

            EndlessAbyssShockService.TryEnqueueEndlessBattleShock(
                TongtianTowerModeRuntime.CurrentFloor(),
                TongtianTowerRewardPlan.CurrentNodeKind(),
                "BattleRewardsUI.Entry");
            TongtianTowerOriginService.ApplyBattleEndEffects("BattleRewardsUI.Entry");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower post battle pressure failed", ex);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian tower reward " + message));
    }

    private sealed class RewardReplacementState
    {
        public string LastGeneration { get; set; } = "";

        public bool Verified { get; set; }
    }
}
