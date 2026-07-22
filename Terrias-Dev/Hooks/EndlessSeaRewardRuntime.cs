using System;
using System.Runtime.CompilerServices;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class EndlessSeaRewardRuntime
{
    private const string RewardRuleId = "Terrias.EndlessSea.RewardPlan";
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
                TerriasFrameDispatcher.RunOnceNextFrame(
                    "EndlessSeaReward.VerifyExclusive",
                    () => ReplaceRewardPlan(rewardUi, "BattleRewardsUI.ModeSetReward:verify"));
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea reward plan failed", ex);
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
        TerriasLog.Info("[EndlessSeaReward] replaced native battle rewards from "
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
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea post battle pressure failed", ex);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "EndlessSeaReward");
    }

    private sealed class RewardReplacementState
    {
        public string LastGeneration { get; set; } = "";

        public bool Verified { get; set; }
    }
}
