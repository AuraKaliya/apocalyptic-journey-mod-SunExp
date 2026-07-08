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

public static class EndlessSeaRewardRuntime
{
    private const string RewardRuleId = "SunExp.EndlessSea.RewardPlan";
    private static readonly ConditionalWeakTable<BattleRewardsUI, RewardReplacementState> ReplacementStates = new();

    public static void Initialize(ModConfig modConfig)
    {
        BattleRewardAdjustmentService.RegisterExclusive(new BattleRewardExclusiveRule(
            RewardRuleId,
            context => AppliesToEndlessSeaBattleReward(context.RewardUi)));
        RegisterAfter(modConfig, "BattleRewardsUI.ModeSetReward", ReplaceRewardPlan);
    }

    public static void InitializePostBattleHooks(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BattleRewardsUI.Entry", ApplyPostBattlePressure);
    }

    private static bool AppliesToEndlessSeaBattleReward(BattleRewardsUI? rewardUi)
    {
        return EndlessSeaModeRuntime.IsEndlessSeaRun()
            && rewardUi != null
            && BattleRewardApi.IsCurrentBattleReward();
    }

    private static void ReplaceRewardPlan(ModHookContext context)
    {
        try
        {
            var rewardUi = context.Target as BattleRewardsUI;
            if (!AppliesToEndlessSeaBattleReward(rewardUi))
            {
                return;
            }

            if (ReplaceRewardPlan(rewardUi, "BattleRewardsUI.ModeSetReward"))
            {
                SunExpFrameDispatcher.RunOnceNextFrame(
                    "EndlessSeaReward.VerifyExclusive",
                    () => ReplaceRewardPlan(rewardUi, "BattleRewardsUI.ModeSetReward:verify"));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea reward plan failed", ex);
        }
    }

    private static bool ReplaceRewardPlan(BattleRewardsUI? rewardUi, string source)
    {
        if (!AppliesToEndlessSeaBattleReward(rewardUi))
        {
            return false;
        }

        var boss = EndlessSeaRewardPlan.IsCurrentNodeBoss();
        var floor = EndlessSeaModeRuntime.CurrentFloor();
        var state = ReplacementStates.GetValue(rewardUi!, _ => new RewardReplacementState());
        var generation = floor + ":" + boss + ":" + BattleRewardApi.GeneratedRewardSnapshot(rewardUi);
        if (state.LastGeneration == generation && state.Verified)
        {
            return false;
        }

        var spec = EndlessSeaRewardPlan.ForCurrentNode(floor, boss);
        var before = BattleRewardApi.GeneratedRewardSnapshot(rewardUi);
        var applied = BattleRewardApi.ReplaceWithRewardSpec(rewardUi, spec, RewardRuleId);
        var after = BattleRewardApi.GeneratedRewardSnapshot(rewardUi);
        state.LastGeneration = floor + ":" + boss + ":" + after;
        state.Verified = source.EndsWith(":verify", StringComparison.Ordinal);
        EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.Reward, source);
        SunExpLog.Info("[EndlessSeaReward] replaced native battle rewards from "
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
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                return;
            }

            EndlessAbyssShockService.TryEnqueueEndlessBattleShock(
                EndlessSeaModeRuntime.CurrentFloor(),
                EndlessSeaRewardPlan.CurrentNodeKind(),
                "BattleRewardsUI.Entry");
            EndlessSeaOriginService.ApplyBattleEndEffects("BattleRewardsUI.Entry");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea post battle pressure failed", ex);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Endless Sea reward " + message));
    }

    private sealed class RewardReplacementState
    {
        public string LastGeneration { get; set; } = "";

        public bool Verified { get; set; }
    }
}
