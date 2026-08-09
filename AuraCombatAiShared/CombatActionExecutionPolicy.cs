using System;
using System.Globalization;

namespace AuraCombatAi.Shared;

public static class CombatActionExecutionPolicy
{
    public const string DivineChoiceSourceId = "careercard_1";
    public const int DefaultHandLimit = 10;

    public static bool IsDivineChoice(CombatActionObservation? action)
    {
        return action != null
               && action.Kind == CombatActionKind.UseSkill
               && string.Equals(
                   action.SourceId,
                   DivineChoiceSourceId,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLiveEligible(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (!IsDivineChoice(action))
        {
            reason = "";
            return true;
        }

        if ((state.DeckKnowledge?.DrawPileCount ?? 0) <= 0)
        {
            reason = "divine choice requires a card in the draw pile";
            return false;
        }

        if (ResolveAvailableHandSlots(state) <= 0)
        {
            reason = "divine choice requires an available hand slot";
            return false;
        }

        reason = "";
        return true;
    }

    public static int ResolveHandLimit(CombatStateObservation state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (state.Features != null
            && state.Features.TryGetValue("handLimit", out var configured)
            && !double.IsNaN(configured)
            && !double.IsInfinity(configured))
        {
            return Math.Max(1, Math.Min(99, (int)Math.Round(configured)));
        }
        return DefaultHandLimit;
    }

    public static int ResolveAvailableHandSlots(CombatStateObservation state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (state.Features != null
            && state.Features.TryGetValue(
                "availableHandSlots",
                out var configured)
            && !double.IsNaN(configured)
            && !double.IsInfinity(configured))
        {
            return Math.Max(0, (int)Math.Floor(configured));
        }
        var pending = state.Features != null
                      && state.Features.TryGetValue(
                          "pendingHandCards",
                          out var pendingValue)
            ? Math.Max(0, (int)Math.Round(pendingValue))
            : 0;
        return Math.Max(
            0,
            ResolveHandLimit(state) - state.HandCount - pending);
    }

    public static string BuildFailureSuppressionKey(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (!IsDivineChoice(action))
        {
            return state.BattleSessionId.ToString(CultureInfo.InvariantCulture)
                   + "|" + (action.CandidateId ?? "")
                   + "|eligibility:cost="
                   + action.Cost.ToString(CultureInfo.InvariantCulture)
                   + ",runtime="
                   + action.RuntimeId.ToString(CultureInfo.InvariantCulture)
                   + ",target="
                   + action.TargetRuntimeId.ToString(CultureInfo.InvariantCulture)
                   + ",usable=" + Feature(action, "runtimeUsable")
                   + ",ex=" + Feature(action, "cardExCost")
                   + ",once=" + Feature(action, "cardOnceExCost")
                   + ",total=" + Feature(action, "cardTotalExCost");
        }

        var hasDrawCard = (state.DeckKnowledge?.DrawPileCount ?? 0) > 0;
        var hasHandSlot = ResolveAvailableHandSlots(state) > 0;
        return state.BattleSessionId.ToString(CultureInfo.InvariantCulture)
               + "|" + (action.CandidateId ?? "")
               + "|divine-choice:draw=" + (hasDrawCard ? "1" : "0")
               + ",hand-slot=" + (hasHandSlot ? "1" : "0");
    }

    public static double NoEffectGraceSeconds(
        CombatActionObservation action,
        double decisionIntervalSeconds)
    {
        // An accepted root action may open its native prompt asynchronously.
        // A short action-specific grace period races that prompt and turns a
        // valid interaction into a permanent no-effect suppression.  The
        // action transaction deadline is the single bounded watchdog; until
        // then settlement remains pending for every action kind.
        return double.PositiveInfinity;
    }

    public static bool OpensFollowUpInteraction(CombatActionObservation? action)
    {
        return action?.Semantics?.OpensInteraction == true
               || action?.Semantics?.Interaction != null
               || action?.Features != null
               && action.Features.TryGetValue(
                   "dynamicActionSetMutation",
                   out var mutation)
               && mutation > 0d;
    }

    private static string Feature(
        CombatActionObservation action,
        string key)
    {
        return action.Features != null
               && action.Features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : "n/a";
    }
}

public static class CombatDecisionFreshnessPolicy
{
    public static bool TryBindCurrent(
        long capturedBattleSessionId,
        string capturedFingerprint,
        CombatStateObservation current,
        CombatDecision decision,
        out CombatDecision bound,
        out string reason)
    {
        bound = new CombatDecision();
        if (current == null)
        {
            reason = "current observation is unavailable";
            return false;
        }
        if (current.BattleSessionId != capturedBattleSessionId)
        {
            reason = "battle session changed while decision was pending";
            return false;
        }
        if (!string.Equals(
                current.Fingerprint,
                capturedFingerprint,
                StringComparison.Ordinal))
        {
            reason = "combat state changed while decision was pending";
            return false;
        }
        if (!CombatDecisionExecutionBindingProtocol.TryBindToObservation(
                decision,
                current,
                out bound,
                out reason))
        {
            return false;
        }
        if (bound.Action != null
            && !CombatActionExecutionPolicy.IsLiveEligible(
                current,
                bound.Action,
                out reason))
        {
            bound = new CombatDecision();
            return false;
        }

        reason = "";
        return true;
    }
}
