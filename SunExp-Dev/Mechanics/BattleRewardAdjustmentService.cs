using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class BattleRewardAdjustmentService
{
    private static readonly List<BattleRewardAdjustmentRule> Rules = new();
    private static readonly ConditionalWeakTable<BattleRewardsUI, AppliedRuleSet> AppliedRules = new();

    public static void Register(BattleRewardAdjustmentRule rule)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.Id))
        {
            return;
        }

        Rules.RemoveAll(existing => string.Equals(existing.Id, rule.Id, StringComparison.Ordinal));
        Rules.Add(rule);
    }

    public static void ApplyAll(BattleRewardsUI? rewardUi)
    {
        if (rewardUi == null)
        {
            return;
        }

        var context = new BattleRewardAdjustmentContext(rewardUi);
        foreach (var rule in Rules.ToArray())
        {
            ApplyRule(context, rule);
        }
    }

    private static void ApplyRule(BattleRewardAdjustmentContext context, BattleRewardAdjustmentRule rule)
    {
        try
        {
            if (WasApplied(context.RewardUi, rule.Id) || !rule.Applies(context))
            {
                return;
            }

            rule.Apply(context);
            MarkApplied(context.RewardUi, rule.Id);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[BattleRewardAdjustment] rule failed: " + rule.Id + " -> " + ex.Message);
        }
    }

    private static bool WasApplied(BattleRewardsUI rewardUi, string ruleId)
    {
        return AppliedRules.TryGetValue(rewardUi, out var set) && set.Contains(ruleId);
    }

    private static void MarkApplied(BattleRewardsUI rewardUi, string ruleId)
    {
        var set = AppliedRules.GetValue(rewardUi, _ => new AppliedRuleSet());
        set.Add(ruleId);
    }

    private sealed class AppliedRuleSet
    {
        private readonly HashSet<string> applied = new(StringComparer.Ordinal);

        public bool Contains(string ruleId)
        {
            return applied.Contains(ruleId);
        }

        public void Add(string ruleId)
        {
            applied.Add(ruleId);
        }
    }
}

public sealed class BattleRewardAdjustmentContext
{
    public BattleRewardAdjustmentContext(BattleRewardsUI rewardUi)
    {
        RewardUi = rewardUi;
    }

    public BattleRewardsUI RewardUi { get; }
}

public sealed class BattleRewardAdjustmentRule
{
    public BattleRewardAdjustmentRule(
        string id,
        Func<BattleRewardAdjustmentContext, bool> applies,
        Action<BattleRewardAdjustmentContext> apply)
    {
        Id = id;
        Applies = applies;
        Apply = apply;
    }

    public string Id { get; }

    public Func<BattleRewardAdjustmentContext, bool> Applies { get; }

    public Action<BattleRewardAdjustmentContext> Apply { get; }
}
