using AuraCombatAi.Shared;
using AuraDecision.Shared;

var assertions = 0;

var graph = new DecisionGraph
{
    RootNodeId = "low-health",
    Nodes =
    {
        new DecisionGraphNode
        {
            Id = "low-health",
            Condition = new DecisionCondition
            {
                Feature = "playerHpRatio",
                Comparison = DecisionComparison.LessThan,
                Value = 0.3d
            },
            TrueNodeId = "survive",
            FalseNodeId = "attack"
        },
        new DecisionGraphNode
        {
            Id = "survive",
            Terminal = true,
            UtilityDelta = new DecisionUtility { Survival = 10d }
        },
        new DecisionGraphNode
        {
            Id = "attack",
            Terminal = true,
            UtilityDelta = new DecisionUtility { Lethal = 3d }
        }
    }
};
var lowHealth = DecisionGraphEvaluator.Evaluate(
    graph,
    new Dictionary<string, double> { ["playerHpRatio"] = 0.2d });
Assert(lowHealth.TerminalNodeId == "survive" && lowHealth.UtilityDelta.Survival == 10d,
    "decision graph follows true branch");

var multi = MultiSelectPlanner.ChooseIndices(
    new[]
    {
        new DecisionUtility { Lethal = 8d },
        new DecisionUtility { Lethal = 1d },
        new DecisionUtility { Survival = 3d }
    },
    2,
    preferLowest: true);
Assert(multi.SequenceEqual(new[] { 1, 2 }), "burn planner keeps the weakest choices");

var state = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 1,
        Kind = CombatTargetKind.Self,
        CurrentHp = 20,
        MaxHp = 30
    },
    CurrentPower = 3,
    HandCount = 2,
    IsPlayerActionWindow = true,
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 2,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 6,
            MaxHp = 20
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "attack",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 6d }
        },
        new CombatActionObservation
        {
            CandidateId = "guard",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { Defend = 5d }
        },
        new CombatActionObservation
        {
            CandidateId = "end",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var combatDecision = new CombatDecisionEngine().Choose(state);
Assert(combatDecision.HasAction && combatDecision.Action?.CandidateId == "attack",
    "lethal action wins balanced utility");

using (CombatAiRegistry.RegisterPreflightRule(
           "Tests",
           "RejectAttack",
           new RejectCandidateRule("attack"),
           100))
{
    var guardedDecision = new CombatDecisionEngine().Choose(state);
    Assert(guardedDecision.HasAction && guardedDecision.Action?.CandidateId == "guard",
        "registered preflight rule filters candidates");
}

CombatInteractionBroker.SetNextHint(new CombatInteractionHint
{
    OwnerModId = "Tests",
    Purpose = "burn",
    Kind = CombatPromptKind.BurnCards,
    PreferLowestValue = true
});
var request = CombatInteractionBroker.Begin(
    new CombatInteractionHint { Purpose = "fallback" },
    2,
    state.Actions);
Assert(request.Hint.Purpose == "burn"
       && request.RequiredCount == 2
       && request.State == CombatInteractionState.AwaitingUi,
    "interaction hint is bound to the next native prompt");
Assert(CombatInteractionBroker.Transition(
        request.RequestId,
        CombatInteractionState.Completed,
        "done"),
    "interaction transition");
CombatInteractionBroker.Clear(request.RequestId);
Assert(CombatInteractionBroker.Snapshot() == null, "interaction cleanup");

Console.WriteLine($"AuraCombatAiShared.Tests passed: {assertions} assertions.");

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }

    assertions++;
}

sealed class RejectCandidateRule : ICombatPreflightRule
{
    private readonly string candidateId;

    public RejectCandidateRule(string candidateId)
    {
        this.candidateId = candidateId;
    }

    public bool IsLegal(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        var legal = action.CandidateId != candidateId;
        reason = legal ? "" : "test rejection";
        return legal;
    }
}
