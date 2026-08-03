using System;
using System.Collections.Generic;

namespace AuraCombatAi.Shared;

/// <summary>
/// Consumer-owned timing estimates for ready role skills. Implementations may
/// inspect role-specific state, but must express their result through the
/// semantic-free <see cref="CombatSkillTimingFeatureNames"/> protocol.
/// </summary>
public interface ICombatSkillTimingProvider
{
    bool TryEnrich(CombatStateObservation state);
}

/// <summary>
/// Semantic-free feature protocol for deciding whether a ready role skill
/// should be used now or held for a better window. Concrete roles own the
/// estimates; the shared policy only compares the two timing alternatives.
/// </summary>
public static class CombatSkillTimingFeatureNames
{
    public const string Prefix = "skillTiming:";

    public const string Active = Prefix + "active";

    public const string ResetsEachBattle = Prefix + "resets-each-battle";

    public const string CooldownAfterUse = Prefix + "cooldown-after-use";

    public const string CurrentCooldown = Prefix + "current-cooldown";

    public const string ActivationsThisBattle =
        Prefix + "activations-this-battle";

    /// <summary>
    /// Future payoff unlocked by activating now that is not already represented
    /// by the action's immediate damage, block, healing, or setup semantics.
    /// </summary>
    public const string OngoingEffectValue = Prefix + "ongoing-effect-value";

    /// <summary>
    /// Value of starting the cooldown cycle now, including a plausible extra
    /// activation before the battle ends.
    /// </summary>
    public const string CooldownCycleValue = Prefix + "cooldown-cycle-value";

    /// <summary>
    /// Expected value lost when waiting causes the current activation window to
    /// expire before the skill is used.
    /// </summary>
    public const string ExpiryRisk = Prefix + "expiry-risk";

    /// <summary>
    /// Expected improvement produced by setup actions before activation.
    /// </summary>
    public const string DelayGain = Prefix + "delay-gain";

    /// <summary>
    /// Option value of retaining the ready skill for a forecast threat or
    /// target window.
    /// </summary>
    public const string ReserveValue = Prefix + "reserve-value";

    public const string RedundancyCost = Prefix + "redundancy-cost";

    public const string OpportunityCost = Prefix + "opportunity-cost";

    public const string UseNowValue = Prefix + "use-now-value";

    public const string WaitValue = Prefix + "wait-value";

    public const string TimingAdvantage = Prefix + "timing-advantage";

    public const string PositiveOpportunity = Prefix + "positive-opportunity";

    public const string BetterToWait = Prefix + "better-to-wait";
}

public readonly struct CombatSkillTimingEvaluation
{
    public CombatSkillTimingEvaluation(
        bool active,
        double useNowValue,
        double waitValue,
        double timingAdvantage)
    {
        Active = active;
        UseNowValue = useNowValue;
        WaitValue = waitValue;
        TimingAdvantage = timingAdvantage;
    }

    public bool Active { get; }

    public double UseNowValue { get; }

    public double WaitValue { get; }

    public double TimingAdvantage { get; }

    public bool PositiveOpportunity => Active && TimingAdvantage > 0d;

    public bool BetterToWait => Active && TimingAdvantage < 0d;
}

public static class CombatSkillTimingPolicy
{
    private const double ComponentLimit = 40d;

    public static CombatSkillTimingEvaluation Enrich(
        CombatActionObservation? action)
    {
        if (action == null
            || action.Kind != CombatActionKind.UseSkill
            || action.Features == null
            || Value(action.Features, CombatSkillTimingFeatureNames.Active) <= 0.5d)
        {
            return default;
        }

        var useNowValue = Positive(action, CombatSkillTimingFeatureNames.OngoingEffectValue)
                          + Positive(action, CombatSkillTimingFeatureNames.CooldownCycleValue)
                          + Positive(action, CombatSkillTimingFeatureNames.ExpiryRisk);
        var waitValue = Positive(action, CombatSkillTimingFeatureNames.DelayGain)
                        + Positive(action, CombatSkillTimingFeatureNames.ReserveValue)
                        + Positive(action, CombatSkillTimingFeatureNames.RedundancyCost)
                        + Positive(action, CombatSkillTimingFeatureNames.OpportunityCost);
        var advantage = Clamp(useNowValue - waitValue, -ComponentLimit, ComponentLimit);
        action.Features[CombatSkillTimingFeatureNames.UseNowValue] = useNowValue;
        action.Features[CombatSkillTimingFeatureNames.WaitValue] = waitValue;
        action.Features[CombatSkillTimingFeatureNames.TimingAdvantage] = advantage;
        action.Features[CombatSkillTimingFeatureNames.PositiveOpportunity] =
            advantage > 0d ? 1d : 0d;
        action.Features[CombatSkillTimingFeatureNames.BetterToWait] =
            advantage < 0d ? 1d : 0d;
        return new CombatSkillTimingEvaluation(
            active: true,
            useNowValue,
            waitValue,
            advantage);
    }

    public static double Value(
        IReadOnlyDictionary<string, double>? features,
        string key)
    {
        return features != null
               && features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static double Positive(
        CombatActionObservation action,
        string key)
    {
        return Clamp(Value(action.Features, key), 0d, ComponentLimit);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}
