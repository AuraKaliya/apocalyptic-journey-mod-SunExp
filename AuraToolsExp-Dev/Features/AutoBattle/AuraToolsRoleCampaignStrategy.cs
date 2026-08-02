using System;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraToolsExp.Dll.Features.AutoBattle;

/// <summary>
/// Tool-owned official-content defaults for campaign reward evaluation. The
/// values are deliberately modest: they expose coherent Nana packages to the
/// learner without forcing every run into the same archetype.
/// </summary>
public static class AuraToolsRoleCampaignStrategy
{
    public static void Apply(CombatCampaignDefinition campaign)
    {
        if (campaign?.Player == null
            || !string.Equals(
                campaign.Player.RoleId,
                "career_2",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        SetDefault(campaign.RolePrior, "burst", 0.65d);
        SetDefault(campaign.RolePrior, "sustained", 0.50d);
        SetDefault(campaign.RolePrior, "cycling", 0.80d);
        SetDefault(campaign.RolePrior, "aoe", 0.45d);
        SetDefault(campaign.RolePrior, "reliability", 0.35d);
        SetDefault(campaign.RolePrior, "doom-growth", 1.10d);
        SetDefault(campaign.RolePrior, "calamity-burst", 0.70d);
        SetDefault(campaign.RolePrior, "pig-farming", 0.55d);
        SetDefault(campaign.RolePrior, "bleeding", 0.50d);
        SetDefault(campaign.RolePrior, "finale", 0.30d);
        SetDefault(campaign.BuildTendency, "doom-growth", 0.65d);
        SetDefault(campaign.BuildTendency, "calamity-burst", 0.35d);

        SetDefault(
            campaign.RewardScoreBiases,
            AuraToolsNanaRoleStrategyProvider.FinaleCardId,
            1.25d);
        for (var index = 1; index <= 13; index++)
        {
            SetDefault(
                campaign.RewardScoreBiases,
                "blood_" + index,
                index is 4 or 8 or 9 or 11 or 13 ? 0.45d : 0.25d);
        }
        SetDefault(campaign.RewardScoreBiases, "burningcard_1", 0.35d);
        SetDefault(campaign.RewardScoreBiases, "burningcard_2", 0.40d);
        SetDefault(campaign.RewardScoreBiases, "burningcard_4", 0.30d);
        SetDefault(campaign.RewardScoreBiases, "elementscard_9", 0.40d);

        foreach (var reward in campaign.Rewards.Where(item =>
                     item != null
                     && item.Kind == CombatCampaignRewardKind.Card))
        {
            reward.Features ??=
                new System.Collections.Generic.Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase);
            if (IsDoomBuilder(reward.RewardId))
            {
                SetDefault(reward.Features, "doom-growth", 0.90d);
                SetDefault(reward.Features, "pig-farming", 0.65d);
            }
            if (reward.RewardId.StartsWith(
                    "blood_",
                    StringComparison.OrdinalIgnoreCase))
            {
                SetDefault(reward.Features, "bleeding", 0.95d);
                SetDefault(reward.Features, "doom-growth", 0.45d);
            }
            if (string.Equals(
                    reward.RewardId,
                    AuraToolsNanaRoleStrategyProvider.FinaleCardId,
                    StringComparison.OrdinalIgnoreCase))
            {
                SetDefault(reward.Features, "finale", 1.20d);
                SetDefault(reward.Features, "calamity-burst", 0.45d);
            }
        }
    }

    private static bool IsDoomBuilder(string? cardId)
    {
        return string.Equals(cardId, "burningcard_1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "burningcard_2", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "burningcard_4", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "elementscard_9", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetDefault(
        System.Collections.Generic.IDictionary<string, double> values,
        string key,
        double value)
    {
        if (!values.ContainsKey(key))
        {
            values[key] = value;
        }
    }
}
