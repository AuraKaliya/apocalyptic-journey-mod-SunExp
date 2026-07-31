using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public static class CombatActionContractProtocol
{
    public const string Version = "action-contract-v1";
}

public sealed class CombatActionPreconditionDefinition
{
    public CombatActionPreconditionKind Kind { get; set; }

    public int Amount { get; set; } = 1;

    public CombatActionPreconditionDefinition Clone()
    {
        return (CombatActionPreconditionDefinition)MemberwiseClone();
    }
}

public sealed class CombatActionContractDefinition
{
    public string Version { get; set; } = CombatActionContractProtocol.Version;

    public List<CombatActionPreconditionDefinition> Preconditions { get; set; } =
        new();

    public CombatActionApplicationOutcome PreconditionFailureOutcome { get; set; } =
        CombatActionApplicationOutcome.NoEffect;

    public bool PolicyEligibleOnPreconditionFailure { get; set; }

    public bool CooldownOnApplied { get; set; } = true;

    public int MinimumCardsMovedFromDrawPileToHandOnApplied { get; set; }

    public CombatActionContractDefinition Clone()
    {
        return new CombatActionContractDefinition
        {
            Version = Version,
            Preconditions = Preconditions
                .Select(item => item.Clone())
                .ToList(),
            PreconditionFailureOutcome = PreconditionFailureOutcome,
            PolicyEligibleOnPreconditionFailure =
                PolicyEligibleOnPreconditionFailure,
            CooldownOnApplied = CooldownOnApplied,
            MinimumCardsMovedFromDrawPileToHandOnApplied =
                MinimumCardsMovedFromDrawPileToHandOnApplied
        };
    }
}

public sealed class CombatActionEligibility
{
    public bool GameInvocable { get; set; } = true;

    public bool PolicyEligible { get; set; } = true;

    public CombatActionApplicationOutcome ExpectedOutcome { get; set; } =
        CombatActionApplicationOutcome.Applied;

    public bool GuaranteedNoEffect { get; set; }

    public string Reason { get; set; } = "";
}

public readonly struct CombatActionContractSnapshot
{
    public CombatActionContractSnapshot(int drawPileCount, int handCount)
    {
        DrawPileCount = drawPileCount;
        HandCount = handCount;
    }

    public int DrawPileCount { get; }

    public int HandCount { get; }

    public static CombatActionContractSnapshot Capture(CombatBattleState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        return new CombatActionContractSnapshot(
            state.DrawPile.Count,
            state.Hand.Count);
    }
}

public static class CombatActionContractEvaluator
{
    public static CombatActionEligibility Evaluate(
        CombatScenarioDefinition scenario,
        CombatBattleState state,
        CombatCardDefinition definition,
        CombatSimulationAction action)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (action == null) throw new ArgumentNullException(nameof(action));

        var result = new CombatActionEligibility();
        var actionKey = ActionKey(action);
        if (state.NoEffectActionAttemptsThisTurn.TryGetValue(
                actionKey,
                out var failedAttempts)
            && failedAttempts > 0)
        {
            result.PolicyEligible = false;
            result.ExpectedOutcome = CombatActionApplicationOutcome.NoEffect;
            result.GuaranteedNoEffect = true;
            result.Reason = "suppressed after a no-effect attempt this turn";
            return result;
        }

        var contract = definition.ActionContract;
        if (contract == null)
        {
            return result;
        }

        foreach (var precondition in contract.Preconditions)
        {
            if (Satisfied(scenario, state, precondition))
            {
                continue;
            }
            result.PolicyEligible =
                contract.PolicyEligibleOnPreconditionFailure;
            result.ExpectedOutcome = contract.PreconditionFailureOutcome;
            result.GuaranteedNoEffect =
                contract.PreconditionFailureOutcome
                == CombatActionApplicationOutcome.NoEffect;
            result.Reason = FailureReason(precondition, scenario, state);
            return result;
        }
        return result;
    }

    public static void Apply(
        CombatSimulationAction action,
        CombatActionEligibility eligibility)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (eligibility == null)
        {
            throw new ArgumentNullException(nameof(eligibility));
        }
        action.GameInvocable = eligibility.GameInvocable;
        action.PolicyEligible = eligibility.PolicyEligible;
        action.ExpectedOutcome = eligibility.ExpectedOutcome;
        action.EligibilityReason = eligibility.Reason;
    }

    public static string ActionKey(CombatSimulationAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return action.Kind
               + "|"
               + action.DefinitionId
               + "|"
               + action.CardInstanceId
               + "|"
               + action.TargetActorId;
    }

    public static bool AppliedPostconditionsSatisfied(
        CombatActionContractDefinition? contract,
        CombatActionContractSnapshot before,
        CombatActionContractSnapshot after,
        out string reason)
    {
        var minimumMoved =
            contract?.MinimumCardsMovedFromDrawPileToHandOnApplied ?? 0;
        if (minimumMoved <= 0)
        {
            reason = "";
            return true;
        }
        var removedFromDraw = before.DrawPileCount - after.DrawPileCount;
        var addedToHand = after.HandCount - before.HandCount;
        if (removedFromDraw >= minimumMoved && addedToHand >= minimumMoved)
        {
            reason = "";
            return true;
        }
        reason =
            "expected at least "
            + minimumMoved
            + " card(s) to move from draw pile to hand"
            + " (draw "
            + before.DrawPileCount
            + "->"
            + after.DrawPileCount
            + ", hand "
            + before.HandCount
            + "->"
            + after.HandCount
            + ")";
        return false;
    }

    private static bool Satisfied(
        CombatScenarioDefinition scenario,
        CombatBattleState state,
        CombatActionPreconditionDefinition precondition)
    {
        var amount = Math.Max(1, precondition.Amount);
        return precondition.Kind switch
        {
            CombatActionPreconditionKind.DrawPileCountAtLeast =>
                state.DrawPile.Count >= amount,
            CombatActionPreconditionKind.AvailableHandSlotsAtLeast =>
                Math.Max(0, scenario.HandLimit - state.Hand.Count) >= amount,
            _ => false
        };
    }

    private static string FailureReason(
        CombatActionPreconditionDefinition precondition,
        CombatScenarioDefinition scenario,
        CombatBattleState state)
    {
        var amount = Math.Max(1, precondition.Amount);
        return precondition.Kind switch
        {
            CombatActionPreconditionKind.DrawPileCountAtLeast =>
                "requires at least "
                + amount
                + " card(s) in the draw pile; found "
                + state.DrawPile.Count,
            CombatActionPreconditionKind.AvailableHandSlotsAtLeast =>
                "requires at least "
                + amount
                + " available hand slot(s); found "
                + Math.Max(0, scenario.HandLimit - state.Hand.Count),
            _ => "unsupported action precondition"
        };
    }
}
