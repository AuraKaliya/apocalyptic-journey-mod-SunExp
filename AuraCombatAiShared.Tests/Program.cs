using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

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
            SourceId = "attack",
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

var transaction = new CombatActionTransaction();
Assert(transaction.TryBegin(7, "card:test", 10d, 2d),
    "action transaction accepts the first root submission");
Assert(!transaction.TryBegin(7, "card:duplicate", 10.1d, 2d)
       && transaction.SubmitCount == 1,
    "action transaction rejects duplicate root submissions");
transaction.AwaitPrompt();
transaction.Selecting();
Assert(!transaction.CheckDeadline(12d), "transaction remains active at its exact deadline");
Assert(transaction.CheckDeadline(12.01d)
       && transaction.State == CombatActionTransactionState.TimedOut,
    "pending interaction obeys the root action deadline");

transaction.Reset();
Assert(transaction.TryBegin(8, "card:handoff", 0d, 5d), "handoff transaction starts");
transaction.HandOff("player takeover");
Assert(transaction.State == CombatActionTransactionState.HandedOff
       && transaction.TerminalReason == "player takeover",
    "player takeover is a terminal transaction state");

foreach (var intermediate in new[]
         {
             CombatActionTransactionState.ExecutingRoot,
             CombatActionTransactionState.AwaitingPrompt,
             CombatActionTransactionState.Selecting,
             CombatActionTransactionState.AwaitingSettlement
         })
{
    transaction.Reset();
    Assert(transaction.TryBegin(9, "battle-end", 0d, 5d), "battle-end transaction starts");
    if (intermediate == CombatActionTransactionState.AwaitingPrompt)
    {
        transaction.AwaitPrompt();
    }
    else if (intermediate == CombatActionTransactionState.Selecting)
    {
        transaction.Selecting();
    }
    else if (intermediate == CombatActionTransactionState.AwaitingSettlement)
    {
        transaction.AwaitSettlement();
    }
    transaction.Cancel("battle ended");
    Assert(transaction.State == CombatActionTransactionState.Cancelled,
        "battle end cancels " + intermediate);
}

var selection = new CombatPromptSelectionTracker(2, 0.8d);
Assert(selection.Observe(0, 0d) == CombatSelectionProgress.Ready
       && selection.TryBeginAttempt(0, 0d),
    "multi-select starts one selection attempt");
Assert(selection.Observe(0, 0.5d) == CombatSelectionProgress.Pending,
    "selection waits for native progress");
Assert(selection.Observe(1, 0.6d) == CombatSelectionProgress.Advanced
       && selection.TryBeginAttempt(1, 0.7d),
    "selection verifies count growth before advancing");
Assert(selection.Observe(1, 1.6d) == CombatSelectionProgress.TimedOut,
    "selection no-progress timeout is detected");

selection = new CombatPromptSelectionTracker(2);
Assert(selection.TryBeginAttempt(0, 0d)
       && selection.Observe(1, 0.1d) == CombatSelectionProgress.Advanced
       && selection.TryBeginAttempt(1, 0.2d)
       && selection.Observe(2, 0.3d) == CombatSelectionProgress.Complete,
    "multi-select reaches the exact required count");
Assert(selection.TryIssueConfirm(2) && !selection.TryIssueConfirm(2),
    "prompt confirmation can only be issued once");

var setupState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 10,
        Kind = CombatTargetKind.Self,
        CurrentHp = 25,
        MaxHp = 30
    },
    CurrentPower = 3,
    MaxPower = 3,
    HandCount = 4,
    IsPlayerActionWindow = true,
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 20,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 20,
            MaxHp = 20
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "small-attack",
            SourceId = "small-attack",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 20,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics { Damage = 3d }
        },
        new CombatActionObservation
        {
            CandidateId = "free-setup",
            SourceId = "free-setup",
            Kind = CombatActionKind.PlayCard,
            Cost = 0,
            Semantics = new CombatActionSemantics
            {
                Draw = 2d,
                Buff = 2d,
                CostReduction = 1d
            }
        },
        new CombatActionObservation
        {
            CandidateId = "end-setup-test",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var setupDecision = new CombatDecisionEngine().Choose(setupState);
Assert(setupDecision.Action?.CandidateId == "free-setup",
    "known positive zero-cost setup can precede a weak attack");

setupState.Actions[1].Semantics = new CombatActionSemantics
{
    RandomOutcome = true,
    Uncertainty = 2d
};
var uncertainFreeDecision = new CombatDecisionEngine().Choose(setupState);
Assert(uncertainFreeDecision.Action?.CandidateId == "small-attack",
    "zero cost alone does not force an uncertain action");

var projectedLeafFeatures = CombatSearchFeatureProjector.ProjectLeaf(
    new CombatSimulationState
    {
        PlayerHp = 18,
        PlayerMaxHp = 30,
        PlayerDefend = 3,
        Power = 2,
        MaxPower = 3,
        HandCount = 4,
        SetupValue = 2.5d,
        DamageMultiplier = 1.2d,
        Threats =
        [
            new CombatSimulationThreat
            {
                BlockableDamage = 10d,
                UnblockableDamage = 2d,
                DamageOverTime = 1d,
                Probability = 0.7d
            }
        ]
    },
    new CombatDecisionProfile(),
    new Dictionary<string, double> { ["turn"] = 3d });
Assert(projectedLeafFeatures["expectedBlockableDamage"] == 7d
       && projectedLeafFeatures["maximumBlockableDamage"] == 10d
       && projectedLeafFeatures["expectedUnblockableDamage"] == 1.4d
       && projectedLeafFeatures["expectedDamageOverTime"] == 0.7d
       && projectedLeafFeatures["playerHp"] == 18d
       && projectedLeafFeatures["setupValue"] == 2.5d
       && projectedLeafFeatures["damageMultiplier"] == 1.2d
       && projectedLeafFeatures["turn"] == 3d,
    "search leaf inference uses the same feature names as training");
var setupLeafState = new CombatSimulationState
{
    PlayerHp = 20,
    PlayerMaxHp = 20,
    SetupValue = 5d,
    PersistentValue = 3d,
    Enemies =
    [
        new CombatSimulationUnit { RuntimeId = 9, Hp = 10, MaxHp = 10 }
    ]
};
var emptyLeafState = setupLeafState.Clone();
emptyLeafState.SetupValue = 0d;
emptyLeafState.PersistentValue = 0d;
Assert(setupLeafState.EvaluateLeaf(new CombatDecisionProfile()).Value
       > emptyLeafState.EvaluateLeaf(new CombatDecisionProfile()).Value,
    "forward leaf value retains setup and persistent benefits");

var cooldownState = new CombatStateObservation
{
    Player = setupState.Player,
    CurrentPower = 2,
    MaxPower = 3,
    HandCount = 2,
    IsPlayerActionWindow = true,
    Enemies = setupState.Enemies,
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "free-card",
            Kind = CombatActionKind.PlayCard,
            Cost = 0,
            Semantics = new CombatActionSemantics { Buff = 2d }
        },
        new CombatActionObservation
        {
            CandidateId = "long-cooldown-skill",
            Kind = CombatActionKind.UseSkill,
            Cost = 0,
            Semantics = new CombatActionSemantics { Buff = 2d, CooldownTurns = 6d }
        },
        new CombatActionObservation
        {
            CandidateId = "end-cooldown-test",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var cooldownDecision = new CombatDecisionEngine().Choose(cooldownState);
Assert(cooldownDecision.Action?.CandidateId == "free-card",
    "long-cooldown skill carries an opportunity cost");

var poisonThreatState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 30,
        Kind = CombatTargetKind.Self,
        CurrentHp = 100,
        MaxHp = 100,
        Defend = 24
    },
    CurrentPower = 1,
    MaxPower = 3,
    HandCount = 2,
    IsPlayerActionWindow = true,
    Threat = new CombatThreatForecast
    {
        CurrentIntentKnown = true,
        ExpectedDamageOverTime = 12d,
        AttackProbability = 1d,
        Confidence = 1d
    },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 31,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 12,
            MaxHp = 12
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "poison-shield",
            SourceId = "shield",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 301,
            Cost = 1,
            Semantics = new CombatActionSemantics { Defend = 6d }
        },
        new CombatActionObservation
        {
            CandidateId = "poison-attack",
            SourceId = "attack",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 302,
            TargetRuntimeId = 31,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics { Damage = 3d }
        },
        new CombatActionObservation
        {
            CandidateId = "poison-end",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var poisonDecision = new CombatDecisionEngine().Choose(poisonThreatState);
var poisonShieldFeatures = poisonDecision.Candidates
    .Single(candidate => candidate.Action.CandidateId == "poison-shield")
    .Action.Features;
Assert(poisonShieldFeatures["immediateDefend"] == 0d
       && poisonShieldFeatures["shieldCarryGain"] == 6d
       && poisonShieldFeatures["wastedDefend"] == 0d,
    "damage-over-time does not consume shield, while persistent shield keeps future value");

var attackThreatState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 40,
        Kind = CombatTargetKind.Self,
        CurrentHp = 5,
        MaxHp = 30
    },
    CurrentPower = 1,
    MaxPower = 3,
    HandCount = 2,
    IsPlayerActionWindow = true,
    Threat = new CombatThreatForecast
    {
        CurrentIntentKnown = true,
        ExpectedBlockableDamage = 10d,
        MaximumBlockableDamage = 10d,
        AttackProbability = 1d,
        Confidence = 1d
    },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 41,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 30,
            MaxHp = 30
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "needed-shield",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 401,
            Cost = 1,
            Semantics = new CombatActionSemantics { Defend = 10d }
        },
        new CombatActionObservation
        {
            CandidateId = "nonlethal-attack",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 402,
            TargetRuntimeId = 41,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics { Damage = 4d }
        },
        new CombatActionObservation
        {
            CandidateId = "attack-threat-end",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var survivalDecision = new CombatDecisionEngine().Choose(attackThreatState);
Assert(survivalDecision.Action?.CandidateId == "needed-shield",
    "known lethal blockable threat still prioritizes sufficient shield");

using (CombatAiRegistry.RegisterThreatProvider(
           "Tests",
           "PoisonForecast",
           new FixedThreatProvider(new CombatThreatForecast
           {
               CurrentIntentKnown = true,
               ExpectedUnblockableDamage = 9d,
               Confidence = 1d
           }),
           100))
{
    Assert(CombatAiRegistry.TryResolveThreat(state, out var registeredThreat)
           && registeredThreat.ExpectedUnblockableDamage == 9d,
        "registered threat provider can override the native forecast");
}

var model = new BoundedLinearDecisionResidualModel(new DecisionResidualModelDefinition
{
    ModelId = "bounded-test",
    FeatureSchemaVersion = 6,
    MaximumCorrection = 2d,
    Weights = new Dictionary<string, double> { ["effectiveDamage"] = 100d },
    FeatureMinimums = new Dictionary<string, double> { ["effectiveDamage"] = 0d },
    FeatureMaximums = new Dictionary<string, double> { ["effectiveDamage"] = 5d },
    FeatureObservationCounts = new Dictionary<string, double> { ["effectiveDamage"] = 10d },
    CategoryObservationCounts = new Dictionary<string, double> { ["categoryAttack"] = 10d }
});
var boundedPrediction = model.Evaluate(new Dictionary<string, double>
{
    ["effectiveDamage"] = 5d,
    ["semanticConfidence"] = 1d,
    ["categoryAttack"] = 1d
});
Assert(boundedPrediction.RawCorrection == 2d
       && boundedPrediction.Applicability == 1d
       && boundedPrediction.AppliedCorrection == 2d,
    "learned residual is bounded before it reaches the rule engine");
Assert(model.Predict(new Dictionary<string, double>
       {
           ["effectiveDamage"] = 5d,
           ["semanticConfidence"] = 0d,
           ["categoryAttack"] = 1d
       }) == 0d,
    "unknown mechanics fall back to the base policy");

combatDecision.Action!.Features["nonFinite"] = double.NaN;
var afterState = new CombatStateObservation
{
    BattleSessionId = state.BattleSessionId,
    Sequence = state.Sequence + 1,
    Fingerprint = "after",
    Player = new CombatUnitObservation
    {
        RuntimeId = 1,
        Kind = CombatTargetKind.Self,
        CurrentHp = 20,
        MaxHp = 30,
        Defend = 0
    },
    CurrentPower = 2,
    MaxPower = 3,
    HandCount = 1,
    IsPlayerActionWindow = true
};
var trainingSample = CombatTrainingSampleBuilder.Create(
    state,
    afterState,
    combatDecision,
    1,
    42,
    CombatActionTransactionState.Completed.ToString(),
    "settled",
    terminal: true,
    gameBuild: "test-game",
    sharedBuild: "test-shared");
Assert(trainingSample.ModelProtocol == "aura.combat-ai.sample.v6"
       && trainingSample.FeatureSchemaVersion == 6
       && trainingSample.Candidates.Count == state.Actions.Count
       && trainingSample.Candidates.Single(candidate =>
           candidate.CandidateId == "attack").SourceId == "attack",
    "training v5 captures the selected action and every candidate");
Assert(trainingSample.Selection.Protocol == "aura.combat-ai.selection.v1"
       && trainingSample.Selection.ExecutedBy == "policy"
       && trainingSample.Selection.LabelKind == "policy-trajectory"
       && trainingSample.Selection.ExecutedCandidateId == "attack"
       && trainingSample.Selection.PolicyPreselectedCandidateId == "attack"
       && trainingSample.Selection.PolicyWasExecuted
       && trainingSample.Candidates.Single(candidate =>
           candidate.CandidateId == "attack").IsExecutedAction
       && trainingSample.Candidates.Single(candidate =>
           candidate.CandidateId == "attack").IsPolicyPreselection,
    "policy sample marks its preselected and executed action");
Assert(trainingSample.Terminal
       && trainingSample.BattleOutcome == "victory"
       && trainingSample.RewardComponents.TerminalBonus == 50d,
    "training v5 captures terminal outcome reward");
Assert(!trainingSample.Features.ContainsKey("nonFinite"),
    "training features reject non-finite values");

var humanSample = CombatTrainingSampleBuilder.Create(
    state,
    afterState,
    combatDecision,
    2,
    43,
    CombatActionTransactionState.Completed.ToString(),
    "human settled",
    terminal: false,
    gameBuild: "test-game",
    sharedBuild: "test-shared",
    demonstrator: "human",
    recommendedCandidateId: "guard",
    policyVisibleToHuman: true);
humanSample.BattleSessionId = 43;
var humanActionCandidate = humanSample.Candidates.Single(candidate =>
    candidate.CandidateId == "attack");
var policyCandidate = humanSample.Candidates.Single(candidate =>
    candidate.CandidateId == "guard");
Assert(humanSample.Selection.ExecutedBy == "human"
       && humanSample.Selection.LabelKind == "human-preference"
       && humanSample.Selection.PolicyVisibleToHuman
       && !humanSample.Selection.PolicyWasExecuted
       && !humanSample.Selection.HumanPolicyAgreement
       && humanActionCandidate.IsExecutedAction
       && humanActionCandidate.IsHumanSelection
       && !humanActionCandidate.IsPolicyPreselection
       && !policyCandidate.IsExecutedAction
       && policyCandidate.IsPolicyPreselection,
    "human sample visibly separates the executed action from policy preselection");

var noThreatDefendFeatures = CombatDecisionEngine.BuildFeatures(
    state,
    state.Actions.Single(action => action.CandidateId == "guard"));
var neededDefendFeatures = CombatDecisionEngine.BuildFeatures(
    attackThreatState,
    attackThreatState.Actions.Single(action => action.CandidateId == "needed-shield"));
Assert(noThreatDefendFeatures["usefulDefend"] == 5d
       && noThreatDefendFeatures["immediateDefend"] == 0d
       && noThreatDefendFeatures["shieldCarryGain"] == 5d
       && noThreatDefendFeatures["wastedDefend"] == 0d
       && noThreatDefendFeatures["semanticConfidence"] == 1d
       && neededDefendFeatures["immediateDefend"] > 0d,
    "context features distinguish immediate defense from persistent shield value");

var originalSemanticAction = new CombatActionObservation
{
    CandidateId = "original-attack",
    SourceId = "BaseGame:card",
    Kind = CombatActionKind.PlayCard,
    TargetRuntimeId = 2,
    TargetKind = CombatTargetKind.Enemy,
    Semantics = new CombatActionSemantics { Damage = 6d }
};
var sameSemanticAction = new CombatActionObservation
{
    CandidateId = "mod-attack",
    SourceId = "SomeOtherMod:custom-card",
    Kind = CombatActionKind.PlayCard,
    TargetRuntimeId = 2,
    TargetKind = CombatTargetKind.Enemy,
    Semantics = new CombatActionSemantics { Damage = 6d }
};
var originalFeatures = CombatDecisionEngine.BuildFeatures(state, originalSemanticAction);
var modFeatures = CombatDecisionEngine.BuildFeatures(state, sameSemanticAction);
Assert(originalFeatures.All(pair =>
        modFeatures.TryGetValue(pair.Key, out var value) && value == pair.Value)
       && modFeatures.All(pair =>
           originalFeatures.TryGetValue(pair.Key, out var value) && value == pair.Value),
    "residual context is independent of original or MOD content identity");

var trained = CombatResidualTrainer.Train(new[] { humanSample }, "balanced");
Assert(trained.Success
       && trained.Model?.FeatureSchemaVersion == 6
       && trained.Model.DecisionProfile == "balanced"
       && trained.Model.FeatureMinimums.Count > 0
       && trained.Model.CategoryObservationCounts.Count > 0,
    "in-process trainer produces a contextual candidate with applicability metadata");
var gatedTraining = CombatResidualTrainer.Train(
    new[] { humanSample },
    "balanced",
    new CombatResidualTrainingOptions
    {
        PresetId = "steady",
        Epochs = 80,
        LearningRate = 0.03d,
        L2 = 0.003d,
        MaximumCorrection = 0.75d,
        MinimumPreferencePairs = 2,
        MinimumCategoryObservations = 10
    });
Assert(!gatedTraining.Success
       && gatedTraining.PreferencePairCount == 1
       && gatedTraining.Message.Contains("最低要求 2", StringComparison.Ordinal),
    "training options reject undersized preference sets before producing a model");
var configuredTraining = CombatResidualTrainer.Train(
    new[] { humanSample },
    "balanced",
    new CombatResidualTrainingOptions
    {
        PresetId = "steady",
        Epochs = 80,
        LearningRate = 0.03d,
        L2 = 0.003d,
        MaximumCorrection = 0.75d,
        MinimumPreferencePairs = 1,
        MinimumCategoryObservations = 10
    });
Assert(configuredTraining.Success
       && configuredTraining.Model?.MaximumCorrection == 0.75d
       && configuredTraining.Model.TrainingPreset == "steady"
       && configuredTraining.Model.MinimumCategoryObservations == 10d
       && configuredTraining.Model.TrainingParameters["epochs"] == 80d
       && configuredTraining.Model.TrainingParameters["randomSeed"] == 7d,
    "training options are bounded and persisted in the candidate model metadata");
Assert(configuredTraining.Model!.Metrics["battleSessionCount"] == 1d
       && configuredTraining.Model.Metrics["groupedValidationAccuracy"] == 0d,
    "grouped validation refuses to report in-sample accuracy for one battle");
var secondBattleHumanSample = CombatTrainingSampleBuilder.Create(
    state,
    afterState,
    combatDecision,
    3,
    44,
    CombatActionTransactionState.Completed.ToString(),
    "second battle human settled",
    terminal: false,
    gameBuild: "test-game",
    sharedBuild: "test-shared",
    demonstrator: "human",
    recommendedCandidateId: "guard",
    policyVisibleToHuman: false);
secondBattleHumanSample.BattleSessionId = 44;
var groupedTraining = CombatResidualTrainer.Train(
    new[] { humanSample, secondBattleHumanSample },
    "balanced");
Assert(groupedTraining.Success
       && groupedTraining.Model?.Metrics["battleSessionCount"] == 2d
       && groupedTraining.Model.Metrics["groupedValidationAccuracy"] >= 0d
       && groupedTraining.Model.Metrics["groupedValidationAccuracy"] <= 1d,
    "residual validation holds out complete battle sessions");
var wrongProfileTraining = CombatResidualTrainer.Train(new[] { humanSample }, "defensive");
Assert(!wrongProfileTraining.Success && wrongProfileTraining.PreferencePairCount == 0,
    "training keeps decision profiles separate without gating on MOD identity");
var guidanceTraining = CombatSearchGuidanceTrainer.Train(
    new[] { trainingSample, humanSample },
    "balanced",
    rounds: 12,
    learningRate: 0.08d);
Assert(guidanceTraining.Success
       && guidanceTraining.Model != null
       && guidanceTraining.Model.Policy.Trees.Count > 0
       && !double.IsNaN(guidanceTraining.Model.Value.Bias)
       && !guidanceTraining.Model.RiskTrained
       && guidanceTraining.Model.Risk.Trees.Count == 0,
    "search guidance trainer produces bounded policy and value tree ensembles");
var guidanceModel = new BoundedTreeCombatSearchGuidanceModel(guidanceTraining.Model!);
Assert(!double.IsNaN(guidanceModel.PolicyLogit(originalFeatures))
       && guidanceModel.DeathRisk(humanSample.StateFeatures) == 0d,
    "untrained one-class terminal risk does not manufacture a death predictor");
var incompatibleLegacySample = new CombatTrainingSample
{
    ModelProtocol = "aura.combat-ai.sample.v3",
    FeatureSchemaVersion = 3,
    CompletionState = "Completed",
    DecisionProfile = "balanced",
    BattleSessionId = 77,
    StateFeatures = new Dictionary<string, double>
    {
        ["playerHp"] = 20d,
        ["playerMaxHp"] = 30d,
        ["playerDefend"] = 0d,
        ["expectedBlockableDamage"] = 0d,
        ["power"] = 2d,
        ["maxPower"] = 3d,
        ["handCount"] = 2d
    },
    Selection = new CombatTrainingSelectionTrace
    {
        ExecutedCandidateId = "legacy-defend"
    }
};
var legacyContext = CombatResidualTrainer.ContextualFeatures(
    incompatibleLegacySample,
    new CombatTrainingCandidate
    {
        ActionKind = "PlayCard",
        Semantics = new CombatActionSemantics { Defend = 6d }
    });
Assert(legacyContext["wastedDefend"] == 6d
       && legacyContext["semanticConfidence"] == 1d
       && legacyContext["categoryDefend"] == 1d,
    "context reconstruction remains deterministic for isolated diagnostics");
var incompatibleTraining = CombatResidualTrainer.Train(
    new[] { incompatibleLegacySample },
    "balanced");
Assert(!CombatTrainingProtocol.IsCompatible(incompatibleLegacySample)
       && incompatibleTraining.CompletedSampleCount == 0
       && !incompatibleTraining.Success
       && CombatLiveEpisodeAssembler.Assemble(
           new[] { incompatibleLegacySample }).Count == 0,
    "legacy omniscient samples are rejected at every player-equivalent training ingress");

var forwardRoot = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 70,
        CurrentHp = 20,
        MaxHp = 30
    },
    CurrentPower = 0,
    MaxPower = 3,
    HandCount = 9
};
var forwardState = CombatForwardModel.Create(forwardRoot, 4);
var reductionAction = new CombatActionObservation
{
    CandidateId = "reduce-three",
    Kind = CombatActionKind.PlayCard,
    Semantics = new CombatActionSemantics { CostReduction = 3d }
};
forwardState = CombatForwardModel.Apply(
    forwardState,
    reductionAction,
    0,
    CombatForwardModel.Resolve(forwardRoot, reductionAction).Outcomes[0],
    new CombatDecisionProfile());
var firstDiscounted = new CombatActionObservation
{
    CandidateId = "cost-two-a",
    Kind = CombatActionKind.PlayCard,
    Cost = 2,
    Semantics = new CombatActionSemantics { Draw = 3d }
};
forwardState = CombatForwardModel.Apply(
    forwardState,
    firstDiscounted,
    1,
    CombatForwardModel.Resolve(forwardRoot, firstDiscounted).Outcomes[0],
    new CombatDecisionProfile());
Assert(forwardState.CostReduction == 1
       && forwardState.Power == 0
       && forwardState.HandCount == 10,
    "forward model consumes cost reduction by base cost and applies the hand limit");
var secondDiscounted = new CombatActionObservation
{
    CandidateId = "cost-two-b",
    Kind = CombatActionKind.PlayCard,
    Cost = 2
};
Assert(CombatForwardModel.EffectiveCost(forwardState, secondDiscounted) == 1,
    "surplus cost reduction cannot make every later card free");
var multiplierRoot = new CombatStateObservation
{
    Player = new CombatUnitObservation { RuntimeId = 75, CurrentHp = 20, MaxHp = 20 },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 76,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 20,
            MaxHp = 20
        }
    }
};
var multiplierState = CombatForwardModel.Create(multiplierRoot, 2);
var multiplierSetup = new CombatActionObservation
{
    CandidateId = "element-setup",
    Kind = CombatActionKind.PlayCard,
    Semantics = new CombatActionSemantics { DamageMultiplierGain = 0.5d }
};
multiplierState = CombatForwardModel.Apply(
    multiplierState,
    multiplierSetup,
    0,
    CombatForwardModel.Resolve(multiplierRoot, multiplierSetup).Outcomes[0],
    new CombatDecisionProfile());
var multipliedAttack = new CombatActionObservation
{
    CandidateId = "multiplied-attack",
    Kind = CombatActionKind.PlayCard,
    TargetRuntimeId = 76,
    TargetKind = CombatTargetKind.Enemy,
    Semantics = new CombatActionSemantics { Damage = 8d }
};
multiplierState = CombatForwardModel.Apply(
    multiplierState,
    multipliedAttack,
    1,
    CombatForwardModel.Resolve(multiplierRoot, multipliedAttack).Outcomes[0],
    new CombatDecisionProfile());
Assert(multiplierState.Enemies[0].Hp == 8,
    "typed setup state changes the simulated value of later damage");
var selfAttritionAction = new CombatActionObservation
{
    CandidateId = "self-attrition",
    Kind = CombatActionKind.PlayCard,
    Semantics = new CombatActionSemantics
    {
        Damage = 2d,
        SelfHpLoss = 3d,
        EndOfCycleSelfHpLoss = 1d
    }
};
var selfAttritionState = CombatForwardModel.Apply(
    CombatForwardModel.Create(multiplierRoot, 1),
    selfAttritionAction,
    0,
    CombatForwardModel.Resolve(multiplierRoot, selfAttritionAction).Outcomes[0],
    new CombatDecisionProfile());
Assert(selfAttritionState.PlayerHp == 16,
    "forward model charges immediate and end-of-cycle self hp loss");
var loopStart = new CombatSimulationState
{
    PlayerHp = 20,
    PlayerMaxHp = 20,
    Power = 3,
    MaxPower = 3,
    HandCount = 2,
    Enemies =
    [
        new CombatSimulationUnit
        {
            RuntimeId = 751,
            Hp = 20,
            MaxHp = 20
        }
    ]
};
var safeLoopEnd = loopStart.Clone();
safeLoopEnd.Enemies[0].Hp = 15;
var fakeLoopEnd = safeLoopEnd.Clone();
fakeLoopEnd.PlayerHp = 4;
var limitedLoopEnd = loopStart.Clone();
limitedLoopEnd.Enemies[0].Hp = 19;
limitedLoopEnd.Enemies[0].Features["damageLimitActive"] = 1d;
var controlLoopEnd = loopStart.Clone();
Assert(CombatLoopSafetyAnalyzer.Analyze(
           loopStart,
           safeLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.CertifiedLethal
       && CombatLoopSafetyAnalyzer.Analyze(
           loopStart,
           fakeLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.Fake
       && CombatLoopSafetyAnalyzer.Analyze(
           loopStart,
           limitedLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.Blocked
       && CombatLoopSafetyAnalyzer.Analyze(
           loopStart,
           controlLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.SustainableControl,
    "loop safety distinguishes lethal, hp-draining fake, limit-damage blocked, and control-only cycles");

var threatRoot = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 80,
        CurrentHp = 5,
        MaxHp = 20
    },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 81,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 4,
            MaxHp = 4
        }
    },
    Threat = new CombatThreatForecast
    {
        CurrentIntentKnown = true,
        Intents =
        {
            new CombatIntentObservation
            {
                SourceRuntimeId = 81,
                Kind = CombatIntentKind.Attack,
                Probability = 1d,
                BlockableDamage = 10d
            }
        }
    }
};
var killThreat = new CombatActionObservation
{
    CandidateId = "kill-threat",
    Kind = CombatActionKind.PlayCard,
    RuntimeId = 801,
    TargetRuntimeId = 81,
    TargetKind = CombatTargetKind.Enemy,
    Semantics = new CombatActionSemantics { Damage = 4d }
};
var threatState = CombatForwardModel.Create(threatRoot, 1);
threatState = CombatForwardModel.Apply(
    threatState,
    killThreat,
    0,
    CombatForwardModel.Resolve(threatRoot, killThreat).Outcomes[0],
    new CombatDecisionProfile());
Assert(threatState.AllEnemiesDefeated
       && threatState.ActiveBlockableThreat(1d) == 0d
       && threatState.EvaluateLeaf(new CombatDecisionProfile()).DeathRisk == 0d,
    "defeated enemies no longer contribute incoming threat");

var selectiveCloneSource = new CombatSimulationState
{
    PlayerRuntimeId = 82,
    PlayerHp = 20,
    PlayerMaxHp = 20,
    HandCount = 1,
    HandCardValues = { 1d },
    DrawPileValues = { 2d },
    DiscardPileValues = { 3d },
    Features = { ["drawPerTurn"] = 1d },
    Enemies =
    [
        new CombatSimulationUnit { RuntimeId = 83, Hp = 10, MaxHp = 10 }
    ],
    UsedActionWords = new ulong[1]
};
var skillAction = new CombatActionObservation
{
    CandidateId = "allocation-skill",
    Kind = CombatActionKind.UseSkill,
    TargetRuntimeId = 83,
    TargetKind = CombatTargetKind.Enemy,
    Semantics = new CombatActionSemantics { Damage = 1d }
};
var selectiveCloneResult = CombatForwardModel.Apply(
    selectiveCloneSource,
    skillAction,
    0,
    CombatForwardModel.Resolve(root: threatRoot, action: skillAction).Outcomes[0],
    new CombatDecisionProfile());
Assert(ReferenceEquals(
           selectiveCloneSource.HandCardValues,
           selectiveCloneResult.HandCardValues)
       && ReferenceEquals(
           selectiveCloneSource.DrawPileValues,
           selectiveCloneResult.DrawPileValues)
       && ReferenceEquals(
           selectiveCloneSource.Features,
           selectiveCloneResult.Features)
       && !ReferenceEquals(
           selectiveCloneSource.Enemies,
           selectiveCloneResult.Enemies)
       && selectiveCloneSource.Enemies[0].Hp == 10
       && selectiveCloneResult.Enemies[0].Hp == 9,
    "forward transitions share immutable card/feature buffers while isolating mutable combat units");
var playAction = new CombatActionObservation
{
    CandidateId = "allocation-card",
    Kind = CombatActionKind.PlayCard,
    SourceId = "allocation-card"
};
var playCloneResult = CombatForwardModel.Apply(
    selectiveCloneSource,
    playAction,
    0,
    CombatForwardModel.Resolve(threatRoot, playAction).Outcomes[0],
    new CombatDecisionProfile());
Assert(!ReferenceEquals(
           selectiveCloneSource.HandCardValues,
           playCloneResult.HandCardValues)
       && !ReferenceEquals(
           selectiveCloneSource.DiscardPileValues,
           playCloneResult.DiscardPileValues)
       && selectiveCloneSource.HandCardValues.Count == 1
       && selectiveCloneSource.DiscardPileValues.Count == 1
       && playCloneResult.HandCardValues.Count == 0
       && playCloneResult.DiscardPileValues.Count == 2,
    "card-pile mutations use copy-on-write and never change the parent search node");

var coverageProfile = new CombatDecisionProfile
{
    SearchBudgetMode = "fixed",
    SearchSimulationBudget = 1,
    SearchNodeBudget = 512,
    SearchMaxPly = 4
};
var coverageState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 90,
        CurrentHp = 20,
        MaxHp = 20
    },
    CurrentPower = 1,
    MaxPower = 1,
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 91,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 20,
            MaxHp = 20
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "coverage-a",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 901,
            Cost = 1,
            TargetRuntimeId = 91,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 2d }
        },
        new CombatActionObservation
        {
            CandidateId = "coverage-b",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 902,
            Cost = 1,
            TargetRuntimeId = 91,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 3d }
        },
        new CombatActionObservation
        {
            CandidateId = "coverage-end",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var coverageDecision = new CombatDecisionEngine().Choose(coverageState, coverageProfile);
Assert(coverageDecision.SearchAlgorithm == "risk-aware-root-sampling-puct-mpc"
       && coverageDecision.SearchSimulations >= 2
       && coverageDecision.Candidates
           .Where(candidate => candidate.Action.Kind != CombatActionKind.EndTurn)
           .All(candidate => candidate.PlanScore != 0d),
    "risk-aware root-sampling PUCT gives every legal root action search evidence");
var earlyStopProfile = new CombatDecisionProfile
{
    SearchBudgetMode = "fixed",
    SearchSimulationBudget = 128,
    SearchMinimumSimulations = 4,
    SearchStabilityWindow = 2,
    SearchStableChecks = 1,
    SearchNodeBudget = 512,
    SearchMaxPly = 4
};
var earlyStopDecision = new CombatDecisionEngine().Choose(coverageState, earlyStopProfile);
Assert(earlyStopDecision.SearchStoppedEarly
       && earlyStopDecision.SearchSimulations < earlyStopProfile.SearchSimulationBudget,
    "risk-aware root-sampling PUCT stops when the root ranking and graph have stabilized");
var sparseTailStatistics = new CombatSearchRiskStatistics();
sparseTailStatistics.Record(-40d, 0d);
var sparseTailEstimate = sparseTailStatistics.Estimate(0.1d);
Assert(sparseTailEstimate.TailConfidence == 0d
       && sparseTailEstimate.EffectiveLowerTailMean == sparseTailEstimate.Mean,
    "tail-risk search shrinks a single return sample fully back to the mean");
var highMeanStatistics = new CombatSearchRiskStatistics();
var lowMeanStatistics = new CombatSearchRiskStatistics();
for (var index = 0; index < 80; index++)
{
    highMeanStatistics.Record(index < 8 ? 0d : 100d, 0d);
    lowMeanStatistics.Record(index < 8 ? 1d : 10d, 0d);
}
var rankingProfile = new CombatDecisionProfile
{
    TailRiskPenalty = 0d,
    UncertaintyPenalty = 0d
};
var highMeanValue = CombatRiskAdjustedSearchValue.Calculate(
    highMeanStatistics.Estimate(0.1d),
    0d,
    rankingProfile);
var lowMeanValue = CombatRiskAdjustedSearchValue.Calculate(
    lowMeanStatistics.Estimate(0.1d),
    0d,
    rankingProfile);
Assert(highMeanStatistics.Estimate(0.1d).RawLowerTailMean
       < lowMeanStatistics.Estimate(0.1d).RawLowerTailMean
       && highMeanValue > lowMeanValue,
    "root ranking uses the configured mean-tail objective instead of lexicographic raw CVaR");

var targetVariantState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 95,
        CurrentHp = 20,
        MaxHp = 20
    },
    Enemies =
    {
        new CombatUnitObservation { RuntimeId = 96, Kind = CombatTargetKind.Enemy, CurrentHp = 10, MaxHp = 10 },
        new CombatUnitObservation { RuntimeId = 97, Kind = CombatTargetKind.Enemy, CurrentHp = 10, MaxHp = 10 }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "same-card:96",
            RuntimeId = 950,
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 96,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 2d }
        },
        new CombatActionObservation
        {
            CandidateId = "same-card:97",
            RuntimeId = 950,
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 97,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 2d }
        },
        new CombatActionObservation { CandidateId = "same-card-end", Kind = CombatActionKind.EndTurn }
    }
};
var targetVariantDecision = new CombatDecisionEngine().Choose(
    targetVariantState,
    new CombatDecisionProfile
    {
        SearchBudgetMode = "fixed",
        SearchSimulationBudget = 128,
        SearchNodeBudget = 512
    });
Assert(targetVariantDecision.Plan.Count(step => step.CandidateId.StartsWith("same-card:", StringComparison.Ordinal)) <= 1,
    "target variants of one runtime card share a single-use group");

var transpositionState = new CombatStateObservation
{
    Player = new CombatUnitObservation { RuntimeId = 98, CurrentHp = 20, MaxHp = 20 },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 99,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 20,
            MaxHp = 20
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "transpose-a",
            RuntimeId = 980,
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 99,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 1d }
        },
        new CombatActionObservation
        {
            CandidateId = "transpose-b",
            RuntimeId = 981,
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 99,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 1d }
        },
        new CombatActionObservation { CandidateId = "transpose-end", Kind = CombatActionKind.EndTurn }
    }
};
var transpositionDecision = new CombatDecisionEngine().Choose(
    transpositionState,
    new CombatDecisionProfile
    {
        SearchBudgetMode = "fixed",
        SearchSimulationBudget = 256,
        SearchNodeBudget = 1024,
        SearchMaxPly = 4
    });
Assert(transpositionDecision.SearchTranspositionHits > 0,
    "commutative action orders reuse a physical-state transposition node");

var persistentShieldTurn = CombatForwardModel.ApplyEndTurn(
    new CombatSimulationState
    {
        PlayerHp = 30,
        PlayerMaxHp = 30,
        PlayerDefend = 12,
        Power = 0,
        MaxPower = 3,
        HandCount = 2,
        HandLimit = 5,
        HandCardValues = { 1d, 2d },
        DiscardPileValues = { 3d },
        DrawPileKnown = true,
        Features = { ["drawPerTurn"] = 2d },
        Threats =
        [
            new CombatSimulationThreat
            {
                BlockableDamage = 7d,
                Probability = 1d
            }
        ]
    },
    new CombatDecisionProfile());
Assert(persistentShieldTurn.PlayerHp == 30
       && persistentShieldTurn.PlayerDefend == 5
       && persistentShieldTurn.Power == 3
       && persistentShieldTurn.HandCount == 2
       && persistentShieldTurn.HandCardValues.Count == 2
       && persistentShieldTurn.DrawPileValues.Count == 1
       && persistentShieldTurn.DiscardPileValues.Count == 0
       && persistentShieldTurn.Threats.Length == 0,
    "end-turn baseline spends only incoming shield and models discard, reshuffle, and next-turn draw");
var retainedCycleTurn = CombatForwardModel.ApplyEndTurn(
    new CombatSimulationState
    {
        PlayerHp = 20,
        PlayerMaxHp = 20,
        HandCount = 2,
        HandLimit = 5,
        HandCardValues = { 1d, 2d },
        RetainedHandCardValues = { 2d },
        DrawPileValues = { 4d, 5d },
        DrawPileKnown = true,
        Features = { ["drawPerTurn"] = 2d }
    },
    new CombatDecisionProfile());
Assert(retainedCycleTurn.HandCount == 3
       && retainedCycleTurn.HandCardValues.Contains(2d)
       && retainedCycleTurn.RetainedHandCardValues.Count == 1
       && retainedCycleTurn.DiscardPileValues.SequenceEqual(new[] { 1d }),
    "end-turn cycle keeps retained cards while only unretained cards enter the discard pile");

using (CombatAiRegistry.RegisterEffectResolver(
           "Tests",
           "ChanceDamage",
           new FixedEffectResolver("chance-action"),
           100))
{
    var chanceAction = new CombatActionObservation
    {
        CandidateId = "chance-action",
        Semantics = new CombatActionSemantics { RandomOutcome = true }
    };
    var chanceModel = CombatForwardModel.Resolve(coverageState, chanceAction);
    Assert(chanceModel.Outcomes.Count == 2
           && Math.Abs(chanceModel.Outcomes.Sum(outcome => outcome.Probability) - 1d) < 0.000001d,
        "content effect resolvers provide normalized chance outcomes");
}

var simulationRules = BuildSimulationRuleset();
Assert(simulationRules.Success
       && simulationRules.Ruleset.CardCount == 4
       && simulationRules.Ruleset.EnemyCount == 1
       && simulationRules.Ruleset.StatusCount == 2,
    "headless combat ruleset freezes authoritative card, enemy and status definitions");
simulationRules.Ruleset.TryGetCard("strike", out var mutableCardSnapshot);
mutableCardSnapshot.Cost = 99;
simulationRules.Ruleset.TryGetCard("strike", out var unchangedCardSnapshot);
Assert(unchangedCardSnapshot.Cost == 1,
    "frozen ruleset lookups return defensive definition snapshots");
var simulationScenario = BuildSimulationScenario(seed: 42UL, CombatSimulationTraceLevel.Full);
var simulationEngine = new CombatSimulationEngine();
var firstSimulation = simulationEngine.Run(
    simulationScenario,
    simulationRules.Ruleset,
    new GreedyCombatSimulationPolicy());
var repeatedSimulation = simulationEngine.Run(
    BuildSimulationScenario(seed: 42UL, CombatSimulationTraceLevel.Full),
    simulationRules.Ruleset,
    new GreedyCombatSimulationPolicy());
Assert(firstSimulation.Outcome == CombatSimulationOutcome.Victory
       && firstSimulation.TerminationReason == CombatTerminationReason.Victory
       && firstSimulation.SemanticCoverage == 1d
       && firstSimulation.Metrics.CardsPlayed > 0
       && firstSimulation.Events.Any(item => item.Kind == CombatSimulationEventKind.CardPlayed)
       && firstSimulation.Events.Any(item => item.Kind == CombatSimulationEventKind.IntentSelected),
    "headless engine runs the complete player/enemy turn lifecycle and records a causal trace");
Assert(firstSimulation.Events
           .Where(item => item.Kind == CombatSimulationEventKind.IntentSelected)
           .GroupBy(item => new { item.Turn, item.SourceActorId })
           .Any(group => group.Count() == 2
                         && group.Select(item => item.DefinitionId)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Count() == 2),
    "an enemy turn pops and records every ordered intent slot without replacement");
Assert(firstSimulation.FinalStateHash == repeatedSimulation.FinalStateHash
       && firstSimulation.Events.Select(item => item.Kind)
           .SequenceEqual(repeatedSimulation.Events.Select(item => item.Kind)),
    "same ruleset, scenario and seed produce a deterministic combat replay");
var matchingTrace = CombatTraceComparer.Compare(firstSimulation.Events, repeatedSimulation.Events);
var changedTraceEvent = repeatedSimulation.Events.First(item =>
    item.Kind == CombatSimulationEventKind.DamageDealt);
changedTraceEvent.Amount++;
var changedTrace = CombatTraceComparer.Compare(firstSimulation.Events, repeatedSimulation.Events);
Assert(matchingTrace.Equivalent
       && !changedTrace.Equivalent
       && changedTrace.FirstDifference?.Field == "Amount",
    "trace comparison reports the first divergent combat fact");
Assert(firstSimulation.FinalState.Cards.Count
       == firstSimulation.FinalState.DrawPile.Count
          + firstSimulation.FinalState.Hand.Count
          + firstSimulation.FinalState.DiscardPile.Count
          + firstSimulation.FinalState.ExhaustPile.Count,
    "every simulated card instance remains in exactly one authoritative zone");
Assert(firstSimulation.Metrics.BlockGained >= 2
       && firstSimulation.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.BlockGained
           && item.ParentSequence > 0),
    "status triggers enqueue deterministic commands instead of mutating state recursively");
var normalTimingScenario = BuildSimulationScenario(
    seed: 44UL,
    CombatSimulationTraceLevel.Full);
normalTimingScenario.InitialDiscardCards.Add("guard");
normalTimingScenario.DirectHpLossAfterPlayerCard = 1;
normalTimingScenario.MovePlayedCardAfterResolution = false;
var normalTimingResult = simulationEngine.Run(
    normalTimingScenario,
    simulationRules.Ruleset,
    new GreedyCombatSimulationPolicy());
var normalFirstPlayed = normalTimingResult.Events.First(item =>
    item.Kind == CombatSimulationEventKind.CardPlayed);
var normalMoved = normalTimingResult.Events.First(item =>
    item.CardInstanceId == normalFirstPlayed.CardInstanceId
    && (item.Kind == CombatSimulationEventKind.CardDiscarded
        || item.Kind == CombatSimulationEventKind.CardExhausted
        || item.Kind == CombatSimulationEventKind.CardCreated));
Assert(normalMoved.Sequence < normalFirstPlayed.Sequence
       && normalTimingResult.FinalState.Cards.Count
       == normalTimingScenario.Player.Deck.Count + 1
       && normalTimingResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.DamageDealt
           && item.DefinitionId == "difficulty:player-card-hp-loss"),
    "normal timing moves a played card before resolution, seeds the discard pile, and applies per-card hp loss");
var lateTimingScenario = BuildSimulationScenario(
    seed: 44UL,
    CombatSimulationTraceLevel.Full);
lateTimingScenario.MovePlayedCardAfterResolution = true;
var lateTimingResult = simulationEngine.Run(
    lateTimingScenario,
    simulationRules.Ruleset,
    new GreedyCombatSimulationPolicy());
var lateFirstPlayed = lateTimingResult.Events.First(item =>
    item.Kind == CombatSimulationEventKind.CardPlayed);
var lateMoved = lateTimingResult.Events.First(item =>
    item.CardInstanceId == lateFirstPlayed.CardInstanceId
    && (item.Kind == CombatSimulationEventKind.CardDiscarded
        || item.Kind == CombatSimulationEventKind.CardExhausted
        || item.Kind == CombatSimulationEventKind.CardCreated));
Assert(lateFirstPlayed.Sequence < lateMoved.Sequence,
    "late-throw difficulty moves the played card only after its effects resolve");

var lifecycleRules = new CombatRulesetBuilder("card-lifecycle-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "unusable-curse",
        Cost = 0,
        Tags = { "Unusable" },
        DrawEffects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.ModifyVariable,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "DrawMark",
                Amount = 1
            }
        },
        DiscardEffects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.ModifyVariable,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "DropMark",
                Amount = 1
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "burnout-set-hp",
        Cost = 0,
        Exhaust = true,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.SetHp,
                Target = CombatSimulationTarget.Self,
                Amount = 10
            },
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Damage,
                Target = CombatSimulationTarget.SelectedEnemy,
                Amount = 100
            }
        },
        DiscardEffects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.ModifyVariable,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "DropMark",
                Amount = 1
            }
        },
        RequiresEnemyTarget = true
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "lifecycle-dummy",
        MaxHp = 10,
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "wait",
                Weight = 1
            }
        }
    })
    .Freeze();
var unusableCurseResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "unusable-curse-lifecycle",
        RulesetVersion = "card-lifecycle-v1",
        Seed = 1,
        InitialDraw = 1,
        DrawPerTurn = 0,
        TraceLevel = CombatSimulationTraceLevel.Full,
        Player = new CombatPlayerSetup
        {
            RoleId = "tester",
            MaxHp = 30,
            CurrentHp = 30,
            Deck = { "unusable-curse" }
        },
        Enemies =
        {
            new CombatEnemySetup { EnemyId = "lifecycle-dummy" }
        },
        Limits = new CombatSimulationLimits
        {
            MaximumTurns = 1,
            MaximumActions = 10,
            MaximumCommands = 100
        }
    },
    lifecycleRules.Ruleset,
    new GreedyCombatSimulationPolicy());
Assert(lifecycleRules.Success
       && unusableCurseResult.FinalState.Player?.Variables["DrawMark"] == 1d
       && unusableCurseResult.FinalState.Player?.Variables["DropMark"] == 1d
       && !unusableCurseResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.CardPlayed)
       && unusableCurseResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.CardDrawn)
       && unusableCurseResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.CardDiscarded),
    "unusable cards execute native draw and discard scripts without becoming legal plays");
var burnoutSetHpResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "burnout-set-hp",
        RulesetVersion = "card-lifecycle-v1",
        Seed = 2,
        InitialDraw = 1,
        DrawPerTurn = 0,
        TraceLevel = CombatSimulationTraceLevel.Full,
        Player = new CombatPlayerSetup
        {
            RoleId = "tester",
            MaxHp = 30,
            CurrentHp = 30,
            Deck = { "burnout-set-hp" }
        },
        Enemies =
        {
            new CombatEnemySetup { EnemyId = "lifecycle-dummy" }
        }
    },
    lifecycleRules.Ruleset,
    new GreedyCombatSimulationPolicy());
Assert(burnoutSetHpResult.Outcome == CombatSimulationOutcome.Victory
       && burnoutSetHpResult.FinalState.Player?.Hp == 10
       && !burnoutSetHpResult.FinalState.Player!.Variables.ContainsKey("DropMark")
       && burnoutSetHpResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.CardExhausted)
       && !burnoutSetHpResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.Healed),
    "SetHp assigns health directly and burnout bypasses native discard scripts");

var fullHandCreationRules = new CombatRulesetBuilder("full-hand-creation-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "overflow-generator",
        Cost = 0,
        Exhaust = true,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.CreateCard,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "overflow-a",
                Amount = 1,
                DestinationZone = CombatCardZone.Hand
            },
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.CreateCard,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "overflow-b",
                Amount = 1,
                DestinationZone = CombatCardZone.Hand
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "overflow-filler",
        Cost = 99,
        Tags = { "Unusable" }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "overflow-a",
        Cost = 99,
        Tags = { "Retain", "Unusable" },
        DiscardEffects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.CreateCard,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "overflow-a",
                Amount = 1,
                DestinationZone = CombatCardZone.Hand
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "overflow-b",
        Cost = 99,
        Tags = { "Retain", "Unusable" },
        DiscardEffects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.CreateCard,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "overflow-b",
                Amount = 1,
                DestinationZone = CombatCardZone.Hand
            }
        }
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "overflow-dummy",
        MaxHp = 100,
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "wait",
                Weight = 1
            }
        }
    })
    .Freeze();
var fullHandCreationResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "full-hand-created-card-overflow",
        RulesetVersion = "full-hand-creation-v1",
        Seed = 3,
        InitialDraw = 2,
        DrawPerTurn = 1,
        HandLimit = 2,
        MovePlayedCardAfterResolution = true,
        TraceLevel = CombatSimulationTraceLevel.Full,
        Player = new CombatPlayerSetup
        {
            RoleId = "tester",
            MaxHp = 30,
            CurrentHp = 30,
            Deck = { "overflow-generator", "overflow-filler" }
        },
        Enemies =
        {
            new CombatEnemySetup { EnemyId = "overflow-dummy" }
        },
        Limits = new CombatSimulationLimits
        {
            MaximumTurns = 3,
            MaximumActions = 10,
            MaximumCommands = 100,
            MaximumCommandsPerAction = 25
        }
    },
    fullHandCreationRules.Ruleset,
    new PlayCardOnceThenEndPolicy("overflow-generator"));
var overflowCreated = fullHandCreationResult.Events
    .Where(item =>
        item.Kind == CombatSimulationEventKind.CardCreated
        && (item.DefinitionId == "overflow-a"
            || item.DefinitionId == "overflow-b"))
    .OrderBy(item => item.Sequence)
    .ToList();
var overflowDrawn = fullHandCreationResult.Events
    .Where(item =>
        item.Kind == CombatSimulationEventKind.CardDrawn
        && overflowCreated.Any(created =>
            created.CardInstanceId == item.CardInstanceId))
    .OrderBy(item => item.Sequence)
    .ToList();
Assert(fullHandCreationRules.Success
       && fullHandCreationResult.Outcome != CombatSimulationOutcome.Invalid
       && overflowCreated.Count == 2
       && overflowCreated[0].DefinitionId == "overflow-a"
       && overflowCreated[1].DefinitionId == "overflow-b"
       && !fullHandCreationResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.CardDiscarded
           && overflowCreated.Any(created =>
               created.CardInstanceId == item.CardInstanceId))
       && overflowDrawn.Count == 2
       && overflowDrawn[0].DefinitionId == "overflow-b"
       && overflowDrawn[1].DefinitionId == "overflow-a",
    "cards created into a full hand stack on the draw-pile top without firing discard effects");

var triggerRules = new CombatRulesetBuilder("trigger-semantics-v1")
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "pulse",
        DecayAtRoundEnd = false,
        Triggers =
        {
            new CombatStatusTriggerDefinition
            {
                TriggerId = "every-second-action",
                EventKind = CombatSimulationEventKind.ActionStarted,
                OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                EveryNthEvent = 2,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 2
                    }
                }
            }
        }
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "bleed",
        DecayAtRoundEnd = false,
        Triggers =
        {
            new CombatStatusTriggerDefinition
            {
                TriggerId = "bleed-action",
                EventKind = CombatSimulationEventKind.ActionStarted,
                OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.DirectHpLoss,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "bleed",
                        AmountExpression = new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.SourceStatusStacks,
                            Key = "bleed"
                        }
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.DirectHpLoss,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "bleed",
                        AmountExpression = new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.SourceStatusStacks,
                            Key = "bleed"
                        },
                        ConditionExpression = new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.GreaterThan,
                            Arguments =
                            {
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                    Key = "bleed"
                                },
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.Constant,
                                    Constant = 30
                                }
                            }
                        }
                    }
                }
            }
        }
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "bleed-ward",
        DecayAtRoundEnd = false,
        MaximumStacks = 1,
        DynamicModifiersPerStack =
        {
            ["DirectHpLossTaken.bleed"] = -1d
        }
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "counter",
        DecayAtRoundEnd = false,
        Triggers =
        {
            new CombatStatusTriggerDefinition
            {
                TriggerId = "counter-attack",
                EventKind = CombatSimulationEventKind.ActionStarted,
                OwnerRelation = CombatStatusTriggerOwnerRelation.EventTarget,
                RequiredActionTag = "Attack",
                ConsumeStacks = int.MaxValue,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.EventSource,
                        DefinitionId = "counter",
                        AmountExpression = new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.SourceStatusStacks,
                            Key = "counter"
                        }
                    }
                }
            }
        }
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "armor",
        DecayAtRoundEnd = false,
        MaximumStacks = 1,
        DynamicModifiersPerStack =
        {
            ["ConversionRate"] = 1d
        }
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "stasis",
        DecayAtRoundEnd = true,
        ReducePerTurn = 1,
        Triggers =
        {
            new CombatStatusTriggerDefinition
            {
                TriggerId = "skip-round",
                EventKind = CombatSimulationEventKind.TurnStarted,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.SkipTurn,
                        Target = CombatSimulationTarget.Self,
                        Amount = 1
                    }
                }
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "wait-a",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.GainBlock,
                Target = CombatSimulationTarget.Self,
                Amount = 0
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "wait-b",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.GainBlock,
                Target = CombatSimulationTarget.Self,
                Amount = 0
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "overheal",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Heal,
                Target = CombatSimulationTarget.Self,
                Amount = 10
            }
        }
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "attacker",
        MaxHp = 5,
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "attack",
                Tags = { "Attack" },
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.Player,
                        Amount = 3
                    }
                }
            }
        }
    })
    .Freeze();
var triggerResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "trigger-semantics",
        RulesetVersion = "trigger-semantics-v1",
        Seed = 5,
        InitialDraw = 2,
        DrawPerTurn = 0,
        Player = new CombatPlayerSetup
        {
            MaxHp = 20,
            CurrentHp = 20,
            Deck = { "wait-a", "wait-b" },
            InitialStatuses =
            {
                new CombatInitialStatus { StatusId = "pulse" },
                new CombatInitialStatus { StatusId = "bleed", Stacks = 31 },
                new CombatInitialStatus { StatusId = "bleed-ward" },
                new CombatInitialStatus { StatusId = "counter", Stacks = 5 }
            }
        },
        Enemies =
        {
            new CombatEnemySetup { EnemyId = "attacker" }
        },
        TraceLevel = CombatSimulationTraceLevel.Full
    },
    triggerRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
var pulseState = triggerResult.FinalState.Player?.Statuses.Single(item =>
    item.StatusId == "pulse");
var overhealScenario = new CombatScenarioDefinition
{
    ScenarioId = "overheal-conversion",
    RulesetVersion = "trigger-semantics-v1",
    Player = new CombatPlayerSetup
    {
        MaxHp = 20,
        CurrentHp = 20,
        Deck = { "overheal" }
    },
    Enemies =
    {
        new CombatEnemySetup { EnemyId = "attacker" }
    }
};
var overhealState = new CombatBattleState
{
    Turn = 1,
    Phase = CombatSimulationPhase.PlayerAction,
    PlayerActorId = 1,
    NextActorId = 3,
    NextCardInstanceId = 2,
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            Kind = CombatSimulationActorKind.Player,
            Hp = 20,
            MaxHp = 20,
            Energy = 3,
            BaseEnergy = 3,
            Statuses =
            {
                new CombatStatusState { StatusId = "armor", Stacks = 1 }
            }
        },
        new CombatActorState
        {
            ActorId = 2,
            Kind = CombatSimulationActorKind.Enemy,
            DefinitionId = "attacker",
            Hp = 5,
            MaxHp = 5
        }
    },
    Cards =
    {
        new CombatCardInstanceState { InstanceId = 1, CardId = "overheal" }
    },
    Hand = { 1 }
};
var overhealAction = simulationEngine.GetLegalPlayerActions(
        overhealScenario,
        triggerRules.Ruleset,
        overhealState)
    .Single(item => item.Kind == CombatSimulationActionKind.PlayCard);
var overhealResult = simulationEngine.ForkAndApplyPlayerAction(
    overhealScenario,
    triggerRules.Ruleset,
    overhealState,
    overhealAction);
var stasisScenario = new CombatScenarioDefinition
{
    ScenarioId = "stasis-skip",
    RulesetVersion = "trigger-semantics-v1",
    Seed = 6,
    InitialDraw = 2,
    DrawPerTurn = 2,
    Player = new CombatPlayerSetup
    {
        MaxHp = 20,
        CurrentHp = 20,
        Deck = { "wait-a", "wait-b" },
        InitialStatuses =
        {
            new CombatInitialStatus { StatusId = "stasis" }
        }
    },
    Enemies =
    {
        new CombatEnemySetup { EnemyId = "attacker" }
    },
    Limits = new CombatSimulationLimits { MaximumTurns = 3 }
};
var stasisResult = simulationEngine.Run(
    stasisScenario,
    triggerRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
Assert(triggerRules.Success
       && triggerResult.Outcome == CombatSimulationOutcome.Victory
       && triggerResult.FinalPlayerHp == 19
       && triggerResult.Metrics.BlockGained == 2
       && pulseState?.TriggerCounts["every-second-action"] == 2
       && triggerResult.Events.Count(item =>
           item.Kind == CombatSimulationEventKind.DamageDealt
           && item.DefinitionId == "bleed"
           && item.Amount == 0) == 4
       && triggerResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.DamageDealt
           && item.DefinitionId == "counter"
           && item.Amount == 5)
        && triggerResult.FinalState.Player?.Statuses.All(item =>
            item.StatusId != "counter") == true
        && overhealResult.Success
       && overhealResult.State.Player?.Block == 10
       && stasisResult.TurnsSummary.First().Actions == 0
       && stasisResult.Events.First(item =>
           item.Kind == CombatSimulationEventKind.CardPlayed).Turn == 2,
    "status triggers preserve owner relation, nth-event counters, conditional effects, damage filters, and enemy pre-action counterattacks");

var ritualRules = new CombatRulesetBuilder("ritual-semantics-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "ritual",
        Cost = 0,
        Tags = { "Ritual" },
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.GainBlock,
                Target = CombatSimulationTarget.Self,
                Amount = 0
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "roll",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.EmitEvent,
                Target = CombatSimulationTarget.Self,
                Amount = 1,
                DefinitionId = "roll",
                EmittedEventKind = CombatSimulationEventKind.DiceChecked
            },
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.GainBlock,
                Target = CombatSimulationTarget.Self,
                Amount = 1,
                RandomChoiceGroup = "roll-result",
                RandomChoiceWeight = 1
            },
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.GainBlock,
                Target = CombatSimulationTarget.Self,
                Amount = 2,
                RandomChoiceGroup = "roll-result",
                RandomChoiceWeight = 1
            }
        }
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "echo",
        DecayAtRoundEnd = false
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "ritual-cycle",
        DecayAtRoundEnd = false,
        Tags = { "Ritual" },
        Triggers =
        {
            new CombatStatusTriggerDefinition
            {
                TriggerId = "fourth-ritual",
                EventKind = CombatSimulationEventKind.ActionStarted,
                OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                RequiredActionTag = "Ritual",
                CounterKey = "ThisCount",
                CounterIncrementMode = CombatStatusCounterIncrementMode.Fixed,
                MinimumCounterValue = 4,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainEnergy,
                        Target = CombatSimulationTarget.Self,
                        AmountExpression = new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.Add,
                            Arguments =
                            {
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                    Key = "ritual-cycle"
                                },
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                    Key = "echo"
                                }
                            }
                        }
                    }
                }
            }
        }
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "fate",
        DecayAtRoundEnd = false,
        Triggers =
        {
            new CombatStatusTriggerDefinition
            {
                TriggerId = "dice-damage",
                EventKind = CombatSimulationEventKind.DiceChecked,
                OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.AllOpponents,
                        Amount = 2
                    }
                }
            }
        }
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "dummy",
        MaxHp = 100,
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "wait",
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 0
                    }
                }
            }
        }
    })
    .Freeze();
var ritualResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "ritual-counter",
        RulesetVersion = "ritual-semantics-v1",
        InitialDraw = 4,
        DrawPerTurn = 0,
        Player = new CombatPlayerSetup
        {
            MaxHp = 20,
            CurrentHp = 20,
            Deck = { "ritual", "ritual", "ritual", "ritual" },
            InitialStatuses =
            {
                new CombatInitialStatus { StatusId = "ritual-cycle", Stacks = 2 },
                new CombatInitialStatus { StatusId = "echo", Stacks = 1 }
            }
        },
        Enemies = { new CombatEnemySetup { EnemyId = "dummy" } },
        Limits = new CombatSimulationLimits { MaximumTurns = 1 }
    },
    ritualRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
var ritualCycleState = ritualResult.FinalState.Player?.Statuses.Single(item =>
    item.StatusId == "ritual-cycle");
var rollScenario = new CombatScenarioDefinition
{
    ScenarioId = "exclusive-dice-choice",
    RulesetVersion = "ritual-semantics-v1",
    Seed = 9,
    InitialDraw = 1,
    DrawPerTurn = 0,
    Player = new CombatPlayerSetup
    {
        MaxHp = 20,
        CurrentHp = 20,
        Deck = { "roll" },
        InitialStatuses = { new CombatInitialStatus { StatusId = "fate" } }
    },
    Enemies = { new CombatEnemySetup { EnemyId = "dummy", HpScale = 0.02d } }
};
var rollResult = simulationEngine.Run(
    rollScenario,
    ritualRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
Assert(ritualRules.Success
       && ritualCycleState?.TriggerCounts["ThisCount"] == 4
       && ritualResult.FinalState.Player?.Energy == 6
       && rollResult.Outcome == CombatSimulationOutcome.Victory
       && rollResult.Metrics.BlockGained is 1 or 2
       && rollResult.Events.Count(item =>
           item.Kind == CombatSimulationEventKind.DiceChecked) == 1,
    "ritual counters, echo scaling, explicit dice checks, and exclusive random branches are deterministic");

var aiSimulation = simulationEngine.Run(
    BuildSimulationScenario(seed: 43UL, CombatSimulationTraceLevel.Actions),
    simulationRules.Ruleset,
    new CombatDecisionSimulationPolicy(
        new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 128,
            SearchNodeBudget = 1024,
            SearchMaxPly = 8
        }));
Assert(aiSimulation.Outcome == CombatSimulationOutcome.Victory
       && aiSimulation.PolicyId.StartsWith("aura-combat-decision:", StringComparison.Ordinal),
    "existing Chance-PUCT decision AI consumes projected headless observations and completes a battle");

var exploratorySimulation = simulationEngine.Run(
    BuildSimulationScenario(seed: 44UL, CombatSimulationTraceLevel.Summary),
    simulationRules.Ruleset,
    new CombatDecisionSimulationPolicy(
        new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 96,
            SearchNodeBudget = 768,
            SearchMaxPly = 6
        },
        exploration: new CombatSelfPlayExplorationOptions
        {
            Probability = 1d,
            Temperature = 1.25d,
            RootDirichletAlpha = 0.30d,
            RootNoiseFraction = 0.25d,
            RandomSeed = 44
        }));
Assert(exploratorySimulation.Metrics.ExplorationDecisions > 0
       && exploratorySimulation.Metrics
              .RootMaximumVisitShareSamples > 0
       && exploratorySimulation.Metrics
              .RootMaximumVisitShareTotal > 0d,
    "self-play injects deterministic Dirichlet noise before root search and records effective exploration telemetry");

var authoritativeTeacherSimulation = simulationEngine.Run(
    BuildSimulationScenario(seed: 45UL, CombatSimulationTraceLevel.Summary),
    simulationRules.Ruleset,
    new CombatAuthoritativeBranchTeacherPolicy(
        new CombatDecisionSimulationPolicy(
            new CombatDecisionProfile
            {
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 96,
                SearchNodeBudget = 768,
                SearchMaxPly = 6
            }),
        new CombatAuthoritativeTeacherOptions
        {
            AuditProbability = 1d,
            RandomSeed = 45
        }));
Assert(authoritativeTeacherSimulation.Metrics
           .AuthoritativeActionsAudited > 0,
    "teacher policy audits projected choices through authoritative immutable action branches");

var branchState = new CombatBattleState
{
    Turn = 1,
    Phase = CombatSimulationPhase.PlayerAction,
    PlayerActorId = 1,
    NextActorId = 3,
    NextCardInstanceId = 2,
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            InstanceKey = "player",
            Kind = CombatSimulationActorKind.Player,
            DefinitionId = "tester",
            Hp = 30,
            MaxHp = 30,
            Energy = 3,
            BaseEnergy = 3
        },
        new CombatActorState
        {
            ActorId = 2,
            InstanceKey = "dummy:branch",
            Kind = CombatSimulationActorKind.Enemy,
            DefinitionId = "dummy",
            Hp = 18,
            MaxHp = 18,
            CurrentIntentId = "hit"
        }
    },
    Cards =
    {
        new CombatCardInstanceState { InstanceId = 1, CardId = "strike" }
    },
    Hand = { 1 }
};
var branchActions = simulationEngine.GetLegalPlayerActions(
    simulationScenario,
    simulationRules.Ruleset,
    branchState);
var branchResult = simulationEngine.ForkAndApplyPlayerAction(
    simulationScenario,
    simulationRules.Ruleset,
    branchState,
    branchActions.First(action => action.Kind == CombatSimulationActionKind.PlayCard));
Assert(branchResult.Success
       && branchState.FindActor(2)?.Hp == 18
       && branchResult.State.FindActor(2)?.Hp == 12
       && branchResult.Events.Any(item => item.Kind == CombatSimulationEventKind.DamageDealt),
    "authoritative action forks support search without mutating the source battle state");

var unsupportedScenario = BuildSimulationScenario(seed: 1UL, CombatSimulationTraceLevel.Summary);
unsupportedScenario.Player.Deck.Add("missing-card");
var unsupportedSimulation = simulationEngine.Run(
    unsupportedScenario,
    simulationRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
Assert(unsupportedSimulation.Outcome == CombatSimulationOutcome.Invalid
       && unsupportedSimulation.TerminationReason == CombatTerminationReason.UnsupportedRule
       && unsupportedSimulation.UnsupportedDefinitions.Contains("card:missing-card"),
    "unknown combat rules fail closed instead of silently contributing zero-value effects");

var duplicateRewardScenario = BuildSimulationScenario(
    seed: 4UL,
    CombatSimulationTraceLevel.Summary);
duplicateRewardScenario.RewardRules.Add(new CombatScenarioRewardRule
{
    RewardId = "stacked-blessing",
    Kind = "Blessing",
    Stacks = 3
});
duplicateRewardScenario.RewardRules.Add(new CombatScenarioRewardRule
{
    RewardId = "stacked-blessing",
    Kind = "Blessing",
    Stacks = 3
});
var duplicateRewardSimulation = simulationEngine.Run(
    duplicateRewardScenario,
    simulationRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
Assert(duplicateRewardSimulation.Outcome == CombatSimulationOutcome.Invalid
       && duplicateRewardSimulation.TerminationReason
       == CombatTerminationReason.InvalidScenario
       && duplicateRewardSimulation.UnsupportedDefinitions.Contains(
           "duplicate-reward-rule:stacked-blessing")
       && duplicateRewardSimulation.RewardVariables.Count == 1,
    "duplicate reward rules fail closed before scripts execute and result projection remains exception-safe");

var reentrantDiscardScenario = BuildSimulationScenario(
    seed: 5UL,
    CombatSimulationTraceLevel.Full);
reentrantDiscardScenario.Limits.MaximumTurns = 1;
var reentrantDiscardSimulation = new CombatSimulationEngine(
        new ReentrantDiscardExtensionFactory())
    .Run(
        reentrantDiscardScenario,
        simulationRules.Ruleset,
        EndTurnSimulationPolicy.Instance);
var reentrantDiscardZones = reentrantDiscardSimulation.FinalState.DrawPile
    .Concat(reentrantDiscardSimulation.FinalState.Hand)
    .Concat(reentrantDiscardSimulation.FinalState.DiscardPile)
    .Concat(reentrantDiscardSimulation.FinalState.ExhaustPile)
    .ToList();
Assert(reentrantDiscardSimulation.Outcome != CombatSimulationOutcome.Invalid
       && reentrantDiscardZones.Count
          == reentrantDiscardSimulation.FinalState.Cards.Count
       && reentrantDiscardZones.Distinct().Count()
          == reentrantDiscardZones.Count
       && !reentrantDiscardSimulation.UnsupportedDefinitions.Any(item =>
           item.StartsWith("state-invariant", StringComparison.Ordinal)),
    "reentrant discard callbacks cannot place one card in multiple zones");

var stackedRewardDefinition = new CombatCampaignDefinition();
stackedRewardDefinition.Rewards.Add(new CombatCampaignRewardDefinition
{
    RewardId = "stacked-blessing",
    Kind = CombatCampaignRewardKind.Blessing,
    FightScript = "noop",
    InitialVariables = new Dictionary<string, string>
    {
        ["counter"] = "7"
    }
});
var stackedRewardState = new CombatCampaignState
{
    Blessings = new List<string>
    {
        "stacked-blessing",
        "stacked-blessing",
        "stacked-blessing"
    }
};
var projectedRewardRules = CombatCampaignRewardRuleProjector.Build(
    stackedRewardDefinition,
    stackedRewardState);
Assert(projectedRewardRules.Count == 1
       && projectedRewardRules[0].RewardId == "stacked-blessing"
       && projectedRewardRules[0].Stacks == 3
       && projectedRewardRules[0].Variables["counter"] == "7",
    "stacked campaign blessings project to one runtime rule with an explicit stack count");

var mismatchedScenario = BuildSimulationScenario(seed: 2UL, CombatSimulationTraceLevel.Summary);
mismatchedScenario.RulesetVersion = "wrong-version";
var mismatchedSimulation = simulationEngine.Run(
    mismatchedScenario,
    simulationRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
Assert(mismatchedSimulation.Outcome == CombatSimulationOutcome.Invalid
       && mismatchedSimulation.TerminationReason == CombatTerminationReason.InvalidScenario,
    "scenario execution rejects a mismatched frozen ruleset version");

var loopScenario = BuildSimulationScenario(seed: 3UL, CombatSimulationTraceLevel.Full);
loopScenario.Player.Deck.Clear();
loopScenario.Player.Deck.Add("loop-seed");
loopScenario.Player.InitialStatuses.Clear();
loopScenario.InitialDraw = 1;
loopScenario.DrawPerTurn = 1;
loopScenario.Limits.MaximumTriggerWavesPerAction = 3;
var loopSimulation = simulationEngine.Run(
    loopScenario,
    simulationRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
Assert(loopSimulation.Outcome == CombatSimulationOutcome.Invalid
       && loopSimulation.TerminationReason == CombatTerminationReason.TriggerLoop
       && loopSimulation.FailureDiagnostics.LimitScope == "trigger-wave"
       && loopSimulation.FailureDiagnostics.RecentCommands.Count > 0
       && !string.IsNullOrWhiteSpace(
           loopSimulation.FailureDiagnostics.PendingCommand),
    "trigger wave budgets terminate self-reinforcing status loops with actionable command diagnostics");

var isolatedRandomA = new CombatRandomCounterState();
var isolatedRandomB = new CombatRandomCounterState();
var deckA1 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomA, "deck.shuffle", out _);
_ = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomA, "enemy.intent:x", out _);
var deckA2 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomA, "deck.shuffle", out _);
var deckB1 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomB, "deck.shuffle", out _);
var deckB2 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomB, "deck.shuffle", out _);
Assert(deckA1 == deckB1 && deckA2 == deckB2,
    "named deterministic random streams isolate deck order from unrelated enemy random calls");

var serialBatch = new CombatBatchRunner().Run(
    new CombatBatchRequest
    {
        Scenario = BuildSimulationScenario(seed: 1UL, CombatSimulationTraceLevel.Summary),
        SeedStart = 100UL,
        SimulationCount = 12,
        MaximumDegreeOfParallelism = 1
    },
    simulationRules.Ruleset,
    new GreedyCombatSimulationPolicyFactory());
var parallelBatch = new CombatBatchRunner().Run(
    new CombatBatchRequest
    {
        Scenario = BuildSimulationScenario(seed: 1UL, CombatSimulationTraceLevel.Summary),
        SeedStart = 100UL,
        SimulationCount = 12,
        MaximumDegreeOfParallelism = 4
    },
    simulationRules.Ruleset,
    new GreedyCombatSimulationPolicyFactory());
Assert(serialBatch.Statistics.CompletedSimulations == 12
       && serialBatch.Statistics.AuthoritativeSimulations == 12
       && serialBatch.Statistics.WinRateLower95 >= 0d
       && serialBatch.Statistics.WinRateUpper95 <= 1d
       && serialBatch.Results.Select(item => item.FinalStateHash)
           .SequenceEqual(parallelBatch.Results.Select(item => item.FinalStateHash)),
    "batch simulation is seed-ordered, parallel-safe and reports bounded Wilson confidence");

using (CombatSimulationRegistry.RegisterProvider(
           "Tests",
           "headless",
           new FixedRulesetProvider(),
           10))
{
    var registryRules = CombatSimulationRegistry.BuildRuleset("registry-v1");
    var repeatedRegistryRules = CombatSimulationRegistry.BuildRuleset("registry-v1");
    Assert(registryRules.Success
           && CombatSimulationRegistry.SnapshotProviderIds().Contains("Tests:headless")
           && registryRules.Ruleset.RulesetHash == repeatedRegistryRules.Ruleset.RulesetHash,
        "content-owned ruleset providers build a stable frozen shared ruleset");
}
using (CombatSimulationRegistry.RegisterScenarioProvider(
           "Tests",
           "scenarios-high",
           new FixedScenarioProvider(77UL),
           10))
using (CombatSimulationRegistry.RegisterScenarioProvider(
           "Tests",
           "scenarios-low",
           new FixedScenarioProvider(12UL),
           0))
{
    var registeredScenarios = CombatSimulationRegistry.SnapshotScenarios();
    Assert(registeredScenarios.Count == 1
           && registeredScenarios[0].ScenarioId == "registered-headless"
           && registeredScenarios[0].Seed == 77UL,
        "content-owned scenario providers publish cloned headless scenarios");
}
var sourceDocumentRules = BuildSimulationRuleset().Ruleset;
var documentRules = CombatSimulationRegistry.BuildRuleset(new CombatRulesetDocument
{
    Version = "document-v1",
    Cards = sourceDocumentRules.SnapshotCards().ToList(),
    Enemies = sourceDocumentRules.SnapshotEnemies().ToList(),
    Statuses = sourceDocumentRules.SnapshotStatuses().ToList()
});
Assert(documentRules.Success
       && documentRules.Ruleset.CardCount == sourceDocumentRules.CardCount
       && documentRules.Ruleset.EnemyCount == sourceDocumentRules.EnemyCount,
    "file-backed ruleset documents use the same validated builder path");

var journeyDefinition = new CombatJourneyDefinition
{
    JourneyId = "base-game-shaped-journey",
    RulesetVersion = "test-v1",
    Player = new CombatPlayerSetup
    {
        RoleId = "tester",
        MaxHp = 30,
        CurrentHp = 30,
        BaseEnergy = 3,
        Deck = { "strike", "strike", "guard", "insight" }
    },
    Stages =
    {
        new CombatJourneyStageDefinition
        {
            StageId = "ordinary-1",
            EncounterPool = { "dummy" }
        },
        new CombatJourneyStageDefinition
        {
            StageId = "ordinary-2",
            EncounterPool = { "dummy" }
        },
        new CombatJourneyStageDefinition
        {
            StageId = "final-boss",
            EncounterPool = { "dummy" },
            IsBoss = true,
            OfferRewardAfterVictory = false
        }
    },
    RewardPool =
    {
        new CombatRewardCardDefinition
        {
            CardId = "strike",
            BaseValue = 0.5d,
            Features = { ["burst"] = 1d, ["reliability"] = 0.5d }
        },
        new CombatRewardCardDefinition
        {
            CardId = "guard",
            BaseValue = 0.5d,
            Features = { ["defense"] = 1d, ["reliability"] = 1d }
        },
        new CombatRewardCardDefinition
        {
            CardId = "insight",
            BaseValue = 0.25d,
            Features = { ["draw"] = 1d, ["cycling"] = 1d }
        }
    },
    RolePrior = { ["burst"] = 0.2d },
    BuildTendency = { ["defense"] = 0.2d },
    BossPreference = { ["reliability"] = 0.5d },
    RewardChoices = 3,
    TraceLevel = CombatSimulationTraceLevel.Summary,
    Limits = new CombatSimulationLimits
    {
        MaximumTurns = 20,
        MaximumActions = 100,
        MaximumCommands = 1000
    }
};
var firstWorldPlan = CombatJourneyWorldPlanner.Build(journeyDefinition, 90210UL);
var repeatedWorldPlan = CombatJourneyWorldPlanner.Build(journeyDefinition, 90210UL);
Assert(firstWorldPlan.PlanHash == repeatedWorldPlan.PlanHash
       && firstWorldPlan.Encounters.Select(item => item.EnemyId)
           .SequenceEqual(repeatedWorldPlan.Encounters.Select(item => item.EnemyId))
       && firstWorldPlan.Encounters.SelectMany(item => item.RewardOffer)
           .SequenceEqual(repeatedWorldPlan.Encounters.SelectMany(item => item.RewardOffer)),
    "journey world planning isolates deterministic encounter and reward streams");

var journeyRunner = new CombatJourneyRunner();
var pairedJourney = journeyRunner.RunPaired(
    journeyDefinition,
    90210UL,
    sourceDocumentRules,
    new GreedyCombatSimulationPolicyFactory(),
    new GreedyCombatSimulationPolicyFactory());
Assert(pairedJourney.Baseline.JourneyVictory
       && pairedJourney.Learned.JourneyVictory
       && pairedJourney.Baseline.ReachedBoss
       && pairedJourney.Baseline.CompletedBattles == 3
       && pairedJourney.Baseline.FinalDeck.Count == journeyDefinition.Player.Deck.Count + 2
       && pairedJourney.Baseline.Rewards.All(item => item.Scores.Count == 3)
       && pairedJourney.Baseline.Rewards.Select(item => item.SelectedCardId)
           .SequenceEqual(pairedJourney.Learned.Rewards.Select(item => item.SelectedCardId)),
    "paired journeys share a world plan, carry hp and deck growth, and explain reward choices");

CombatJourneyCheckpoint? interruptedCheckpoint = null;
using (var stopAfterFirstBattle = new CancellationTokenSource())
{
    try
    {
        journeyRunner.Run(
            journeyDefinition,
            firstWorldPlan,
            sourceDocumentRules,
            new GreedyCombatSimulationPolicyFactory(),
            checkpointSink: checkpoint =>
            {
                interruptedCheckpoint = checkpoint;
                stopAfterFirstBattle.Cancel();
            },
            cancellationToken: stopAfterFirstBattle.Token);
    }
    catch (OperationCanceledException)
    {
    }
}
Assert(interruptedCheckpoint?.NextEncounterIndex == 1
       && interruptedCheckpoint.Deck.Count == journeyDefinition.Player.Deck.Count + 1,
    "journey checkpoints persist inherited hp, deck and the next encounter boundary");
var resumedJourney = journeyRunner.Run(
    journeyDefinition,
    firstWorldPlan,
    sourceDocumentRules,
    new GreedyCombatSimulationPolicyFactory(),
    interruptedCheckpoint);
Assert(resumedJourney.JourneyVictory
       && resumedJourney.CompletedBattles == pairedJourney.Baseline.CompletedBattles
       && resumedJourney.FinalDeck.SequenceEqual(pairedJourney.Baseline.FinalDeck),
    "journey resume continues from the checkpoint without replaying completed battles");

var liveBattleSamples = new List<CombatTrainingSample>
{
    new()
    {
        GameBuild = "test",
        BattleSessionId = 7001,
        DecisionIndex = 0,
        Sequence = 1,
        StateFingerprint = "live-state-1",
        DecisionProfile = "balanced",
        Selection = new CombatTrainingSelectionTrace
        {
            ExecutedBy = "human",
            ExecutedCandidateId = "card_1:enemy"
        },
        StateFeatures =
        {
            ["playerHp"] = 30d,
            ["playerMaxHp"] = 30d,
            ["turn"] = 1d
        },
        Candidates =
        {
            new CombatTrainingCandidate
            {
                CandidateId = "card_1:enemy",
                SourceId = "card_1",
                Legal = true,
                IsExecutedAction = true,
                Cost = 1,
                Semantics = new CombatActionSemantics { Damage = 5d }
            }
        },
        CompletionState = "Completed",
        CreatedUtc = DateTime.UtcNow.AddSeconds(-1)
    },
    new()
    {
        GameBuild = "test",
        BattleSessionId = 7001,
        DecisionIndex = 1,
        Sequence = 2,
        StateFingerprint = "live-state-2",
        DecisionProfile = "balanced",
        Selection = new CombatTrainingSelectionTrace
        {
            ExecutedBy = "human",
            ExecutedCandidateId = "card_14:enemy"
        },
        StateFeatures =
        {
            ["playerHp"] = 25d,
            ["playerMaxHp"] = 30d,
            ["turn"] = 2d
        },
        Candidates =
        {
            new CombatTrainingCandidate
            {
                CandidateId = "card_14:enemy",
                SourceId = "card_14",
                Legal = true,
                IsExecutedAction = true,
                Cost = 1,
                Semantics = new CombatActionSemantics { Damage = 12d, HitCount = 2d }
            }
        },
        RewardComponents = new CombatTrainingReward { PlayerHpChange = 0d },
        Terminal = true,
        BattleOutcome = "victory",
        TerminalReason = "victory",
        CompletionState = "Completed",
        CreatedUtc = DateTime.UtcNow
    }
};
var assembledLiveEpisodes = CombatLiveEpisodeAssembler.Assemble(liveBattleSamples);
Assert(assembledLiveEpisodes.Count == 1
       && assembledLiveEpisodes[0].Frames.Count == 2
       && assembledLiveEpisodes[0].BattleSessionId == 7001
       && assembledLiveEpisodes[0].Outcome == "victory"
       && assembledLiveEpisodes[0].Provenance == "live-world-simulation",
    "completed live battle samples assemble into an authoritative policy-value episode");
CombatJourneyTrainingProjection.ApplyJourneyReturns(
    assembledLiveEpisodes,
    new[]
    {
        new CombatJourneyTrainingEpisode
        {
            JourneyRunId = "live-journey-1",
            Complete = true,
            Outcome = "defeat",
            Battles =
            {
                new CombatJourneyBattleTrainingRecord
                {
                    BattleIndex = 0,
                    BattleSessionId = 7001,
                    Outcome = "victory"
                },
                new CombatJourneyBattleTrainingRecord
                {
                    BattleIndex = 1,
                    BattleSessionId = 7002,
                    Outcome = "defeat"
                }
            }
        }
    });
Assert(assembledLiveEpisodes[0].JourneyRunId == "live-journey-1"
       && assembledLiveEpisodes[0].JourneyBattleIndex == 0
       && assembledLiveEpisodes[0].Campaign.OutcomeClass == "defeat"
       && assembledLiveEpisodes[0].Campaign.CampaignCompletedBattles == 2
       && assembledLiveEpisodes[0].Frames.All(frame =>
           frame.LongTermReturn < 0d
           && frame.DeathTarget == 1d
           && !frame.StateFeatures.ContainsKey("journeyRemainingBattles")),
    "complete journey outcome replaces local battle return while keeping post-hoc labels out of model features");

var repositoryRoot = Directory.GetCurrentDirectory();
var bundledRulesPath = Path.Combine(
    repositoryRoot,
    "AuraToolsExp",
    "Config",
    "combat-simulation",
    "witch-base-evaluation-v1.ruleset.json");
if (!File.Exists(bundledRulesPath))
{
    repositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    bundledRulesPath = Path.Combine(
        repositoryRoot,
        "AuraToolsExp",
        "Config",
        "combat-simulation",
        "witch-base-evaluation-v1.ruleset.json");
}
var bundledJourneyPath = Path.Combine(
    repositoryRoot,
    "AuraToolsExp",
    "Config",
    "combat-simulation",
    "witch-world-simulation-v1.journey.json");
var bundledJsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};
bundledJsonOptions.Converters.Add(new JsonStringEnumConverter());
var bundledRulesDocument = JsonSerializer.Deserialize<CombatRulesetDocument>(
    File.ReadAllText(bundledRulesPath),
    bundledJsonOptions);
var bundledJourney = JsonSerializer.Deserialize<CombatJourneyDefinition>(
    File.ReadAllText(bundledJourneyPath),
    bundledJsonOptions);
var loadedBundledJourney = bundledJourney
                           ?? throw new InvalidOperationException(
                               "Bundled journey JSON could not be deserialized.");
var bundledRules = CombatSimulationRegistry.BuildRuleset(bundledRulesDocument);
CombatJourneyWorldPlanner.Validate(loadedBundledJourney);
Assert(bundledRules.Success
       && bundledRules.Ruleset.CardCount == 10
       && bundledRules.Ruleset.EnemyCount == 5
       && loadedBundledJourney.Player.RoleId == "career_1"
       && loadedBundledJourney.Stages.Last().EncounterPool.SequenceEqual(
           new[] { "enemy_10022" })
       && loadedBundledJourney.Player.Deck.All(cardId =>
           bundledRules.Ruleset.TryGetCard(cardId, out _))
       && loadedBundledJourney.Stages.SelectMany(stage => stage.EncounterPool)
           .All(enemyId => bundledRules.Ruleset.TryGetEnemy(enemyId, out _))
       && !File.ReadAllText(bundledRulesPath)
           .Contains("Terrias", StringComparison.OrdinalIgnoreCase),
    "bundled standard evaluation package uses only resolvable base-game content");

var bundledCampaignPath = Path.Combine(
    repositoryRoot,
    "AuraToolsExp",
    "Config",
    "combat-simulation",
    "witch-world-simulation-v2.campaign.json");
var bundledRulesV2Path = Path.Combine(
    repositoryRoot,
    "AuraToolsExp",
    "Config",
    "combat-simulation",
    "witch-base-evaluation-v2.ruleset.json");
var bundledCampaign = JsonSerializer.Deserialize<CombatCampaignDefinition>(
    File.ReadAllText(bundledCampaignPath),
    bundledJsonOptions)
    ?? throw new InvalidOperationException(
        "Bundled campaign v2 JSON could not be deserialized.");
var bundledRulesV2Document = JsonSerializer.Deserialize<CombatRulesetDocument>(
    File.ReadAllText(bundledRulesV2Path),
    bundledJsonOptions);
var bundledRulesV2 = CombatSimulationRegistry.BuildRuleset(bundledRulesV2Document);
bundledRulesV2.Ruleset.TryGetStatus(
    "buff_impregnable",
    out var bundledImpregnable);
bundledRulesV2.Ruleset.TryGetStatus(
    "buff_weak",
    out var bundledWeak);
bundledRulesV2.Ruleset.TryGetStatus(
    "buff_ritualbloodsacrifice",
    out var bundledBloodSacrifice);
bundledRulesV2.Ruleset.TryGetStatus(
    "buff_ritualtimeprison",
    out var bundledTimePrison);
bundledRulesV2.Ruleset.TryGetStatus(
    "buff_barkhide",
    out var bundledBarkhide);
bundledRulesV2.Ruleset.TryGetStatus(
    "buff_bloodwall",
    out var bundledBloodWall);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_BlessedByHeaven",
    out var bundledBlessedByHeaven);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_CAR_Momentum",
    out var bundledCarMomentum);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_Dragon'sBlood",
    out var bundledDragonBlood);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_ThirstForBlood",
    out var bundledThirstForBlood);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_Transcendent",
    out var bundledTranscendent);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_AllogeneicConcentric",
    out var bundledAllogeneicConcentric);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_believer",
    out var bundledBeliever);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_expiation",
    out var bundledExpiation);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_fluster",
    out var bundledFluster);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_hunting",
    out var bundledHunting);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_Twins",
    out var bundledTwins);
bundledRulesV2.Ruleset.TryGetStatus(
    "SpecialBuff_UnparalleledPower",
    out var bundledUnparalleledPower);
bundledRulesV2.Ruleset.TryGetCard(
    "ritualcard_1",
    out var bundledRitualSearch);
bundledRulesV2.Ruleset.TryGetCard(
    "timekeeper_16",
    out var bundledFrozenSearch);
bundledRulesV2.Ruleset.TryGetEnemy(
    "enemy_10005",
    out var bundledBloodWallEnemy);
bundledRulesV2.Ruleset.TryGetEnemy(
    "enemy_10022",
    out var bundledSummonerEnemy);
bundledRulesV2.Ruleset.TryGetEnemy(
    "enemy_10056",
    out var bundledHammerEnemy);
bundledRulesV2.Ruleset.TryGetEnemy(
    "enemy_10003",
    out var bundledThiefEnemy);
bundledRulesV2.Ruleset.TryGetCard(
    "luckycard_3",
    out var bundledMoneyThrow);
bundledRulesV2.Ruleset.TryGetCard(
    "cursecard_1",
    out var bundledDrawCurse);
bundledRulesV2.Ruleset.TryGetCard(
    "cursecard_13",
    out var bundledRecurringCurse);
bundledRulesV2.Ruleset.TryGetCard(
    "universalcard_10",
    out var bundledBloodPactStrike);
bundledRulesV2.Ruleset.TryGetCard(
    "nocard_5",
    out var bundledLuckyPrize);
CombatCampaignWorldPlanner.Validate(bundledCampaign);
var bundledCampaignNormal = CombatCampaignWorldPlanner.Build(
    bundledCampaign,
    "normal",
    23816797UL);
var bundledCampaignAdvanced = CombatCampaignWorldPlanner.Build(
    bundledCampaign,
    "advanced",
    23816797UL);
var firstBand = bundledCampaign.Encounters.Where(item =>
    item.NativeBand is 0 or -1).ToList();
Assert(bundledRulesV2.Success
       && bundledRulesV2.Ruleset.CardCount == 228
       && bundledRulesV2.Ruleset.EnemyCount == 55
        && bundledRulesV2.Ruleset.StatusCount == 129
        && bundledRulesV2.Ruleset.SnapshotCards().Count(item =>
            item.Fidelity == CombatRuleFidelity.Authoritative) == 228
        && bundledRulesV2.Ruleset.SnapshotStatuses().Count(item =>
            item.Fidelity == CombatRuleFidelity.Authoritative) == 129
        && bundledRulesV2.Ruleset.SnapshotEnemies().Count(item =>
            item.Fidelity == CombatRuleFidelity.Authoritative) == 55
        && bundledImpregnable.Fidelity == CombatRuleFidelity.Authoritative
       && bundledImpregnable.MaximumStacks == 8
       && bundledImpregnable.DynamicModifiersPerStack["AttackedPercentDamage"] == -0.1d
        && bundledWeak.Fidelity == CombatRuleFidelity.Authoritative
        && bundledWeak.ReducePerTurn == 1
        && bundledBloodSacrifice.Fidelity == CombatRuleFidelity.Authoritative
        && bundledBloodSacrifice.Triggers.Any(item =>
            item.EventKind == CombatSimulationEventKind.CardExhausted
            && item.CounterKey == "ThisCount"
            && item.MinimumCounterValue == 10)
        && bundledTimePrison.Fidelity == CombatRuleFidelity.Authoritative
        && bundledTimePrison.Triggers.Any(item =>
            item.EventKind == CombatSimulationEventKind.DeferredEffectTriggered)
        && bundledBarkhide.Fidelity == CombatRuleFidelity.Authoritative
        && bundledBarkhide.Triggers.Any(item =>
            item.EventKind == CombatSimulationEventKind.DamageDealt
            && item.OwnerRelation == CombatStatusTriggerOwnerRelation.EventTarget
            && item.CounterIncrementMode == CombatStatusCounterIncrementMode.Fixed)
        && bundledBloodWall.Fidelity == CombatRuleFidelity.Authoritative
        && bundledBloodWall.Triggers.Single().Effects.Single().Kind
        == CombatSimulationEffectKind.GainBlock
        && bundledBlessedByHeaven.Fidelity == CombatRuleFidelity.Authoritative
        && bundledBlessedByHeaven.Triggers.Single().MaximumCounterValue == 7
        && bundledCarMomentum.Fidelity == CombatRuleFidelity.Authoritative
        && bundledCarMomentum.Triggers.Single().OwnerRelation
        == CombatStatusTriggerOwnerRelation.Any
        && bundledDragonBlood.Fidelity == CombatRuleFidelity.Authoritative
        && bundledDragonBlood.Triggers.Single().Effects.Any(effect =>
            effect.Kind == CombatSimulationEffectKind.Heal
            && effect.Rounding == CombatSimulationValueRounding.Floor)
        && bundledThirstForBlood.Fidelity == CombatRuleFidelity.Authoritative
        && bundledThirstForBlood.Triggers.Single(item =>
            item.EventKind == CombatSimulationEventKind.BattleStarted)
            .Effects.Single(effect =>
                effect.DefinitionId == "buff_bloodriver").Amount == 3
        && bundledTranscendent.Fidelity == CombatRuleFidelity.Authoritative
        && bundledTranscendent.DynamicModifiersPerStack["AttackedDefaultDamage"] == -4d
        && bundledTranscendent.DynamicModifiersPerStack["AttackedPercentDamage"] == -0.3d
        && bundledTranscendent.DynamicModifiersPerStack["PercentDamage"] == 0.3d
        && bundledTranscendent.Triggers.Single().Effects.Single().Kind
        == CombatSimulationEffectKind.DirectHpLoss
        && bundledAllogeneicConcentric.Fidelity
        == CombatRuleFidelity.Authoritative
        && bundledAllogeneicConcentric.Triggers.Single().OwnerRelation
        == CombatStatusTriggerOwnerRelation.EventTargetAllyExceptSelf
        && bundledAllogeneicConcentric.Triggers.Single().Effects.Any(effect =>
            effect.Kind == CombatSimulationEffectKind.ScaleMaxHpPercent
            && effect.Amount == 150)
        && bundledBeliever.Fidelity == CombatRuleFidelity.Authoritative
        && bundledBeliever.Triggers.Single().Effects.Any(effect =>
            effect.Kind == CombatSimulationEffectKind.ScaleVariablePercent
            && effect.DefinitionId == "PercentDamage"
            && effect.Amount == 130)
        && bundledExpiation.Fidelity == CombatRuleFidelity.Authoritative
        && bundledExpiation.Triggers.Single(item =>
            item.TriggerId == "expiation-fourth-round")
            .MinimumCounterValue == 4
        && bundledFluster.Fidelity == CombatRuleFidelity.Authoritative
        && bundledFluster.Triggers.Single().CounterStepOrigin == 4
        && bundledFluster.Triggers.Single().CounterStep == 3
        && bundledHunting.Fidelity == CombatRuleFidelity.Authoritative
        && bundledHunting.Triggers.Single().Effects.Any(effect =>
            effect.AmountExpression?.Operation
            == CombatSimulationValueOperation.SourceVariable
            && effect.AmountExpression.Key == "BaseAttack")
        && bundledTwins.Fidelity == CombatRuleFidelity.Authoritative
        && bundledTwins.Triggers.Single().MaximumCounterValue == 1
        && bundledUnparalleledPower.Fidelity
        == CombatRuleFidelity.Authoritative
        && bundledUnparalleledPower.Triggers.Single().Effects.Any(effect =>
            effect.Kind == CombatSimulationEffectKind.ScaleVariablePercent
            && effect.Amount == 200)
        && bundledRitualSearch.Fidelity == CombatRuleFidelity.Authoritative
        && bundledRitualSearch.Effects.Single().Kind
        == CombatSimulationEffectKind.RetrieveCards
        && bundledFrozenSearch.Effects.Single().SourceZone
        == CombatCardZone.DiscardPile
        && bundledBloodWallEnemy.InitialStatuses.Count == 2
        && bundledBloodWallEnemy.InitialStatuses.All(item =>
            item.ConditionExpression != null)
        && bundledSummonerEnemy.Intents.Single(item =>
            item.IntentId == "enemycard_Come").Effects.Any(effect =>
                effect.Kind == CombatSimulationEffectKind.SummonEnemy
                && effect.DefinitionId == "enemy_10023")
        && bundledHammerEnemy.Intents.Single(item =>
            item.IntentId == "enemycard_CAR_Hammer").Effects.Any(effect =>
                effect.Kind == CombatSimulationEffectKind.ModifyVariablePercent
                && effect.DefinitionId == "HealMultiplier"
                && effect.Amount == -20)
        && bundledThiefEnemy.Intents.Single(item =>
            item.IntentId == "enemycard_obtainMoney").Effects.Any(effect =>
                effect.Kind == CombatSimulationEffectKind.DeferVariableUntilVictory
                && effect.DefinitionId == "Money"
                && effect.Amount == 15
                && effect.PersistAcrossBattles)
        && bundledMoneyThrow.Fidelity == CombatRuleFidelity.Authoritative
        && bundledMoneyThrow.Effects.Count(effect =>
            effect.Kind == CombatSimulationEffectKind.Damage) == 20
         && bundledMoneyThrow.Effects.Single(effect =>
             effect.Kind == CombatSimulationEffectKind.ModifyVariable)
             .PersistAcrossBattles
         && bundledDrawCurse.Fidelity == CombatRuleFidelity.Authoritative
         && bundledDrawCurse.Tags.Contains(
             "Unusable",
             StringComparer.OrdinalIgnoreCase)
         && bundledDrawCurse.DrawEffects.Single().DefinitionId
         == "buff_vulnerability"
         && bundledRecurringCurse.DiscardEffects.Single().DefinitionId
         == "cursecard_13"
       && bundledBloodPactStrike.Effects.First().Kind
        == CombatSimulationEffectKind.SetHp
       && bundledLuckyPrize.Fidelity == CombatRuleFidelity.Authoritative
       && bundledLuckyPrize.Metadata.GetValueOrDefault(
           "NativeExecution",
           "") == "Script"
        && bundledCampaign.Encounters.Count == 48
       && bundledCampaign.Rewards.Count == 428
       && bundledCampaign.RequireAuthoritativeRules
       && bundledCampaign.InitialMoney == 100
       && bundledCampaign.Player.MaxHp == 100
       && bundledCampaign.Player.CurrentHp == 100
       && bundledCampaign.Player.Deck.SequenceEqual(new[]
       {
           "card_1", "card_2", "card_1", "card_2", "card_1", "card_2",
           "card_2", "burningcard_1", "card_4", "card_3", "burningcard_2",
           "burningcard_2", "elementscard_9", "card_3", "elementscard_1"
       })
       && !bundledCampaign.Difficulties.Single(item =>
           item.DifficultyId == "normal").MovePlayedCardAfterResolution
       && bundledCampaign.Difficulties.Single(item =>
           item.DifficultyId == "advanced").MovePlayedCardAfterResolution
       && bundledCampaign.Difficulties.Single(item =>
           item.DifficultyId == "advanced").EnemyHpMultiplier == 1.4d
       && bundledCampaign.Difficulties.Single(item =>
           item.DifficultyId == "advanced").EnemyAttackMultiplier == 1.4d
       && bundledCampaign.Difficulties.Single(item =>
           item.DifficultyId == "advanced").InitialDiscardCards.Count == 2
       && bundledCampaign.Difficulties.Single(item =>
           item.DifficultyId == "advanced").DirectHpLossAfterPlayerCard == 1
       && bundledCampaign.Difficulties.Single(item =>
           item.DifficultyId == "advanced").AdditionalEnemyHpMultiplier == 3d
       && bundledCampaign.Rewards
           .Where(item => item.Kind == CombatCampaignRewardKind.Card)
           .All(item => item.OfferWeight is 8d or 5d or 2d or 1d)
       && !bundledCampaign.Rewards.Any(item =>
           item.Kind == CombatCampaignRewardKind.Card
           && item.RewardId.StartsWith("curse", StringComparison.OrdinalIgnoreCase))
       && bundledRulesV2.Ruleset.SnapshotEnemies()
           .All(item => item.ActionCount is >= 1 and <= 3)
       && bundledRulesV2.Ruleset.SnapshotEnemies()
           .SelectMany(item => item.Intents)
           .Any(item => item.Effects.Any(effect =>
               effect.Kind == CombatSimulationEffectKind.CreateCard))
       && bundledRulesV2.Ruleset.SnapshotEnemies()
           .SelectMany(item => item.Intents)
           .Any(item => item.Effects.Any(effect =>
               effect.Kind == CombatSimulationEffectKind.AddStatus))
       && bundledRulesV2.Ruleset.SnapshotEnemies()
           .SelectMany(item => item.Intents)
           .Where(item => item.IntentId == "enemycard_FiveHit")
           .All(item => item.Effects.Count(effect =>
               effect.Kind == CombatSimulationEffectKind.Damage) == 5)
       && bundledCampaign.Rewards.Single(item => item.RewardId == "blessing_1")
           .PermanentAttributeBonuses["Strength"] == 2
       && bundledCampaign.Rewards.Single(item => item.RewardId == "blessing_7")
           .MaxHpBonus == 5
       && firstBand.Count(item => item.Kind == CombatCampaignEncounterKind.Normal) == 12
       && firstBand.Count(item => item.Kind == CombatCampaignEncounterKind.Elite) == 8
       && firstBand.Count(item => item.Kind == CombatCampaignEncounterKind.Boss) == 3
       && bundledCampaignNormal.Encounters.Count == 37
       && bundledCampaignNormal.Encounters[36].Kind
       == CombatCampaignEncounterKind.FinalBoss
       && bundledCampaignNormal.Encounters[36].EncounterId is
           "final-caroline-perfect-angel"
           or "final-evernight-incarnation"
           or "final-demon-king"
           or "final-holy-judgment-engine"
       && bundledCampaignNormal.Encounters.Select(item => item.EncounterId)
           .SequenceEqual(
               bundledCampaignAdvanced.Encounters.Select(item => item.EncounterId))
       && bundledCampaignNormal.PlanHash != bundledCampaignAdvanced.PlanHash
       && bundledCampaign.Rewards
           .Where(item => item.Kind == CombatCampaignRewardKind.Blessing
                          && item.Negative)
           .All(item => bundledCampaignNormal.Encounters
               .All(encounter => !string.Equals(
                   encounter.RewardOffer.BlessingId,
                   item.RewardId,
                   StringComparison.OrdinalIgnoreCase)))
       && !File.ReadAllText(bundledCampaignPath)
           .Contains("Terrias", StringComparison.OrdinalIgnoreCase)
       && !File.ReadAllText(bundledCampaignPath)
           .Contains("Saya_", StringComparison.OrdinalIgnoreCase)
       && !File.ReadAllText(bundledRulesV2Path)
           .Contains("Terrias", StringComparison.OrdinalIgnoreCase)
       && !File.ReadAllText(bundledRulesV2Path)
           .Contains("Saya_", StringComparison.OrdinalIgnoreCase),
     "bundled campaign v2 fixes seven layers, base-game pools, positive rewards, final bosses, and paired difficulty worlds");

CombatSimulationResult RunBundledStatusScenario(
    string enemyId,
    string cardId,
    ulong seed,
    int cardCopies = 1,
    int initialDraw = 1,
    int maximumTurns = 1,
    IReadOnlyList<string>? additionalEnemyIds = null)
{
    var deck = Enumerable.Repeat(cardId, Math.Max(1, cardCopies)).ToList();
    var enemyIds = new List<string> { enemyId };
    if (additionalEnemyIds != null)
    {
        enemyIds.AddRange(additionalEnemyIds);
    }
    return new CombatSimulationEngine().Run(
        new CombatScenarioDefinition
        {
            ScenarioId = "bundled-status-" + enemyId,
            RulesetVersion = "witch-base-evaluation-v2",
            Seed = seed,
            InitialDraw = initialDraw,
            DrawPerTurn = initialDraw,
            HandLimit = Math.Max(10, initialDraw),
            RequireAuthoritativeRules = false,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Player = new CombatPlayerSetup
            {
                RoleId = "witch",
                MaxHp = 999,
                CurrentHp = 999,
                BaseEnergy = 20,
                Deck = deck
            },
            Enemies = enemyIds.Select(id =>
                new CombatEnemySetup { EnemyId = id }).ToList(),
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = maximumTurns,
                MaximumActions = 500,
                MaximumCommands = 10000,
                MaximumCommandsPerAction = 1000,
                MaximumTriggerWavesPerAction = 100
            }
        },
        bundledRulesV2.Ruleset,
        new GreedyCombatSimulationPolicy());
}

var barkhideScenario = RunBundledStatusScenario("enemy_10024", "card_1", 2401UL);
var bloodWallScenario = RunBundledStatusScenario("enemy_10005", "cursecard_1", 2402UL);
var blessedScenario = RunBundledStatusScenario("enemy_10041", "cursecard_1", 2403UL);
var momentumScenario = RunBundledStatusScenario("enemy_10056", "cursecard_1", 2404UL);
var dragonScenario = RunBundledStatusScenario("enemy_10059", "cursecard_1", 2405UL);
var thirstScenario = RunBundledStatusScenario("enemy_10039", "cursecard_1", 2406UL);
var transcendentScenario =
    RunBundledStatusScenario("enemy_10027", "cursecard_1", 2407UL);
var dragonEnemy = dragonScenario.FinalState.Actors.Single(actor =>
    actor.Kind == CombatSimulationActorKind.Enemy);
Assert(
    barkhideScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.BlockGained
        && item.DefinitionId == "buff_barkhide"
        && item.Amount == 2),
    "barkhide grants twice the per-turn hit counter as persistent block");
Assert(
    bloodWallScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.BlockGained
        && item.DefinitionId == "buff_bloodwall"
        && item.Amount == 3),
    "blood wall grants its current stacks as block on enemy actions");
Assert(
    blessedScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.StatusAdded
        && item.DefinitionId == "buff_evergreen"
        && item.Amount == 2),
    "blessed by heaven adds floor five percent max hp evergreen on global turn start");
Assert(
    momentumScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.StatusAdded
        && item.DefinitionId == "buff_extraordinary"
        && item.Amount == 20),
    "Caroline momentum triggers on the global turn start");
Assert(
    dragonEnemy.Statuses.Any(item =>
        item.StatusId == "buff_impregnable"
        && item.Stacks >= 1)
    && dragonEnemy.Statuses.Any(item =>
        item.StatusId == "buff_extraordinary"
        && item.Stacks >= 20),
    "dragon blood resolves healing support statuses on the global turn end");
Assert(
    thirstScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.StatusAdded
        && item.DefinitionId == "buff_bloodriver"
        && item.Amount == 3),
    "thirst for blood applies the scripted three blood-river stacks at battle start");
Assert(
    transcendentScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.DamageDealt
        && item.DefinitionId == "SpecialBuff_Transcendent"
        && item.Amount == 4),
    "transcendent actions apply four direct hp loss to every opponent");

var allogeneicScenario = RunBundledStatusScenario(
    "enemy_10005",
    "universalcard_17",
    2411UL,
    additionalEnemyIds: new[] { "enemy_10029" });
var allogeneicEnemy = allogeneicScenario.FinalState.Actors.Single(actor =>
    actor.DefinitionId == "enemy_10029");
Assert(
    allogeneicEnemy.MaxHp == 195
    && allogeneicEnemy.Hp == 195
    && Math.Abs(allogeneicEnemy.Variables["PercentDamage"] - 1.5d) < 0.000001d
    && Math.Abs(allogeneicEnemy.Variables["DefendPercent"] - 1.5d) < 0.000001d,
    "allogeneic concentric scales attack, defense, max hp, and fully heals after an ally dies");

var believerScenario = RunBundledStatusScenario(
    "enemy_10009",
    "universalcard_17",
    2412UL,
    cardCopies: 3,
    initialDraw: 3);
var believerEnemy = believerScenario.FinalState.Actors.Single(actor =>
    actor.Kind == CombatSimulationActorKind.Enemy);
Assert(
    believerEnemy.Hp > believerEnemy.MaxHp / 2
    && Math.Abs(believerEnemy.Variables["PercentDamage"] - 1.3d) < 0.000001d
    && Math.Abs(believerEnemy.Variables["DefendPercent"] - 1.3d) < 0.000001d
    && believerEnemy.Statuses.All(status =>
        status.StatusId != "SpecialBuff_believer"),
    "believer heals and permanently scales attack and defense only after crossing half hp");

var expiationScenario = RunBundledStatusScenario(
    "enemy_10018",
    "cursecard_1",
    2413UL,
    cardCopies: 4,
    maximumTurns: 4);
Assert(
    expiationScenario.Outcome == CombatSimulationOutcome.Victory
    && expiationScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.DamageDealt
        && item.DefinitionId == "SpecialBuff_expiation"
        && item.TargetActorId != expiationScenario.FinalState.PlayerActorId),
    "expiation naturally ends the battle on its fourth round after healing the opponent");

var flusterScenario = RunBundledStatusScenario(
    "enemy_10010",
    "card_1",
    2414UL,
    cardCopies: 5,
    initialDraw: 5);
Assert(
    flusterScenario.Metrics.CardsPlayed == 4
    && flusterScenario.Events.Count(item =>
        item.Kind == CombatSimulationEventKind.CardDiscarded) == 5,
    "fluster ignores the first hit and discards on the fourth, seventh, and later third-hit intervals");

var huntingScenario = RunBundledStatusScenario(
    "enemy_10028",
    "cursecard_11",
    2415UL,
    cardCopies: 2,
    initialDraw: 2);
Assert(
    huntingScenario.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.DamageDealt
        && item.DefinitionId == "SpecialBuff_hunting"
        && item.Amount == 12),
    "hunting reads the witch hand parity and uses the enemy's native attack value");

var twinsScenario = RunBundledStatusScenario(
    "enemy_10005",
    "universalcard_17",
    2416UL,
    additionalEnemyIds: new[] { "enemy_10036" });
var twinsEnemy = twinsScenario.FinalState.Actors.Single(actor =>
    actor.DefinitionId == "enemy_10036");
Assert(
    twinsEnemy.Statuses.Any(status =>
        status.StatusId == "buff_impregnable" && status.Stacks >= 2)
    && twinsEnemy.Statuses.Any(status =>
        status.StatusId == "buff_extraordinary" && status.Stacks >= 30)
    && twinsEnemy.Statuses.Any(status =>
        status.StatusId == "buff_thorns" && status.Stacks >= 3),
    "twins reacts once to an ally death while its owner is still alive");

var unparalleledScenario = RunBundledStatusScenario(
    "enemy_10022",
    "cursecard_1",
    2417UL);
var unparalleledEnemy = unparalleledScenario.FinalState.Actors.Single(actor =>
    actor.DefinitionId == "enemy_10022");
Assert(
    unparalleledEnemy.Hp == 180
    && Math.Abs(unparalleledEnemy.Variables["PercentDamage"] - 2d) < 0.000001d,
    "unparalleled power loses one quarter max hp and doubles current attack at round end");

var moneyObservation = CombatSimulationObservationProjector.Project(
    new CombatSimulationPolicyContext
    {
        Scenario = new CombatScenarioDefinition { HandLimit = 10 },
        Ruleset = bundledRulesV2.Ruleset,
        State = new CombatBattleState
        {
            Turn = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 20,
                    MaxHp = 20,
                    Variables = { ["Money"] = 40 }
                },
                new CombatActorState
                {
                    ActorId = 2,
                    Kind = CombatSimulationActorKind.Enemy,
                    DefinitionId = "enemy_10003",
                    Hp = 100,
                    MaxHp = 100
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = "luckycard_3"
                }
            },
            Hand = { 1 }
        },
        LegalActions = new List<CombatSimulationAction>
        {
            new CombatSimulationAction
            {
                CandidateId = "money-throw",
                Kind = CombatSimulationActionKind.PlayCard,
                CardInstanceId = 1,
                DefinitionId = "luckycard_3",
                Cost = 1
            }
        }
    });
var moneyThrowObservation = moneyObservation.Actions.Single(item =>
    item.CandidateId == "money-throw");
Assert(!moneyObservation.Features.ContainsKey("player.Money")
       && moneyThrowObservation.Semantics.Damage == 12d
       && !moneyThrowObservation.Semantics.StateChanges.ContainsKey("player.Money"),
    "player-equivalent observations exclude campaign money while retaining visible action damage");

foreach (var lateMove in new[] { false, true })
{
    var bloodState = new CombatBattleState
    {
        Turn = 1,
        Phase = CombatSimulationPhase.PlayerAction,
        PlayerActorId = 1,
        NextActorId = 3,
        NextCardInstanceId = 11,
        Actors =
        {
            new CombatActorState
            {
                ActorId = 1,
                InstanceKey = "player",
                Kind = CombatSimulationActorKind.Player,
                DefinitionId = "career_1",
                Hp = 100,
                MaxHp = 100,
                Energy = 999,
                BaseEnergy = 999
            },
            new CombatActorState
            {
                ActorId = 2,
                InstanceKey = "dummy:blood",
                Kind = CombatSimulationActorKind.Enemy,
                DefinitionId = "enemy_1",
                Hp = 100000,
                MaxHp = 100000
            }
        }
    };
    bloodState.Cards.Add(new CombatCardInstanceState
    {
        InstanceId = 1,
        CardId = "ritualcard_14"
    });
    bloodState.Hand.Add(1);
    for (var instanceId = 2; instanceId <= 10; instanceId++)
    {
        bloodState.Cards.Add(new CombatCardInstanceState
        {
            InstanceId = instanceId,
            CardId = "card_1"
        });
        bloodState.Hand.Add(instanceId);
    }

    var bloodScenario = new CombatScenarioDefinition
    {
        ScenarioId = "blood-sacrifice-" + lateMove,
        RulesetVersion = bundledRulesV2.Ruleset.Version,
        MovePlayedCardAfterResolution = lateMove,
        RequireAuthoritativeRules = true
    };
    for (var instanceId = 1; instanceId <= 10; instanceId++)
    {
        var legal = simulationEngine.GetLegalPlayerActions(
            bloodScenario,
            bundledRulesV2.Ruleset,
            bloodState);
        var selected = legal.First(item =>
            item.Kind == CombatSimulationActionKind.PlayCard
            && item.CardInstanceId == instanceId);
        var applied = simulationEngine.ForkAndApplyPlayerAction(
            bloodScenario,
            bundledRulesV2.Ruleset,
            bloodState,
            selected);
        Assert(applied.Success, "blood sacrifice action fork succeeds");
        bloodState = applied.State;
        if (instanceId == 1)
        {
            Assert(bloodState.Hand.All(cardInstanceId =>
                    bloodState.FindCard(cardInstanceId)?.Tags.Contains(
                        "Burnout",
                        StringComparer.OrdinalIgnoreCase) == true),
                "blood sacrifice persistently marks every remaining hand instance");
        }
    }
    Assert(bloodState.ExhaustPile.Count == 10
           && bloodState.Player?.Statuses.All(item =>
               item.StatusId != "buff_ritualbloodsacrifice") == true
           && bloodState.Player?.Statuses.Single(item =>
               item.StatusId == "buff_extraordinary").Stacks == 444,
        "blood sacrifice counts ten actual burns and resolves identically for both card-move timings");
}

var retrievalState = new CombatBattleState
{
    Turn = 1,
    Phase = CombatSimulationPhase.PlayerAction,
    PlayerActorId = 1,
    NextActorId = 3,
    NextCardInstanceId = 5,
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            InstanceKey = "player",
            Kind = CombatSimulationActorKind.Player,
            DefinitionId = "career_1",
            Hp = 100,
            MaxHp = 100,
            Energy = 99,
            BaseEnergy = 99
        },
        new CombatActorState
        {
            ActorId = 2,
            InstanceKey = "dummy:retrieval",
            Kind = CombatSimulationActorKind.Enemy,
            DefinitionId = "enemy_1",
            Hp = 100000,
            MaxHp = 100000
        }
    },
    Cards =
    {
        new CombatCardInstanceState { InstanceId = 1, CardId = "ritualcard_1" },
        new CombatCardInstanceState { InstanceId = 2, CardId = "ritualcard_14" },
        new CombatCardInstanceState { InstanceId = 3, CardId = "card_1" },
        new CombatCardInstanceState { InstanceId = 4, CardId = "ritualcard_17" }
    },
    Hand = { 1 },
    DrawPile = { 2, 3, 4 }
};
var retrievalScenario = new CombatScenarioDefinition
{
    ScenarioId = "tagged-retrieval",
    RulesetVersion = bundledRulesV2.Ruleset.Version,
    RequireAuthoritativeRules = true
};
var retrievalAction = simulationEngine.GetLegalPlayerActions(
        retrievalScenario,
        bundledRulesV2.Ruleset,
        retrievalState)
    .Single(item => item.Kind == CombatSimulationActionKind.PlayCard);
var retrievalApplication = simulationEngine.ForkAndApplyPlayerAction(
    retrievalScenario,
    bundledRulesV2.Ruleset,
    retrievalState,
    retrievalAction);
Assert(retrievalApplication.Success
       && retrievalApplication.State.Hand.Select(instanceId =>
               retrievalApplication.State.FindCard(instanceId)?.CardId)
           .OrderBy(item => item, StringComparer.Ordinal)
           .SequenceEqual(new[] { "ritualcard_14", "ritualcard_17" })
       && retrievalApplication.State.DrawPile.Single() == 3,
    "tagged retrieval moves only matching card instances out of the requested source zone");

var timePrisonRules = new CombatRulesetBuilder("time-prison-semantics-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "defer",
        Cost = 0,
        Tags = { "Inherent" },
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.EmitEvent,
                Target = CombatSimulationTarget.Self,
                Amount = 2,
                DefinitionId = "buff_timelock",
                EmittedEventKind = CombatSimulationEventKind.DeferredEffectTriggered
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "ritual-a",
        Cost = 99,
        Tags = { "Ritual" },
        Effects = { new CombatSimulationEffectDefinition { Kind = CombatSimulationEffectKind.GainBlock } }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "ritual-b",
        Cost = 99,
        Tags = { "Ritual" },
        Effects = { new CombatSimulationEffectDefinition { Kind = CombatSimulationEffectKind.GainBlock } }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "ritual-c",
        Cost = 99,
        Tags = { "Ritual" },
        Effects = { new CombatSimulationEffectDefinition { Kind = CombatSimulationEffectKind.GainBlock } }
    })
    .RegisterStatus(bundledTimePrison)
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "buff_ritualechostaff"
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "dummy",
        MaxHp = 1000,
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "wait",
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self
                    }
                }
            }
        }
    })
    .Freeze();
var timePrisonResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "time-prison-deferred-count",
        RulesetVersion = "time-prison-semantics-v1",
        InitialDraw = 1,
        DrawPerTurn = 0,
        Player = new CombatPlayerSetup
        {
            MaxHp = 30,
            CurrentHp = 30,
            Deck = { "ritual-a", "ritual-b", "ritual-c", "defer" },
            InitialStatuses =
            {
                new CombatInitialStatus
                {
                    StatusId = "buff_ritualtimeprison",
                    Stacks = 1
                },
                new CombatInitialStatus
                {
                    StatusId = "buff_ritualechostaff",
                    Stacks = 1
                }
            }
        },
        Enemies = { new CombatEnemySetup { EnemyId = "dummy" } },
        Limits = new CombatSimulationLimits { MaximumTurns = 2 }
    },
    timePrisonRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
Assert(timePrisonRules.Success
       && timePrisonResult.Events.Count(item =>
           item.Kind == CombatSimulationEventKind.CardDrawn
           && item.DefinitionId.StartsWith("ritual-", StringComparison.Ordinal)) == 3
       && timePrisonResult.FinalState.Player?.Statuses.Single(item =>
               item.StatusId == "buff_ritualtimeprison")
           .TriggerCounts["ThisCount"] == 0,
    "time prison counts actual deferred executions, applies ritual echo repeats, retrieves by tag, and resets its counter");

var bundledCampaignSmoke = new CombatCampaignRunner().Run(
    bundledCampaign,
    bundledCampaignNormal,
    bundledRulesV2.Ruleset,
    new GreedyCombatSimulationPolicyFactory());
Assert(bundledCampaignSmoke.CompletedBattles >= 1
       && !bundledCampaignSmoke.Invalid
       && Math.Abs(bundledCampaignSmoke.BattleSemanticCoverage - 1d)
       < 0.000001d,
    "bundled campaign v2 executes with complete authoritative semantic coverage");
var bundledRuns = Enumerable.Range(0, 8)
    .Select(index => new CombatJourneyRunner().Run(
        loadedBundledJourney,
        CombatJourneyWorldPlanner.Build(
            loadedBundledJourney,
            (ulong)(1000 + index)),
        bundledRules.Ruleset,
        new GreedyCombatSimulationPolicyFactory()))
    .ToList();
Assert(bundledRuns.All(run => !run.Invalid && run.Battles.Count > 0)
       && bundledRuns.Any(run => run.ReachedBoss)
       && bundledRuns.Any(run => run.JourneyVictory)
       && bundledRuns.Select(run => run.PlanHash).Distinct().Count() > 1,
    "bundled world simulation is valid, seed-varied, and can defeat its base-game final boss");

var knowledgePackage = new CombatKnowledgePackage
{
    OwnerId = "Tests",
    PackageId = "authoritative-combat",
    GameBuild = "test-build",
    SourceHash = "test-source-hash",
    Actions =
    {
        new CombatKnowledgeActionDefinition
        {
            SourceId = "elementscard_1",
            Fidelity = CombatKnowledgeFidelity.Authoritative,
            Semantics = new CombatActionSemantics
            {
                Draw = 1d,
                Buff = 2d,
                DamageMultiplierGain = 0.04d,
                StateChanges = { ["status:buff_elements"] = 2d }
            }
        },
        new CombatKnowledgeActionDefinition
        {
            SourceId = "finisher",
            Fidelity = CombatKnowledgeFidelity.Authoritative,
            Semantics = new CombatActionSemantics { Damage = 20d }
        }
    },
    Statuses =
    {
        new CombatKnowledgeStatusDefinition
        {
            StatusId = "buff_elements",
            Fidelity = CombatKnowledgeFidelity.Authoritative
        }
    },
    Enemies =
    {
        new CombatKnowledgeEnemyDefinition
        {
            EnemyId = "enemy_test",
            Fidelity = CombatKnowledgeFidelity.Authoritative
        }
    }
};
using (CombatKnowledgeRegistry.RegisterPackage(
           knowledgePackage,
           out var knowledgeErrors))
{
    var knowledgeState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            Statuses =
            {
                new CombatStatusObservation { StatusId = "buff_elements", Level = 2 }
            }
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                Kind = CombatTargetKind.Enemy,
                DefinitionId = "enemy_test",
                CurrentHp = 10,
                MaxHp = 10
            }
        },
        Actions =
        {
            new CombatActionObservation
            {
                CandidateId = "ocean",
                SourceId = "elementscard_1",
                Kind = CombatActionKind.PlayCard
            }
        }
    };
    CombatAiRegistry.ApplySemantics(knowledgeState, knowledgeState.Actions[0]);
    var coverage = CombatKnowledgeRegistry.EvaluateCoverage(knowledgeState);
    Assert(knowledgeErrors.Count == 0
           && knowledgeState.Actions[0].Semantics.Draw == 1d
           && knowledgeState.Actions[0].Semantics.DamageMultiplierGain == 0.04d
           && knowledgeState.Actions[0].SemanticFidelity
           == CombatKnowledgeFidelity.Authoritative
           && coverage.IsAuthoritative
           && coverage.AuthoritativeCoverage == 1d,
        "versioned combat knowledge overrides heuristics and gates the full live state");

    knowledgeState.Player.Statuses.Add(
        new CombatStatusObservation { StatusId = "buff_unknown", Level = 1 });
    var incompleteCoverage = CombatKnowledgeRegistry.EvaluateCoverage(knowledgeState);
    Assert(!incompleteCoverage.IsAuthoritative
           && incompleteCoverage.UnknownDefinitions.Contains("status:buff_unknown"),
        "knowledge coverage fails closed for an unregistered active buff");

    var drawRoot = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            CurrentHp = 20,
            MaxHp = 20
        },
        HandCount = 1,
        DeckCardIds = { "finisher" },
        DeckKnowledge = new CombatDeckKnowledge
        {
            DrawPileCount = 1,
            KnownDeckCardIds = { "finisher" },
            KnownTopCardIds = { "finisher" }
        }
    };
    var drawAction = new CombatActionObservation
    {
        SourceId = "draw",
        Kind = CombatActionKind.PlayCard,
        Semantics = new CombatActionSemantics { Draw = 1d }
    };
    var drawSimulation = CombatForwardModel.Create(drawRoot, 1);
    var drawOutcome = CombatForwardModel.Resolve(
        drawRoot,
        drawAction,
        useRegisteredResolvers: false).Outcomes[0];
    var afterDraw = CombatForwardModel.Apply(
        drawSimulation,
        drawAction,
        0,
        drawOutcome,
        new CombatDecisionProfile());
    Assert(afterDraw.DrawPileValues.Count == 0
           && afterDraw.HandCount == 1
           && afterDraw.DrawnCardPotential > 0d,
        "forward search values the exact next high-value deck card instead of count-only draw");
}

var expressionRulesBuilder = new CombatRulesetBuilder("expression-v1");
expressionRulesBuilder.RegisterStatus(new CombatStatusDefinition
{
    OwnerModId = "Tests",
    StatusId = "scaling",
    DecayAtRoundEnd = false,
    DynamicModifiersPerStack = { ["PercentDamage"] = 0.1d }
});
var expressionRules = expressionRulesBuilder.Freeze();
var expressionState = new CombatBattleState
{
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            Hp = 10,
            MaxHp = 10,
            Variables = { ["PercentDamage"] = 1d },
            Statuses =
            {
                new CombatStatusState { StatusId = "scaling", Stacks = 2 }
            }
        }
    }
};
var expressionValue = CombatSimulationExpressionEvaluator.Evaluate(
    new CombatSimulationValueExpression
    {
        Operation = CombatSimulationValueOperation.Multiply,
        Arguments =
        {
            new CombatSimulationValueExpression
            {
                Operation = CombatSimulationValueOperation.SourceVariable,
                Key = "PercentDamage"
            },
            new CombatSimulationValueExpression
            {
                Operation = CombatSimulationValueOperation.Constant,
                Constant = 10d
            }
        }
    },
    expressionState,
    expressionRules.Ruleset,
    1,
    1);
Assert(expressionRules.Success && Math.Abs(expressionValue - 12d) < 1e-9d,
    "simulation expressions resolve source-owned status modifiers without UI state");

var oceanRulesBuilder = new CombatRulesetBuilder("ocean-v1");
oceanRulesBuilder.RegisterStatus(new CombatStatusDefinition
{
    OwnerModId = "Tests",
    StatusId = "buff_extraordinary",
    DecayAtRoundEnd = false,
    DynamicModifiersPerStack = { ["PercentDamage"] = 0.01d }
});
oceanRulesBuilder.RegisterStatus(new CombatStatusDefinition
{
    OwnerModId = "Tests",
    StatusId = "buff_elements",
    DecayAtRoundEnd = false,
    Triggers =
    {
        new CombatStatusTriggerDefinition
        {
            TriggerId = "action-after",
            EventKind = CombatSimulationEventKind.ActionResolved,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.AddStatus,
                    Target = CombatSimulationTarget.Self,
                    DefinitionId = "buff_extraordinary",
                    Amount = 2,
                    ScaleWithStatusStacks = true
                }
            }
        }
    }
});
oceanRulesBuilder.RegisterCard(new CombatCardDefinition
{
    OwnerModId = "Tests",
    CardId = "elementscard_1",
    Cost = 0,
    Effects =
    {
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.Draw,
            Target = CombatSimulationTarget.Self,
            Amount = 1
        },
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.AddStatus,
            Target = CombatSimulationTarget.Self,
            DefinitionId = "buff_elements",
            Amount = 2
        }
    }
});
var oceanRules = oceanRulesBuilder.Freeze();
var oceanState = new CombatBattleState
{
    PlayerActorId = 1,
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            InstanceKey = "player",
            Kind = CombatSimulationActorKind.Player,
            Hp = 20,
            MaxHp = 20,
            Energy = 1
        },
        new CombatActorState
        {
            ActorId = 2,
            InstanceKey = "enemy",
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 20,
            MaxHp = 20
        }
    },
    Cards =
    {
        new CombatCardInstanceState { InstanceId = 1, CardId = "elementscard_1" },
        new CombatCardInstanceState { InstanceId = 2, CardId = "drawn" }
    },
    Hand = { 1 },
    DrawPile = { 2 }
};
var oceanApplication = new CombatSimulationEngine().ForkAndApplyPlayerAction(
    new CombatScenarioDefinition
    {
        ScenarioId = "ocean-contract",
        RulesetVersion = "ocean-v1",
        Player = new CombatPlayerSetup
        {
            RoleId = "Tests",
            MaxHp = 20,
            CurrentHp = 20
        },
        Enemies = { new CombatEnemySetup { EnemyId = "unused" } }
    },
    oceanRules.Ruleset,
    oceanState,
    new CombatSimulationAction
    {
        CandidateId = "card:1",
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 1,
        DefinitionId = "elementscard_1"
    });
var oceanPlayer = oceanApplication.State.FindActor(1);
Assert(oceanApplication.Success
       && oceanPlayer?.Statuses.First(item => item.StatusId == "buff_elements").Stacks == 2
       && oceanPlayer.Statuses.First(item => item.StatusId == "buff_extraordinary").Stacks == 4
       && oceanApplication.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.ActionResolved),
    "Ocean Dream resolves draw, elements, and the same-action 4 percent damage setup chain");

var episodeProfile = new CombatDecisionProfile
{
    Id = "balanced",
    SearchBudgetMode = "fixed",
    SearchSimulationBudget = 128,
    SearchNodeBudget = 1024,
    SearchMaxPly = 8
};
var episodes = new List<CombatEpisode>();
for (var episodeIndex = 0; episodeIndex < 10; episodeIndex++)
{
    var episodePolicy = new CombatEpisodeRecordingPolicy(
        new CombatDecisionSimulationPolicy(episodeProfile),
        episodeProfile.Id);
    var episodeResult = simulationEngine.Run(
        BuildSimulationScenario(
            seed: (ulong)(100 + episodeIndex),
            CombatSimulationTraceLevel.Summary),
        simulationRules.Ruleset,
        episodePolicy);
    var recordedEpisode = episodePolicy.Complete(episodeResult);
    recordedEpisode.JourneyRunId = "policy-value-run:" + episodeIndex / 2;
    recordedEpisode.JourneyBattleIndex = (episodeIndex % 4) switch
    {
        0 => 2,
        1 => 12,
        2 => 25,
        _ => 36
    };
    recordedEpisode.Campaign.DifficultyId =
        episodeIndex % 3 == 0 ? "advanced" : "normal";
    recordedEpisode.Campaign.OutcomeClass =
        episodeIndex % 2 == 0 ? "victory" : "defeat";
    recordedEpisode.Campaign.FinalBossVictory = episodeIndex % 2 == 0;
    episodes.Add(recordedEpisode);
}
Assert(episodes.All(episode => episode.Frames.Count > 0
                               && episode.Frames.All(frame =>
                                   frame.Candidates.Count > 0
                                   && frame.RemainingTurnsTarget >= 0d))
       && episodes.All(episode => episode.Authoritative),
    "episode recorder captures search targets and backfills cross-turn terminal returns");
var policyValueTraining = CombatPolicyValueTrainer.Train(
    episodes,
    "balanced",
    new CombatPolicyValueTrainingOptions
    {
        Epochs = 12,
        LearningRate = 0.01d,
        MinimumEpisodes = 4,
        RandomSeed = 17
    });
Assert(policyValueTraining.Success
       && policyValueTraining.Model != null
       && policyValueTraining.Model.Metrics["trainingRunCount"] == 3d
       && policyValueTraining.Model.Metrics["validationRunCount"] == 1d
       && policyValueTraining.Model.Metrics["testRunCount"] == 1d
       && policyValueTraining.Model.Metrics.ContainsKey("testCompositeLoss")
       && policyValueTraining.Model.Metrics["optimizerAdamW"] == 1d
       && policyValueTraining.Model.Metrics["optimizerStep"] > 0d
       && policyValueTraining.Model.PolicyTemperature is >= 0.5d and <= 3d
       && policyValueTraining.Model.Metrics["policyTemperature"]
          == policyValueTraining.Model.PolicyTemperature
       && policyValueTraining.Model.Metrics.ContainsKey(
           "validationPolicyCrossEntropy")
       && policyValueTraining.Model.Metrics.ContainsKey(
           "validationCriticalPolicyAccuracy")
       && policyValueTraining.Model.Metrics.ContainsKey(
           "validationDeathBrier")
       && policyValueTraining.Model.Metrics.ContainsKey(
           "validationCompositeLoss")
       && policyValueTraining.CandidateModels.Count > 0
       && policyValueTraining.CandidateModels.Count <= 3
       && policyValueTraining.CandidateModels.All(candidate =>
           candidate.Model.PolicyTemperature is >= 0.5d and <= 3d
           && candidate.Model.Metrics.ContainsKey(
               "validationCompositeLoss")
           && candidate.Model.Metrics["policyTemperature"]
              == candidate.Model.PolicyTemperature)
       && policyValueTraining.FrameStratificationProtocol
          == CombatPolicyValueFrameStratificationProtocol.Version
       && policyValueTraining.FrameStrata.Count >= 4
       && policyValueTraining.MinimumFrameWeight
          >= CombatPolicyValueFrameStratificationProtocol.MinimumWeight
       && policyValueTraining.MaximumFrameWeight <= 3d
       && CombatPolicyValueNetworkValidator.TryValidate(
           policyValueTraining.Model,
           out _),
    "complete episodes train a validated managed policy-value network, retain Top-K checkpoints, and select by multi-objective validation");
var trainingCancellationObserved = false;
using (var cancelledTraining = new CancellationTokenSource())
{
    cancelledTraining.Cancel();
    try
    {
        CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            new CombatPolicyValueTrainingOptions { MinimumEpisodes = 4 },
            cancelledTraining.Token);
    }
    catch (OperationCanceledException)
    {
        trainingCancellationObserved = true;
    }
}
Assert(trainingCancellationObserved,
    "policy-value training observes cancellation before expensive epoch work");
var batchTrainingOptions = new CombatPolicyValueTrainingOptions
{
    Epochs = 6,
    MinimumEpochs = 6,
    EarlyStoppingPatience = 10,
    BatchSize = 8,
    MaximumDegreeOfParallelism = 4,
    LearningRate = 0.01d,
    MinimumEpisodes = 4,
    RandomSeed = 117
};
CombatPolicyValueTrainingResumeState? capturedBatchCheckpoint = null;
var batchProgress = new List<CombatPolicyValueTrainingProgress>();
using (var interruptedBatchTraining = new CancellationTokenSource())
{
    var interrupted = false;
    try
    {
        CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            batchTrainingOptions,
            interruptedBatchTraining.Token,
            new CombatPolicyValueTrainingSession
            {
                Progress = progress => batchProgress.Add(progress),
                Checkpoint = checkpoint =>
                {
                    capturedBatchCheckpoint = checkpoint;
                    if (checkpoint.CompletedEpochs >= 2)
                    {
                        interruptedBatchTraining.Cancel();
                    }
                }
            });
    }
    catch (OperationCanceledException)
    {
        interrupted = true;
    }
    Assert(interrupted
           && capturedBatchCheckpoint?.CompletedEpochs == 2
           && capturedBatchCheckpoint.Optimizer?.Step > 0
           && capturedBatchCheckpoint.Optimizer.FirstMoment.Length
              == capturedBatchCheckpoint.Optimizer.SecondMoment.Length
           && batchProgress.Any(progress =>
               progress.Stage == "encoding")
           && batchProgress.Any(progress =>
               progress.Stage == "training"
               && progress.CompletedFrames > 0),
        "batch policy-value training reports frame progress and checkpoints every completed epoch");
}
var resumedBatchTraining = CombatPolicyValueTrainer.Train(
    episodes,
    "balanced",
    batchTrainingOptions,
    CancellationToken.None,
    new CombatPolicyValueTrainingSession
    {
        Resume = capturedBatchCheckpoint
    });
var uninterruptedBatchTraining = CombatPolicyValueTrainer.Train(
    episodes,
    "balanced",
    batchTrainingOptions,
    CancellationToken.None);
Assert(resumedBatchTraining.Success
       && uninterruptedBatchTraining.Success
       && resumedBatchTraining.CompletedEpochs == 6
       && resumedBatchTraining.Model != null
       && uninterruptedBatchTraining.Model != null
       && resumedBatchTraining.Model.StateWeights.SequenceEqual(
           uninterruptedBatchTraining.Model.StateWeights)
       && resumedBatchTraining.Model.PolicyWeights.SequenceEqual(
           uninterruptedBatchTraining.Model.PolicyWeights),
    "resumed deterministic minibatch training produces the uninterrupted model weights");
var policyValueModel = new ManagedCombatPolicyValueModel(policyValueTraining.Model!);
var firstEpisodeFrame = episodes[0].Frames[0];
var policyValuePrediction = policyValueModel.Evaluate(new CombatPolicyValueInput
{
    StateFeatures = firstEpisodeFrame.StateFeatures,
    Candidates = firstEpisodeFrame.Candidates
        .Where(candidate => candidate.Legal)
        .Select(candidate => new CombatPolicyValueCandidate
        {
            CandidateId = candidate.CandidateId,
            SourceId = candidate.SourceId,
            Features = candidate.Features
        })
        .ToList()
});
Assert(policyValuePrediction.PolicyLogits.Count
       == firstEpisodeFrame.Candidates.Count(candidate => candidate.Legal)
       && policyValuePrediction.WinProbability is >= 0d and <= 1d
       && policyValuePrediction.DeathProbability is >= 0d and <= 1d,
    "managed policy-value inference returns masked action logits and calibrated probability ranges");
var evolution = new CombatPolicyEvolutionRunner().Run(
    new CombatPolicyEvolutionRequest
    {
        DecisionProfile = "balanced",
        Iterations = 1,
        TrainingEpisodesPerIteration = 8,
        ArenaEpisodesPerIteration = 2,
        SeedStart = 500,
        Profile = episodeProfile,
        Training = new CombatPolicyValueTrainingOptions
        {
            Epochs = 5,
            MinimumEpisodes = 4,
            HiddenDimensions = 16,
            RandomSeed = 31
        },
        Scenarios =
        {
            BuildSimulationScenario(seed: 500, CombatSimulationTraceLevel.Summary)
        }
    },
    simulationRules.Ruleset);
Assert(evolution.Iterations.Count == 1
       && evolution.Replay.Count == 8
       && evolution.Iterations[0].InvalidCandidateBattles == 0,
    "automatic policy evolution generates episodes, trains a challenger, and runs a paired arena");

var campaign = BuildStandardCampaign();
CombatCampaignWorldPlanner.Validate(campaign);
var normalPlan = CombatCampaignWorldPlanner.Build(campaign, "normal", 700UL);
var advancedPlan = CombatCampaignWorldPlanner.Build(campaign, "advanced", 700UL);
Assert(normalPlan.Encounters.Count == 37
       && normalPlan.Encounters.Take(12).All(item =>
           item.LayerNumber is 1 or 2 && item.GameLevel is >= 0 and <= 11)
       && normalPlan.Encounters.Skip(12).Take(12).All(item =>
           item.LayerNumber is 3 or 4 && item.GameLevel is >= 12 and <= 23)
       && normalPlan.Encounters.Skip(24).Take(12).All(item =>
           item.LayerNumber is 5 or 6 && item.GameLevel is >= 24 and <= 35)
       && normalPlan.Encounters[36].Kind == CombatCampaignEncounterKind.FinalBoss,
    "campaign v2 maps six fixed layers to the native 0/1/2 encounter bands");
Assert(normalPlan.Encounters.Take(36).All(item =>
           item.RewardOffer.CardRounds.Count
           == (item.Kind == CombatCampaignEncounterKind.Normal ? 1 : 0)
           && item.RewardOffer.CardRounds.All(round => round.Count == 3)
           && !string.IsNullOrWhiteSpace(item.RewardOffer.RelicId)
           && !string.IsNullOrWhiteSpace(item.RewardOffer.BlessingId))
       && normalPlan.Encounters.Take(36).Sum(item =>
           item.RewardOffer.CardRounds.Count) == 24
       && normalPlan.Encounters.SelectMany(item =>
               item.RewardOffer.CardRounds)
           .SelectMany(round => round)
           .All(id => !CombatCampaignCardAcquisitionPolicy
               .IsGeneratedOnlyIdentifier(id))
       && normalPlan.Encounters[36].RewardOffer.CardRounds.Count == 0,
    "campaign v2 limits card growth and excludes generated-only cards from every reward offer");
Assert(normalPlan.PlanHash != advancedPlan.PlanHash
       && normalPlan.Encounters.Select(item => item.EncounterId)
           .SequenceEqual(advancedPlan.Encounters.Select(item => item.EncounterId)),
    "difficulty is part of evaluation identity without changing the paired encounter stream");

var campaignRulesBuilder = new CombatRulesetBuilder(campaign.RulesetVersion);
foreach (var cardId in new[] { "strike", "guard", "skip-me" })
{
    campaignRulesBuilder.RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = cardId,
        Cost = 0,
        RequiresEnemyTarget = true,
        Fidelity = CombatRuleFidelity.Authoritative,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Damage,
                Target = CombatSimulationTarget.SelectedEnemy,
                Amount = 1
            }
        }
    });
}
foreach (var enemyId in campaign.Encounters
             .SelectMany(item => item.EnemyIds)
             .Distinct(StringComparer.OrdinalIgnoreCase))
{
    campaignRulesBuilder.RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = enemyId,
        MaxHp = 1,
        Fidelity = CombatRuleFidelity.Authoritative,
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "wait",
                Weight = 1,
                Effects = new List<CombatSimulationEffectDefinition>()
            }
        }
    });
}
campaignRulesBuilder.RegisterCard(new CombatCardDefinition
{
    OwnerModId = "Tests",
    CardId = "attribute-card",
    Cost = 0,
    Exhaust = true,
    RequiresEnemyTarget = true,
    Fidelity = CombatRuleFidelity.Authoritative,
    Effects =
    {
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.GainBlock,
            Target = CombatSimulationTarget.Self,
            Amount = 5
        },
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.ModifyVariable,
            Target = CombatSimulationTarget.Self,
            DefinitionId = "StrengthUpperBound",
            Amount = 5,
            PersistAcrossBattles = true
        },
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.Damage,
            Target = CombatSimulationTarget.SelectedEnemy,
            Amount = 5
        }
    }
});
campaignRulesBuilder.RegisterEnemy(new CombatEnemyDefinition
{
    OwnerModId = "Tests",
    EnemyId = "attribute-dummy",
    MaxHp = 6,
    Fidelity = CombatRuleFidelity.Authoritative,
    Intents =
    {
        new CombatEnemyIntentDefinition
        {
            IntentId = "wait",
            Weight = 1
        }
    }
});
var campaignRules = campaignRulesBuilder.Freeze();
var attributeResult = new CombatSimulationEngine().Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "attribute-scaling",
        RulesetVersion = campaign.RulesetVersion,
        Seed = 1,
        Player = new CombatPlayerSetup
        {
            MaxHp = 20,
            CurrentHp = 20,
            Deck = { "attribute-card" },
            Variables =
            {
                ["Strength"] = 10,
                ["Perceive"] = 5
            }
        },
        Enemies =
        {
            new CombatEnemySetup { EnemyId = "attribute-dummy" }
        },
        InitialDraw = 1,
        DrawPerTurn = 0,
        RequireAuthoritativeRules = true
    },
    campaignRules.Ruleset,
    new GreedyCombatSimulationPolicy());
Assert(attributeResult.Outcome == CombatSimulationOutcome.Victory
       && attributeResult.Metrics.DamageDealt == 6
       && attributeResult.Metrics.BlockGained == 6
       && attributeResult.PersistentVariableDeltas["StrengthUpperBound"] == 5,
    "campaign attributes use Witch scaling and explicitly project persistent cap effects");
var moneyRulesBuilder = new CombatRulesetBuilder("money-rules");
moneyRulesBuilder.RegisterCard(new CombatCardDefinition
{
    OwnerModId = "Tests",
    CardId = "chip",
    Cost = 0,
    RequiresEnemyTarget = true,
    Effects =
    {
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.Damage,
            Target = CombatSimulationTarget.SelectedEnemy,
            Amount = 1
        }
    }
});
moneyRulesBuilder.RegisterEnemy(new CombatEnemyDefinition
{
    OwnerModId = "Tests",
    EnemyId = "pickpocket",
    MaxHp = 2,
    Intents =
    {
        new CombatEnemyIntentDefinition
        {
            IntentId = "steal-and-refund",
            Weight = 1,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.ModifyVariable,
                    Target = CombatSimulationTarget.Player,
                    DefinitionId = "Money",
                    Amount = -15,
                    PersistAcrossBattles = true,
                    MinimumVariableValue = 0
                },
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.DeferVariableUntilVictory,
                    Target = CombatSimulationTarget.Player,
                    DefinitionId = "Money",
                    Amount = 15,
                    PersistAcrossBattles = true,
                    MinimumVariableValue = 0
                }
            }
        }
    }
});
var moneyRules = moneyRulesBuilder.Freeze();
var moneyResult = new CombatSimulationEngine().Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "money-clamp-and-refund",
        RulesetVersion = "money-rules",
        Seed = 7,
        Player = new CombatPlayerSetup
        {
            MaxHp = 20,
            CurrentHp = 20,
            Deck = { "chip" },
            Variables = { ["Money"] = 10 }
        },
        Enemies = { new CombatEnemySetup { EnemyId = "pickpocket" } },
        InitialDraw = 1,
        DrawPerTurn = 1,
        RequireAuthoritativeRules = true
    },
    moneyRules.Ruleset,
    new GreedyCombatSimulationPolicy());
var changedMoneyState = moneyResult.FinalState.Clone();
changedMoneyState.Player!.Variables["Money"]++;
Assert(moneyRules.Success
       && moneyResult.Outcome == CombatSimulationOutcome.Victory
       && moneyResult.FinalState.Player!.Variables["Money"] == 15d
       && moneyResult.PersistentVariableDeltas["Money"] == 5
       && moneyResult.FinalState.DeferredVictoryVariableChanges.Count == 0
       && CombatBattleStateHasher.Hash(moneyResult.FinalState)
          != CombatBattleStateHasher.Hash(changedMoneyState),
    "combat money clamps theft to the available balance, refunds on victory, persists the actual delta, and participates in state identity");
var resurrectionRules = new CombatRulesetBuilder("resurrection-settlement-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "wait-resurrection",
        Cost = 1,
        Fidelity = CombatRuleFidelity.Authoritative
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "escape-resurrection",
        Cost = 1,
        Fidelity = CombatRuleFidelity.Authoritative,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.WinBattle,
                Target = CombatSimulationTarget.Self
            }
        }
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "lethal-resurrection-enemy",
        MaxHp = 100,
        Fidelity = CombatRuleFidelity.Authoritative,
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "lethal",
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.Player,
                        Amount = 20
                    }
                }
            }
        }
    })
    .Freeze();
var resurrectedPlayerResult = new CombatSimulationEngine(
        new TestResurrectionExtensionFactory())
    .Run(
        new CombatScenarioDefinition
        {
            ScenarioId = "player-resurrection-settlement",
            RulesetVersion = "resurrection-settlement-v1",
            Player = new CombatPlayerSetup
            {
                MaxHp = 10,
                CurrentHp = 10,
                BaseEnergy = 1,
                Deck = { "wait-resurrection" }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = "lethal-resurrection-enemy"
                }
            },
            InitialDraw = 1,
            DrawPerTurn = 0,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 5
            }
        },
        resurrectionRules.Ruleset,
        FirstLegalCombatSimulationPolicy.Instance);
var explicitVictoryResult = new CombatSimulationEngine().Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "explicit-victory-settlement",
        RulesetVersion = "resurrection-settlement-v1",
        Player = new CombatPlayerSetup
        {
            MaxHp = 10,
            CurrentHp = 10,
            BaseEnergy = 1,
            Deck = { "escape-resurrection" }
        },
        Enemies =
        {
            new CombatEnemySetup
            {
                EnemyId = "lethal-resurrection-enemy"
            }
        },
        InitialDraw = 1,
        DrawPerTurn = 0
    },
    resurrectionRules.Ruleset,
    FirstLegalCombatSimulationPolicy.Instance);
var lateEscapeResult = new CombatSimulationEngine(
        new TestLateEscapeExtensionFactory())
    .Run(
        new CombatScenarioDefinition
        {
            ScenarioId = "late-escape-settlement",
            RulesetVersion = "resurrection-settlement-v1",
            Player = new CombatPlayerSetup
            {
                MaxHp = 10,
                CurrentHp = 10,
                BaseEnergy = 1,
                Deck = { "wait-resurrection" }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = "lethal-resurrection-enemy"
                }
            },
            InitialDraw = 1,
            DrawPerTurn = 0,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 2,
                MaximumActions = 5
            }
        },
        resurrectionRules.Ruleset,
        FirstLegalCombatSimulationPolicy.Instance);
Assert(resurrectedPlayerResult.Outcome == CombatSimulationOutcome.Draw
       && resurrectedPlayerResult.FinalPlayerHp == 5
       && resurrectedPlayerResult.TerminalConsistencyValid
       && explicitVictoryResult.Outcome == CombatSimulationOutcome.Victory
       && explicitVictoryResult.ExplicitRuleTermination
       && explicitVictoryResult.TerminalConsistencyValid
       && explicitVictoryResult.FinalState.LivingEnemies.Any(),
    "physical outcome waits for resurrection settlement while explicit rule termination may end with living enemies");
var lateEndedEvent = lateEscapeResult.Events.Last(item =>
    item.Kind == CombatSimulationEventKind.BattleEnded);
Assert(lateEscapeResult.Outcome == CombatSimulationOutcome.Victory
       && lateEscapeResult.TerminalResolution
       == CombatTerminalResolution.ResurrectionEscapeOverride
       && lateEscapeResult.InitialTerminalOutcome
       == CombatSimulationOutcome.Defeat
       && lateEscapeResult.InitialTerminalPlayerHp == 0
       && lateEscapeResult.FinalPlayerHp == 5
       && lateEscapeResult.ExplicitRuleTermination
       && lateEscapeResult.TerminalConsistencyValid
       && lateEscapeResult.Metrics.RuleTerminalOverrides == 1
       && lateEscapeResult.FailureDiagnostics.TerminalOutcome == "Defeat"
       && lateEscapeResult.FailureDiagnostics.RecentEvents.Count > 0
       && lateEndedEvent.Amount == (int)CombatSimulationOutcome.Victory
       && lateEndedEvent.Message
       == CombatTerminalResolution.ResurrectionEscapeOverride.ToString(),
    "late resurrection escape explicitly overrides only a physical defeat and preserves the original terminal snapshot");
var overflowState = new CombatCampaignState
{
    Attributes = { ["Strength"] = 40 },
    LayerBaseAttributes = { ["Strength"] = 40 },
    PermanentAttributeBonuses = { ["Strength"] = 5 },
    AttributeUpperBounds = { ["Strength"] = 40 }
};
CombatCampaignRewardSelector.ClampAttributes(overflowState);
overflowState.AttributeUpperBounds["Strength"] = 45;
CombatCampaignRewardSelector.ClampAttributes(overflowState);
Assert(overflowState.Attributes["Strength"] == 40
       && overflowState.PermanentAttributeBonuses["Strength"] == 0,
    "attribute overflow is discarded and does not return after a later cap increase");
var mechanicBuildDefinition = new CombatCampaignDefinition
{
    CampaignId = "mechanic-build-test",
    Rewards =
    {
        new CombatCampaignRewardDefinition
        {
            RewardId = "Crowdfundingcard_6",
            Kind = CombatCampaignRewardKind.Card
        },
        new CombatCampaignRewardDefinition
        {
            RewardId = "Crowdfundingcard_8",
            Kind = CombatCampaignRewardKind.Card
        },
        new CombatCampaignRewardDefinition
        {
            RewardId = "Crowdfundingcard_10",
            Kind = CombatCampaignRewardKind.Card
        },
        new CombatCampaignRewardDefinition
        {
            RewardId = "Crowdfundingcard_11",
            Kind = CombatCampaignRewardKind.Card
        },
        new CombatCampaignRewardDefinition
        {
            RewardId = "luckycard_4",
            Kind = CombatCampaignRewardKind.Card,
            BaseValue = 50d,
            Features = { ["risk"] = 1d }
        },
        new CombatCampaignRewardDefinition
        {
            RewardId = "safe-card",
            Kind = CombatCampaignRewardKind.Card,
            BaseValue = 1d,
            Features = { ["defense"] = 1d }
        }
    }
};
var mechanicBuildState = new CombatCampaignState
{
    CurrentLayer = 3,
    MaxHp = 100,
    CurrentHp = 100,
    Deck =
    {
        "Crowdfundingcard_6",
        "Crowdfundingcard_8",
        "Crowdfundingcard_10",
        "Crowdfundingcard_11"
    }
};
var boundedResidualDefinition = new CombatCampaignDefinition
{
    RewardScoreResidualMaximumAbsolute = 0.20d,
    RewardScoreResiduals =
    {
        ["residual-positive"] = 10d,
        ["residual-negative"] = -10d
    },
    Rewards =
    {
        new CombatCampaignRewardDefinition
        {
            RewardId = "residual-positive",
            Kind = CombatCampaignRewardKind.Card,
            BaseValue = 1d
        },
        new CombatCampaignRewardDefinition
        {
            RewardId = "residual-negative",
            Kind = CombatCampaignRewardKind.Card,
            BaseValue = 1d
        }
    }
};
var boundedResidualScores = CombatCampaignRewardSelector.ScoreRewards(
    boundedResidualDefinition,
    new CombatCampaignState
    {
        CurrentLayer = 1,
        MaxHp = 100,
        CurrentHp = 100
    },
    new[] { "residual-negative", "residual-positive" },
    boundedResidualDefinition.Rewards.ToDictionary(
        item => item.RewardId,
        StringComparer.OrdinalIgnoreCase),
    0,
    CombatCampaignRewardKind.Card);
Assert(boundedResidualScores[0].RewardId == "residual-positive"
       && boundedResidualScores[0].LearnedResidual == 0.20d
       && boundedResidualScores[1].LearnedResidual == -0.20d,
    "campaign reward scoring applies learned residuals without exceeding the configured safety bound");
var configuredRelicBiasDefinition = new CombatCampaignDefinition
{
    RewardScoreResidualMaximumAbsolute = 0.20d,
    RewardScoreResiduals = { ["relic-keep"] = 0.10d },
    RewardScoreBiasMaximumAbsolute = 8d,
    RewardScoreBiases = { ["relic-avoid"] = -4d },
    Rewards =
    {
        new CombatCampaignRewardDefinition
        {
            RewardId = "relic-keep",
            Kind = CombatCampaignRewardKind.Relic,
            BaseValue = 1d,
            OfferWeight = 1d
        },
        new CombatCampaignRewardDefinition
        {
            RewardId = "relic-avoid",
            Kind = CombatCampaignRewardKind.Relic,
            BaseValue = 1d,
            OfferWeight = 0d
        }
    }
};
var configuredRelicBiasScores = CombatCampaignRewardSelector.ScoreRewards(
    configuredRelicBiasDefinition,
    new CombatCampaignState
    {
        CurrentLayer = 1,
        MaxHp = 100,
        CurrentHp = 100
    },
    new[] { "relic-avoid", "relic-keep" },
    configuredRelicBiasDefinition.Rewards.ToDictionary(
        item => item.RewardId,
        StringComparer.OrdinalIgnoreCase),
    0,
    CombatCampaignRewardKind.Relic);
var weightedRelicPick = CombatCampaignWorldPlanner.PickWeightedUnused(
    configuredRelicBiasDefinition.Rewards,
    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    123UL,
    "relic-offer-test",
    0);
Assert(configuredRelicBiasScores[0].RewardId == "relic-keep"
       && configuredRelicBiasScores[0].LearnedResidual == 0.10d
       && configuredRelicBiasScores[1].ConfiguredBias == -4d
       && weightedRelicPick == "relic-keep",
    "configured relic avoidance lowers both deterministic offer probability and reward selection score");
var mechanicPlan = CombatCampaignRewardSelector.RefreshBuildPlan(
    mechanicBuildDefinition,
    mechanicBuildState);
var mechanicRewardLookup = mechanicBuildDefinition.Rewards.ToDictionary(
    item => item.RewardId,
    StringComparer.OrdinalIgnoreCase);
var mechanicRewardScores = CombatCampaignRewardSelector.ScoreRewards(
    mechanicBuildDefinition,
    mechanicBuildState,
    new[] { "luckycard_4", "safe-card" },
    mechanicRewardLookup,
    12,
    CombatCampaignRewardKind.Card);
Assert(mechanicPlan.PrimaryArchetype == "rebirth"
       && mechanicRewardScores.Single(item =>
           item.RewardId == "luckycard_4").RiskPenalty >= 100d
       && mechanicRewardScores[0].RewardId == "safe-card",
    "campaign planning recognizes mechanic archetypes and hard-demotes curse alchemy despite an inflated base value");
var campaignPair = new CombatCampaignRunner().RunPaired(
    campaign,
    "normal",
    700UL,
    campaignRules.Ruleset,
    new GreedyCombatSimulationPolicyFactory(),
    new GreedyCombatSimulationPolicyFactory());
Assert(campaignPair.Baseline.CampaignVictory
       && campaignPair.Learned.CampaignVictory
       && campaignPair.Baseline.CompletedBattles == 37
       && campaignPair.Baseline.Rewards.Count == 36
       && campaignPair.Baseline.FinalState.MaxHp == 260
       && campaignPair.Baseline.FinalState.CurrentHp == 260
       && campaignPair.Baseline.FinalState.Attributes["Strength"] == 40
       && campaignPair.Baseline.FinalState.Attributes["Wisdom"] == 39
       && campaignPair.Baseline.FinalState.Attributes["Lucky"] == 20
       && campaignPair.Baseline.FinalState.Money == 100
       && campaignPair.Baseline.FinalState.Relics.Count == 6
       && campaignPair.Baseline.FinalState.Deck.Count <= 35
       && campaignPair.Baseline.FinalState.BuildPlan.PrimaryArchetype == "burst"
       && campaignPair.Baseline.FinalState.BuildPlan.SynergySources["card"]
          == campaignPair.Baseline.FinalState.Deck.Count
       && campaignPair.Baseline.FinalState.BuildPlan.SynergySources["relic"]
          == campaignPair.Baseline.FinalState.Relics.Count
       && campaignPair.Baseline.Rewards.All(item =>
           item.BuildPlan.TargetDeckSizeMaximum <= 35),
    "campaign runner carries full state, applies layer-aware deck bounds, and records the build plan separately from battle policy");
var encounterStarts = new List<CombatCampaignCheckpoint>();
var encounterPlan = CombatCampaignWorldPlanner.Build(
    campaign,
    "normal",
    701UL);
_ = new CombatCampaignRunner().RunMonitoredWithEncounterStarts(
    campaign,
    encounterPlan,
    campaignRules.Ruleset,
    new GreedyCombatSimulationPolicyFactory(),
    battleProgress: null,
    encounterStart: checkpoint => encounterStarts.Add(checkpoint));
var localEncounterCheckpoint = encounterStarts[10];
localEncounterCheckpoint.Battles.Clear();
localEncounterCheckpoint.Rewards.Clear();
localEncounterCheckpoint.Completed = false;
var localEncounterResult = new CombatCampaignRunner().RunMonitoredSegment(
    campaign,
    encounterPlan,
    campaignRules.Ruleset,
    new GreedyCombatSimulationPolicyFactory(),
    localEncounterCheckpoint,
    maximumEncounters: 1,
    battleProgress: null);
Assert(localEncounterResult.Battles.Count == 1
       && localEncounterResult.Battles[0].ScenarioId.Contains(
           ":10:",
           StringComparison.Ordinal)
       && localEncounterResult.Checkpoint.NextEncounterIndex == 11,
    "campaign runner can replay exactly one failed encounter from a compact pre-battle checkpoint");
Assert(campaignRules.Ruleset.TryGetCardCore("strike", out var projectedStrike),
    "foundation fixture resolves the starter attack definition");
projectedStrike!.Fidelity = CombatRuleFidelity.Authoritative;
var projectedTrainingCampaign = BuildStandardCampaign();
projectedTrainingCampaign.RequireAuthoritativeRules = true;
var projectedValidationCampaign = BuildStandardCampaign();
projectedValidationCampaign.RequireAuthoritativeRules = true;
foreach (var difficulty in projectedTrainingCampaign.Difficulties
             .Concat(projectedValidationCampaign.Difficulties))
{
    difficulty.ApplyGameLevelShield = false;
}
var foundationSeedPlanA = CombatFoundationSeedPlan.Create(123456789UL, 2_000_000UL);
var foundationSeedPlanARepeat = CombatFoundationSeedPlan.Create(
    123456789UL,
    2_000_000UL);
var foundationSeedPlanB = CombatFoundationSeedPlan.Create(987654321UL, 2_000_000UL);
Assert(foundationSeedPlanA.TrainingSeedStart
       == foundationSeedPlanARepeat.TrainingSeedStart
       && foundationSeedPlanA.ArenaSeedStart
       == foundationSeedPlanARepeat.ArenaSeedStart
       && foundationSeedPlanA.TuningSeedStart
       == foundationSeedPlanARepeat.TuningSeedStart
       && foundationSeedPlanA.ModelRandomSeed
       == foundationSeedPlanARepeat.ModelRandomSeed
       && foundationSeedPlanA.TrainingSeedStart
       != foundationSeedPlanB.TrainingSeedStart
       && foundationSeedPlanA.ArenaSeedStart
       != foundationSeedPlanB.ArenaSeedStart
       && foundationSeedPlanA.TuningSeedStart
       != foundationSeedPlanB.TuningSeedStart
       && foundationSeedPlanA.ModelRandomSeed
       != foundationSeedPlanB.ModelRandomSeed
       && foundationSeedPlanA.ValidationSeedStart == 2_000_000UL
       && foundationSeedPlanB.ValidationSeedStart == 2_000_000UL,
    "foundation RunSeed deterministically separates self-play, arena, and model randomness while retaining canonical validation seeds");
var cleanFeatureVector = CombatPolicyValueEncoding.EncodeState(
    new Dictionary<string, double>
    {
        ["playerHp"] = 20d,
        ["enemyHpTotal"] = 15d
    },
    32);
var contaminatedFeatureVector = CombatPolicyValueEncoding.EncodeState(
    new Dictionary<string, double>
    {
        ["playerHp"] = 20d,
        ["enemyHpTotal"] = 15d,
        ["finalBossVictory"] = 1d,
        ["journeyProgress"] = 1d,
        ["target:value"] = 1d,
        ["future.outcome"] = 1d
    },
    32);
var legacyFeatureModel = new CombatPolicyValueNetworkDefinition
{
    FeatureSchemaVersion = 6
};
Assert(cleanFeatureVector.SequenceEqual(contaminatedFeatureVector)
       && !CombatPolicyValueNetworkValidator.TryValidate(
           legacyFeatureModel,
           out _),
    "policy-value feature contract rejects legacy schema and makes post-hoc labels unable to change the encoded observation");
var curriculumOpening = CombatFoundationCurriculum.BuildDifficulties(
    8,
    0,
    4,
    123456789UL,
    enabled: true);
var curriculumFinal = CombatFoundationCurriculum.BuildDifficulties(
    8,
    3,
    4,
    123456789UL,
    enabled: true,
    priorNormalWinRate: 1d,
    priorNormalTrials: 200,
    priorAdvancedWinRate: 0.8d,
    priorAdvancedTrials: 100);
Assert(curriculumOpening.Count(item => item == "advanced") == 0
       && curriculumFinal.Count(item => item == "advanced") == 2
       && CombatFoundationCurriculum.BuildDifficulties(
               20,
               3,
               4,
               123456789UL,
               enabled: true,
               priorNormalWinRate: 0d,
               priorNormalTrials: 32)
           .Count(item => item == "advanced") == 7
       && CombatFoundationCurriculum.BuildDifficulties(
               20,
               1,
               4,
               123456789UL,
               enabled: true)
           .Count(item => item == "advanced") == 5
       && curriculumOpening.SequenceEqual(
           CombatFoundationCurriculum.BuildDifficulties(
               8,
               0,
               4,
               123456789UL,
               enabled: true)),
    "foundation curriculum starts on normal, introduces 25 percent advanced play, and raises recovery coverage to 35 percent");
CombatCandidateEvaluation BudgetCandidate(
    string id,
    CombatActionSemantics? semantics = null)
{
    return new CombatCandidateEvaluation
    {
        Legal = true,
        Action = new CombatActionObservation
        {
            CandidateId = id,
            Semantics = semantics ?? new CombatActionSemantics()
        }
    };
}

var budgetState = new CombatStateObservation
{
    Player = new CombatUnitObservation { CurrentHp = 80, MaxHp = 100 },
    Enemies =
    {
        new CombatUnitObservation
        {
            DefinitionId = "ordinary-enemy",
            CurrentHp = 100,
            MaxHp = 100
        }
    }
};
var budgetProfile = new CombatDecisionProfile
{
    SearchBudgetMode = "dynamic",
    SearchQuality = "balanced",
    SearchBudgetContext = "deployment"
};
var forcedBudget = CombatSearchBudgetPolicy.Resolve(
    budgetState,
    new[] { BudgetCandidate("only") },
    budgetProfile);
var simpleBudget = CombatSearchBudgetPolicy.Resolve(
    budgetState,
    new[] { BudgetCandidate("a"), BudgetCandidate("b") },
    budgetProfile);
var normalBudget = CombatSearchBudgetPolicy.Resolve(
    budgetState,
    Enumerable.Range(0, 5)
        .Select(index => BudgetCandidate("normal-" + index))
        .ToList(),
    budgetProfile);
var bossState = new CombatStateObservation
{
    Player = new CombatUnitObservation { CurrentHp = 80, MaxHp = 100 },
    Enemies =
    {
        new CombatUnitObservation
        {
            DefinitionId = "final-boss",
            CurrentHp = 500,
            MaxHp = 500
        }
    }
};
var difficultBudget = CombatSearchBudgetPolicy.Resolve(
    bossState,
    Enumerable.Range(0, 5)
        .Select(index => BudgetCandidate("boss-" + index))
        .ToList(),
    budgetProfile);
var fakeLoopBudget = CombatSearchBudgetPolicy.Resolve(
    budgetState,
    new[]
    {
        BudgetCandidate(
            "fake-loop",
            new CombatActionSemantics
            {
                Draw = 1,
                EnergyGain = 1,
                CardGeneration = 1,
                EndOfCycleSelfHpLoss = 1
            }),
        BudgetCandidate("escape")
    },
    budgetProfile);
Assert(forcedBudget.Tier == "forced"
       && forcedBudget.SimulationBudget == 1
       && simpleBudget.Tier == "simple"
       && simpleBudget.SimulationBudget == 96
       && normalBudget.Tier == "normal"
       && normalBudget.SimulationBudget == 224
       && difficultBudget.Tier == "difficult"
       && difficultBudget.SimulationBudget == 384
       && fakeLoopBudget.Tier == "complex"
       && fakeLoopBudget.MaxPly == 16,
    "dynamic search spends one simulation on forced play and reserves deep budgets for bosses and fake-loop states");
var partitionedStatus = CombatPolicyValueEncoding.EncodeState(
    new Dictionary<string, double> { ["playerStatus:test"] = 1d },
    128,
    "partitioned-v3");
var partitionedDeck = CombatPolicyValueEncoding.EncodeState(
    new Dictionary<string, double> { ["deck:test"] = 1d },
    128,
    "partitioned-v3");
Assert(partitionedStatus
           .Select((value, index) => new { value, index })
           .Where(item => Math.Abs(item.value) > 0.0000001d)
           .All(item => item.index >= 32 && item.index < 56)
       && partitionedDeck
           .Select((value, index) => new { value, index })
           .Where(item => Math.Abs(item.value) > 0.0000001d)
           .All(item => item.index >= 56 && item.index < 80),
    "partitioned state encoding keeps status and deck identities in disjoint feature ranges");
var coreStateEncoding = CombatPolicyValueEncoding.EncodeState(
    new Dictionary<string, double>
    {
        ["playerHp"] = 10d,
        ["playerMaxHp"] = 20d
    },
    128,
    "partitioned-v3");
var coreActionEncoding = CombatPolicyValueEncoding.EncodeCandidate(
    new CombatPolicyValueCandidate
    {
        SourceId = "test",
        Features = new Dictionary<string, double>
        {
            ["cost"] = 1d,
            ["risk"] = 2d
        }
    },
    96,
    "partitioned-v3");
Assert(coreStateEncoding[0] != 0d
       && coreStateEncoding[1] != 0d
       && coreActionEncoding[0] != 0d
       && coreActionEncoding[21] != 0d
       && policyValueTraining.Model!.Metrics.ContainsKey(
           "stateFeatureCollisionRate")
       && policyValueTraining.Model.Metrics.ContainsKey(
           "actionFeatureCollisionRate"),
    "partitioned-v3 reserves fixed core slots and reports sparse collision telemetry");
var replayFixture = Enumerable.Range(0, 8)
    .Select(index => new CombatEpisode
    {
        EpisodeId = "replay-" + index,
        JourneyRunId = "fixture:"
                       + (index < 6 ? "normal" : "advanced")
                       + ":"
                       + index,
        JourneyBattleIndex = index == 7 ? 36 : index,
        Seed = (ulong)index,
        Campaign = new CombatCampaignEpisodeMetadata
        {
            DifficultyId = index < 6 ? "normal" : "advanced",
            FinalBossVictory = index == 7,
            CampaignCompletedBattles = index == 7 ? 37 : index + 1,
            CampaignTotalBattles = 37,
            OutcomeClass = index == 7 ? "victory" : "defeat"
        },
        Frames =
        {
            new CombatEpisodeFrame()
        }
    })
    .ToList();
var replaySelectionFixture = CombatFoundationReplaySampler.Select(
    replayFixture,
    8,
    enabled: true);
Assert(replaySelectionFixture.Episodes.Count == 7
       && replaySelectionFixture.NormalEpisodes == 5
       && replaySelectionFixture.AdvancedEpisodes == 2
       && replaySelectionFixture.SuccessfulEpisodes > 0
       && replaySelectionFixture.QuotaShortfalls.TryGetValue(
           "advanced:defeat",
           out var advancedDefeatShortfall)
       && advancedDefeatShortfall == 1,
    "foundation replay stratification preserves the advanced quota, reports scarcity, and never silently backfills it with normal episodes");
var replayWithDuplicate = replayFixture.Concat(new[] { replayFixture[7] }).ToList();
var deduplicatedReplay = CombatFoundationReplaySampler.Select(
    replayWithDuplicate,
    8,
    enabled: true);
Assert(deduplicatedReplay.Episodes.Count == 7
       && deduplicatedReplay.DroppedDuplicateEpisodes == 1
       && deduplicatedReplay.Episodes
           .Select(item => item.EpisodeId)
           .Distinct(StringComparer.Ordinal)
           .Count() == 7,
    "foundation replay persistence never expands weighted priorities into duplicate episode payloads");

CombatCampaignResult CaseCampaign(
    ulong seed,
    bool victory,
    string archetype,
    params string[] deck)
{
    return new CombatCampaignResult
    {
        CampaignId = "case-learning",
        CampaignVersion = "1",
        DifficultyId = "normal",
        WorldSeed = seed,
        PlanHash = "plan-" + seed,
        PolicyId = victory ? "winner" : "failure",
        FinalBossVictory = victory,
        CampaignVictory = victory,
        ReachedFinalBoss = victory,
        CompletedBattles = victory ? 37 : 31,
        TotalBattles = 37,
        BattleSemanticCoverage = 1d,
        ProgressionSemanticCoverage = 1d,
        FinalState = new CombatCampaignState
        {
            CurrentHp = victory ? 70 : 0,
            MaxHp = 100,
            Deck = deck.ToList(),
            BuildPlan = new CombatCampaignBuildPlan
            {
                PrimaryArchetype = archetype
            }
        },
        Battles =
        {
            new CombatSimulationResult
            {
                ScenarioId = victory ? "final-boss" : "late-elite",
                RulesetHash = "case-rules",
                Outcome = victory
                    ? CombatSimulationOutcome.Victory
                    : CombatSimulationOutcome.Defeat,
                TerminalConsistencyValid = true,
                SemanticCoverage = 1d,
                Turns = victory ? 4 : 12,
                FinalPlayerHp = victory ? 70 : 0,
                Metrics = new CombatSimulationMetrics
                {
                    CardsPlayed = victory ? 8 : 20,
                    DamageDealt = victory ? 300 : 180,
                    DamageTaken = victory ? 30 : 100
                }
            }
        }
    };
}

var caseEpisode = new CombatEpisode
{
    EpisodeId = "case-success-episode",
    RulesetHash = "case-rules",
    Authoritative = true,
    SemanticCoverage = 1d,
    JourneyBattleIndex = 36,
    Campaign = new CombatCampaignEpisodeMetadata
    {
        FinalBossVictory = true,
        IntegrityValid = true,
        DifficultyId = "normal",
        OutcomeClass = "victory"
    }
};
var successfulCaseCampaign = CaseCampaign(
    100UL,
    true,
    "cycle",
    "engine",
    "draw");
var failedCaseCampaign = CaseCampaign(
    100UL,
    false,
    "cycle",
    "plain",
    "plain",
    "filler");
var successfulObservation = CombatFoundationCaseLearning.Observe(
    successfulCaseCampaign,
    "arena",
    1,
    "candidate",
    "case-rules",
    "balanced",
    "model-success",
    new[] { caseEpisode });
var failedObservation = CombatFoundationCaseLearning.Observe(
    failedCaseCampaign,
    "arena",
    1,
    "champion",
    "case-rules",
    "balanced",
    "model-failure");
var caseAnalysis = CombatFoundationCaseLearning.Analyze(
    new[] { successfulObservation, failedObservation });
Assert(successfulObservation.ArchiveEligible
       && successfulObservation.RobustnessScore > 0d
       && caseAnalysis.SuccessfulCases == 1
       && caseAnalysis.FailedCases == 1
       && caseAnalysis.MatchedPairs == 1
       && caseAnalysis.Pairs[0].SuccessSeed
       == caseAnalysis.Pairs[0].FailureSeed,
    "foundation success learning archives authoritative wins and builds same-seed comparisons");
var archivedCase = CombatFoundationCaseLearning.CreateSuccessCase(
    successfulCaseCampaign,
    successfulObservation,
    new[] { caseEpisode });
var compatibleExpertEpisodes =
    CombatFoundationCaseLearning.SelectExpertEpisodes(
        new[] { archivedCase },
        "case-learning",
        "1",
        "case-rules",
        8);
var incompatibleExpertEpisodes =
    CombatFoundationCaseLearning.SelectExpertEpisodes(
        new[] { archivedCase },
        "case-learning",
        "1",
        "different-rules",
        8);
Assert(compatibleExpertEpisodes.Count == 1
       && incompatibleExpertEpisodes.Count == 0
       && CombatFoundationCaseLearning.CompatibilityKey(
           "case-learning",
           "1",
           "case-rules")
       == successfulObservation.CompatibilityKey,
    "foundation expert replay is bounded and isolated by campaign, ruleset and feature protocol");
var stratifiedExpertCases = Enumerable.Range(0, 8)
    .Select(caseIndex =>
    {
        var advanced = caseIndex >= 6;
        return new CombatFoundationSuccessCase
        {
            Observation = new CombatFoundationCampaignObservation
            {
                CaseId = "stratified-case-" + caseIndex,
                ArchiveEligible = true,
                CampaignId = "case-learning",
                CampaignVersion = "1",
                RulesetHash = "case-rules",
                DifficultyId = advanced ? "advanced" : "normal",
                StrategyFingerprint = "strategy-" + (caseIndex % 3),
                RobustnessScore = 1d - caseIndex * 0.01d
            },
            Episodes = Enumerable.Range(0, 4)
                .Select(battleIndex => new CombatEpisode
                {
                    EpisodeId = "stratified-episode-"
                                + caseIndex
                                + "-"
                                + battleIndex,
                    JourneyRunId = "stratified-run-" + caseIndex,
                    JourneyBattleIndex = battleIndex,
                    RulesetHash = "case-rules",
                    Authoritative = true,
                    Campaign = new CombatCampaignEpisodeMetadata
                    {
                        DifficultyId = advanced ? "advanced" : "normal",
                        IntegrityValid = true,
                        FinalBossVictory = true
                    }
                })
                .ToList()
        };
    })
    .ToList();
var stratifiedExpertSelection =
    CombatFoundationCaseLearning.SelectExpertReplay(
        stratifiedExpertCases,
        "case-learning",
        "1",
        "case-rules",
        episodeLimit: 16,
        targetAdvancedShare: 0.35d,
        maximumEpisodesPerRun: 2);
Assert(stratifiedExpertSelection.Episodes.Count == 16
       && stratifiedExpertSelection.SelectedAdvancedEpisodes == 4
       && stratifiedExpertSelection.SelectedNormalEpisodes == 12
       && stratifiedExpertSelection.DistinctRuns == 8
       && stratifiedExpertSelection.QuotaShortfalls["advanced"] == 2,
    "expert replay is campaign-first, run-bounded, difficulty-stratified, and reports unavoidable quota shortfalls");
var rewardResidualObservations = Enumerable.Range(0, 60)
    .Select(index => new CombatFoundationCampaignObservation
    {
        CaseId = "reward-residual-" + index,
        IntegrityValid = true,
        FinalBossVictory = index < 30,
        CompletedBattles = index < 30 ? 37 : 34,
        SelectedCards =
        {
            index < 30 ? "learned-good-card" : "learned-bad-card"
        },
        Relics =
        {
            index < 30 ? "relic_learned_good" : "relic_learned_bad"
        },
        Blessings =
        {
            index < 30
                ? "blessing_learned_good"
                : "blessing_learned_bad"
        }
    })
    .ToList();
var rewardResidualTraining =
    CombatFoundationCaseLearning.TrainRewardResiduals(
        rewardResidualObservations);
Assert(rewardResidualTraining.EligibleObservations == 60
       && rewardResidualTraining.Residuals["learned-good-card"] > 0d
       && rewardResidualTraining.Residuals["learned-bad-card"] < 0d
       && rewardResidualTraining.Residuals["relic_learned_good"] > 0d
       && rewardResidualTraining.Residuals["relic_learned_bad"] < 0d
       && rewardResidualTraining.Residuals["blessing_learned_good"] > 0d
       && rewardResidualTraining.Residuals["blessing_learned_bad"] < 0d
       && rewardResidualTraining.CardResiduals == 2
       && rewardResidualTraining.RelicResiduals == 2
       && rewardResidualTraining.BlessingResiduals == 2
       && rewardResidualTraining.Residuals.Values.All(value =>
           Math.Abs(value) <= 0.20d),
    "reward residual learning uses late comparable outcomes and hard-bounds every learned adjustment");

var hardSeedEpisodes = Enumerable.Range(0, 5)
    .Select(index => new CombatEpisode
    {
        EpisodeId = "hard-seed-" + index,
        JourneyRunId = "hard-seed-run-" + index,
        JourneyBattleIndex = 10 + index,
        Campaign = new CombatCampaignEpisodeMetadata
        {
            WorldSeed = (ulong)(50_000 + index),
            DifficultyId = index == 4 ? "advanced" : "normal",
            CampaignCompletedBattles = 10 + index,
            TerminalScenarioId = index < 4
                ? "recurring-gatekeeper"
                : "one-off-failure",
            OutcomeClass = index == 3 ? "victory" : "defeat",
            IntegrityValid = true
        }
    })
    .ToList();
var hardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
    hardSeedEpisodes,
    campaignCount: 8,
    replayShare: 0.5d,
    iteration: 2,
    runSeed: 123456789UL,
    enabled: true);
var hardSeedRepeat = CombatFoundationHardSeedCurriculum.Select(
    hardSeedEpisodes,
    campaignCount: 8,
    replayShare: 0.5d,
    iteration: 2,
    runSeed: 123456789UL,
    enabled: true);
Assert(hardSeedPlan.SourceCampaigns == 4
       && hardSeedPlan.Seeds.Count == 4
       && hardSeedPlan.Seeds.All(seed => seed.WorldSeed != 50_003UL)
       && hardSeedPlan.Seeds
           .Select(seed => seed.WorldSeed)
           .SequenceEqual(hardSeedRepeat.Seeds.Select(seed => seed.WorldSeed))
       && hardSeedPlan.Clusters["recurring-gatekeeper"] == 3,
    "hard-seed curriculum deterministically replays valid prior defeats and emphasizes recurring terminal clusters");
var cooledHardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
    new[]
    {
        new CombatFoundationHardSeedHistoryEntry
        {
            WorldSeed = 66_001UL,
            DifficultyId = "normal",
            TerminalScenarioId = "unsolved-gate",
            FailureOccurrences = 3,
            TrainingAttempts = 2,
            RecoverySuccesses = 0,
            LastTrainedIteration = 4
        }
    },
    campaignCount: 8,
    replayShare: 0.35d,
    iteration: 5,
    runSeed: 123UL,
    enabled: true);
Assert(cooledHardSeedPlan.SourceCampaigns == 0
       && cooledHardSeedPlan.Seeds.Count == 0,
    "repeated hard seeds with no recovery enter a cooldown instead of consuming every following curriculum round");
var hardEncounterCheckpoint = new CombatCampaignCheckpoint
{
    CampaignId = "hard-encounter",
    CampaignVersion = "1",
    DifficultyId = "advanced",
    WorldSeed = 77_001UL,
    PlanHash = "hard-plan",
    PolicyId = "aura-foundation-training:balanced",
    NextEncounterIndex = 5
};
var hardEncounterPlan = CombatFoundationHardSeedCurriculum.Select(
    new[]
    {
        new CombatFoundationHardSeedHistoryEntry
        {
            WorldSeed = 77_001UL,
            DifficultyId = "advanced",
            TerminalScenarioId = "hard-encounter:5:gate",
            CompletedBattles = 6,
            FirstSeenIteration = 1,
            LastSeenIteration = 1,
            FailureOccurrences = 2,
            FailureEncounterCheckpoint = hardEncounterCheckpoint
        }
    },
    campaignCount: 4,
    replayShare: 0.5d,
    iteration: 2,
    runSeed: 99UL,
    enabled: true);
var hardEncounterSchedule = CombatFoundationTrainingSchedule.Build(
    4,
    80_000UL,
    99UL,
    2,
    CombatFoundationCurriculum.Evaluate(
        true,
        2,
        10,
        32,
        0,
        32),
    hardEncounterPlan);
Assert(hardEncounterPlan.Seeds.Single().FailureEncounterCheckpoint
           ?.NextEncounterIndex == 5
       && hardEncounterSchedule.Single(slot => slot.HardSeed)
           .FailureEncounterCheckpoint?.NextEncounterIndex == 5,
    "hard-seed planning carries the compact failed-encounter checkpoint into the training schedule");
var terminalCreditEpisodes = Enumerable.Range(0, 3)
    .Select(index => new CombatEpisode
    {
        JourneyBattleIndex = index,
        Frames =
        {
            new CombatEpisodeFrame(),
            new CombatEpisodeFrame()
        }
    })
    .ToList();
var terminalCreditCampaign = new CombatCampaignResult
{
    WorldSeed = 9001UL,
    DifficultyId = "normal",
    CompletedBattles = 3,
    TotalBattles = 37,
    Battles =
    {
        new CombatSimulationResult
        {
            ScenarioId = "won-1",
            Outcome = CombatSimulationOutcome.Victory
        },
        new CombatSimulationResult
        {
            ScenarioId = "won-2",
            Outcome = CombatSimulationOutcome.Victory
        },
        new CombatSimulationResult
        {
            ScenarioId = "lost-3",
            Outcome = CombatSimulationOutcome.Defeat
        }
    }
};
CombatCampaignFoundationTrainer.ApplyCampaignTargets(
    terminalCreditEpisodes,
    terminalCreditCampaign,
    "terminal-credit-test",
    1);
Assert(terminalCreditEpisodes[0].Frames[0].LongTermReturn > 0d
       && terminalCreditEpisodes[0].Campaign.OutcomeClass
          == "battle-victory"
       && terminalCreditEpisodes[1].Frames[0].LongTermReturn
          > terminalCreditEpisodes[2].Frames[0].LongTermReturn
       && terminalCreditEpisodes[2].Frames[^1].LongTermReturn == -1d,
    "terminal credit preserves local victories while assigning the strongest negative target to the actual failing encounter");
Assert(CombatCampaignFoundationTrainer.ShouldRunCounterfactualHardEncounter(
           new CombatCampaignFoundationTrainingRequest
           {
               EnableCounterfactualHardEncounters = true
           },
           true,
           terminalCreditCampaign)
       && !CombatCampaignFoundationTrainer.ShouldRunCounterfactualHardEncounter(
           new CombatCampaignFoundationTrainingRequest
           {
               EnableCounterfactualHardEncounters = false
           },
           true,
           terminalCreditCampaign),
    "hard-encounter counterfactual replay is gated by protocol setting and a real local defeat");
var counterfactualBaseline = new CombatCampaignResult
{
    Battles =
    {
        new CombatSimulationResult
        {
            Outcome = CombatSimulationOutcome.Defeat,
            Turns = 3,
            Metrics = new CombatSimulationMetrics
            {
                DamageDealt = 40
            }
        }
    }
};
var counterfactualImprovement = new CombatCampaignResult
{
    Battles =
    {
        new CombatSimulationResult
        {
            Outcome = CombatSimulationOutcome.Defeat,
            Turns = 4,
            Metrics = new CombatSimulationMetrics
            {
                DamageDealt = 55
            }
        }
    }
};
var counterfactualVictory = new CombatCampaignResult
{
    Battles =
    {
        new CombatSimulationResult
        {
            Outcome = CombatSimulationOutcome.Victory,
            Turns = 4
        }
    }
};
var counterfactualNoGain = new CombatCampaignResult
{
    Battles =
    {
        new CombatSimulationResult
        {
            Outcome = CombatSimulationOutcome.Defeat,
            Turns = 3,
            Metrics = new CombatSimulationMetrics
            {
                DamageDealt = 44
            }
        }
    }
};
Assert(CombatCampaignFoundationTrainer.ClassifyCounterfactual(
           counterfactualBaseline,
           counterfactualVictory)
       == CombatFoundationCounterfactualAdmission.Victory
       && CombatCampaignFoundationTrainer.ClassifyCounterfactual(
           counterfactualBaseline,
           counterfactualImprovement)
       == CombatFoundationCounterfactualAdmission.Improved
       && CombatCampaignFoundationTrainer.ClassifyCounterfactual(
           counterfactualBaseline,
           counterfactualNoGain)
       == CombatFoundationCounterfactualAdmission.Rejected,
    "counterfactual admission retains victories and measurable improvements while rejecting no-gain teacher defeats");
var ineffectiveHardIterations = new List<CombatCampaignFoundationIteration>
{
    new()
    {
        HardSeedCounterfactualCampaigns = 10
    },
    new()
    {
        HardSeedCounterfactualCampaigns = 10
    }
};
var adaptiveHardRequest = new CombatCampaignFoundationTrainingRequest
{
    HardSeedReplayShare = 0.35d
};
Assert(Math.Abs(
           CombatCampaignFoundationTrainer.EffectiveHardSeedReplayShare(
               adaptiveHardRequest,
               ineffectiveHardIterations)
           - CombatFoundationStagnationProtocol.ReducedHardSeedReplayShare)
       < 0.000001d,
    "hard-seed replay share is reduced after a sustained unsolved counterfactual window");
ineffectiveHardIterations[1].HardSeedCounterfactualVictories = 1;
Assert(Math.Abs(
           CombatCampaignFoundationTrainer.EffectiveHardSeedReplayShare(
               adaptiveHardRequest,
               ineffectiveHardIterations)
           - adaptiveHardRequest.HardSeedReplayShare)
       < 0.000001d,
    "hard-seed replay share remains configured when the recent solve-rate floor is met");
var stagnationIterations = new List<CombatCampaignFoundationIteration>
{
    new() { Promoted = true },
    new() { Promoted = false },
    new() { Promoted = false },
    new() { Promoted = false }
};
Assert(CombatCampaignFoundationTrainer.ShouldStopForStagnation(
           new CombatCampaignFoundationTrainingRequest
           {
               MaximumConsecutiveRejectedIterations = 3
           },
           stagnationIterations,
           hasChampion: true)
       && !CombatCampaignFoundationTrainer.ShouldStopForStagnation(
           new CombatCampaignFoundationTrainingRequest
           {
               MaximumConsecutiveRejectedIterations = 3
           },
           stagnationIterations,
           hasChampion: false),
    "stagnation control stops only after the configured rejected-candidate streak and only when a usable champion exists");
var longArchiveRoot =
    @"D:\Steam\steamapps\common\Witch's Apocalyptic Journey\Witch's Apocalyptic Journey_Data\ModsData\AuraShared\Logs\AuraToolsExp\combat-simulation-results\foundation-success-cases";
var fullCompatibilityKey = new string('a', 64);
var fullCaseId = new string('b', 64);
var compactArchivePath = CombatFoundationCaseArchiveProtocol.EntryPath(
    longArchiveRoot,
    fullCompatibilityKey,
    CombatFoundationCaseArchiveProtocol.ExpertDirectoryName,
    fullCaseId);
var legacyArchivePath = Path.Combine(
    CombatFoundationCaseArchiveProtocol.LegacyCompatibilityDirectory(
        longArchiveRoot,
        fullCompatibilityKey),
    "expert-cases",
    fullCaseId + ".json");
Assert(CombatFoundationCaseArchiveProtocol.Version
           == "success-case-archive-worker-v2"
       && compactArchivePath.Length < 260
       && compactArchivePath.Length < legacyArchivePath.Length
       && compactArchivePath.Contains(
           Path.DirectorySeparatorChar
           + "v2"
           + Path.DirectorySeparatorChar,
           StringComparison.Ordinal)
       && !compactArchivePath.Contains(
           fullCompatibilityKey,
           StringComparison.Ordinal),
    "case archive v2 keeps long Steam install paths below the legacy MAX_PATH boundary while payload ids remain authoritative");
var workerProtocolJob = new CombatFoundationWorkerJob
{
    JobId = "worker-protocol-test"
};
var workerProtocolProgress = new CombatFoundationWorkerProgress
{
    JobId = workerProtocolJob.JobId
};
var workerProtocolResult = new CombatFoundationWorkerResult
{
    JobId = workerProtocolJob.JobId
};
Assert(workerProtocolJob.SchemaVersion
           == CombatFoundationWorkerProtocol.SchemaVersion
       && CombatFoundationWorkerProtocol.SchemaVersion == 7
       && CombatFoundationTerminalCreditProtocol.Version
          == "terminal-credit-v2"
       && CombatFoundationCounterfactualProtocol.Version
          == "hard-encounter-counterfactual-v2"
       && CombatFoundationStagnationProtocol.Version
          == "foundation-stagnation-v1"
       && CombatPolicyValueFrameStratificationProtocol.Version
          == "frame-strata-v1"
       && workerProtocolProgress.SchemaVersion
           == CombatFoundationWorkerProtocol.SchemaVersion
       && workerProtocolResult.SchemaVersion
           == CombatFoundationWorkerProtocol.SchemaVersion
       && new CombatFoundationWorkerCheckpoint().SchemaVersion
           == CombatFoundationWorkerProtocol.SchemaVersion
       && new CombatCampaignFoundationResumeState().SchemaVersion
           == CombatFoundationWorkerProtocol.SchemaVersion
       && new CombatFoundationCompatibilityManifest().SchemaVersion
           == CombatFoundationWorkerProtocol.SchemaVersion,
    "foundation worker artifacts share one protocol version constant");
Assert(CombatFoundationWorkerProtocol.TryValidateJob(
           workerProtocolJob,
           out var validJobDiagnostic)
       && string.IsNullOrEmpty(validJobDiagnostic)
       && CombatFoundationWorkerProtocol.TryValidateProgress(
           workerProtocolProgress,
           workerProtocolJob.JobId,
           out var validProgressDiagnostic)
       && string.IsNullOrEmpty(validProgressDiagnostic)
       && CombatFoundationWorkerProtocol.TryValidateResult(
           workerProtocolResult,
           workerProtocolJob.JobId,
           out var validResultDiagnostic)
       && string.IsNullOrEmpty(validResultDiagnostic),
    "foundation worker host accepts matching job, progress and result artifacts");
workerProtocolProgress.SchemaVersion =
    CombatFoundationWorkerProtocol.SchemaVersion - 1;
Assert(!CombatFoundationWorkerProtocol.TryValidateProgress(
           workerProtocolProgress,
           workerProtocolJob.JobId,
           out var versionDiagnostic)
       && versionDiagnostic.Contains("worker=6", StringComparison.Ordinal)
       && versionDiagnostic.Contains("host=7", StringComparison.Ordinal),
    "foundation worker host rejects stale progress with an actionable protocol diagnostic");
workerProtocolProgress.SchemaVersion =
    CombatFoundationWorkerProtocol.SchemaVersion;
workerProtocolProgress.JobId = "other-worker-job";
Assert(!CombatFoundationWorkerProtocol.TryValidateProgress(
           workerProtocolProgress,
           workerProtocolJob.JobId,
           out var jobIdDiagnostic)
       && jobIdDiagnostic.Contains("jobId 不匹配", StringComparison.Ordinal),
    "foundation worker host rejects progress from a different job with an actionable diagnostic");
var foundationRequest = new CombatCampaignFoundationTrainingRequest
{
    DecisionProfile = "balanced",
    Iterations = 1,
    TrainingCampaignsPerIteration = 2,
    ArenaCampaignsPerDifficulty = 1,
    PreflightCampaignsPerDifficulty = 1,
    PreflightSeedStart = 19_000,
    NormalValidationCampaigns = 5,
    AdvancedValidationCampaigns = 5,
    CapabilityProbeCampaignsPerDifficulty = 1,
    MaximumDegreeOfParallelism = 4,
    CaseArchiveLoad = new CombatFoundationCaseArchiveLoadDiagnostics
    {
        ArchiveExists = true,
        LoadedCases = 3,
        LoadedObservations = 9,
        Message = "fixture"
    },
    TrainingSeedStart = 10_000,
    ArenaSeedStart = 20_000,
    ValidationSeedStart = 30_000,
    TrainingCampaign = projectedTrainingCampaign,
    ValidationCampaign = projectedValidationCampaign,
    Profile = new CombatDecisionProfile
    {
        SearchBudgetMode = "fixed",
        SearchSimulationBudget = 128,
        SearchNodeBudget = 512,
        SearchMaxPly = 4
    },
    Training = new CombatPolicyValueTrainingOptions
    {
        Epochs = 2,
        MinimumEpisodes = 2,
        HiddenDimensions = 8,
        RandomSeed = 73
    }
};
foundationRequest.Resume = new CombatCampaignFoundationResumeState
{
    SchemaVersion = 2,
    Stage = "model-training",
    NextIteration = 1,
    CompletedCampaigns = 999,
    Champion = legacyFeatureModel,
    WorkingChampion = legacyFeatureModel,
    Replay =
    {
        new CombatEpisode
        {
            FeatureSchemaVersion = 6,
            ModelProtocol = CombatPolicyValueProtocol.EpisodeProtocol
        }
    }
};
var incrementallyObservedFoundationCases = 0;
var incrementallyArchivedFoundationCases = 0;
foundationRequest.ObservationRecorded = _ =>
    incrementallyObservedFoundationCases++;
foundationRequest.SuccessCaseRecorded = _ =>
    incrementallyArchivedFoundationCases++;
var foundationTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset);
Assert(foundationTraining.Success
       && foundationTraining.AcceptancePassed
       && foundationTraining.Champion != null
       && foundationTraining.Preflight.Passed
       && foundationTraining.Preflight.CompletedCampaigns == 2
       && foundationTraining.Preflight.InvalidCampaigns == 0
       && foundationTraining.Replay.Count == 16
       && foundationTraining.Replay.All(episode => episode.Authoritative)
       && foundationTraining.ValidationRuns.Count == 10
       && foundationTraining.Validation.NormalCampaigns == 5
       && foundationTraining.Validation.AdvancedCampaigns == 5
       && foundationTraining.Validation.RequiredNormalVictories == 5
       && foundationTraining.Validation.RequiredAdvancedVictories == 3
       && Math.Abs(
           foundationTraining.Validation.RequiredNormalWinRate - 0.9d)
          < 0.0001d
       && Math.Abs(
           foundationTraining.Validation.RequiredAdvancedWinRate - 0.5d)
          < 0.0001d
       && foundationTraining.CompletedCampaigns < 999
       && foundationTraining.CaseArchiveLoad.LoadedCases == 3
       && foundationTraining.CaseArchiveLoad.LoadedObservations == 9
       && foundationTraining.Validation.NormalWinRate == 1d
       && foundationTraining.Validation.AdvancedWinRate == 1d
       && foundationTraining.EffectiveParallelism == 4
       && foundationTraining.PeakConcurrentCampaigns >= 1
       && foundationTraining.ObservedWorkerThreads >= 1
       && foundationTraining.CompletedBattles > 0
       && foundationTraining.MaximumCompletedBattleDepth == 37
       && foundationTraining.Depth1To5Campaigns
          + foundationTraining.Depth6To10Campaigns
          + foundationTraining.Depth11To20Campaigns
          + foundationTraining.Depth21To30Campaigns
          + foundationTraining.Depth31To37Campaigns
          == foundationTraining.CompletedCampaigns
       && foundationTraining.ProjectedBattleDepth == 37d
       && foundationTraining.PolicyDecisions > 0
       && foundationTraining.SearchSimulations > 0
       && foundationTraining.SearchNodes > 0
       && foundationTraining.CapabilityProbe.Arms.Count == 3
       && foundationTraining.CapabilityProbe.Arms.All(arm =>
           arm.NormalCampaigns == 1
           && arm.AdvancedCampaigns == 1
           && arm.InvalidCampaigns == 0)
       && incrementallyObservedFoundationCases
          == foundationTraining.CampaignObservations.Count
       && incrementallyArchivedFoundationCases
          == foundationTraining.SuccessCases.Count
       && incrementallyArchivedFoundationCases > 0
       && foundationTraining.ElapsedSeconds > 0d,
    "foundation trainer reports telemetry and streams successful cases as campaigns complete");
CombatCampaignFoundationResumeState? capturedFoundationCheckpoint = null;
var interruptedFoundationObserved = false;
using (var interruptedFoundation = new CancellationTokenSource())
{
    foundationRequest.Checkpoint = checkpoint =>
    {
        if (checkpoint.Stage == "model-training"
            && checkpoint.ModelTraining == null)
        {
            capturedFoundationCheckpoint = checkpoint;
            interruptedFoundation.Cancel();
        }
    };
    try
    {
        new CombatCampaignFoundationTrainer().Run(
            foundationRequest,
            campaignRules.Ruleset,
            cancellationToken: interruptedFoundation.Token);
    }
    catch (OperationCanceledException)
    {
        interruptedFoundationObserved = true;
    }
}
foundationRequest.Checkpoint = null;
foundationRequest.Resume = capturedFoundationCheckpoint;
var resumedFoundationTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset);
Assert(interruptedFoundationObserved
       && capturedFoundationCheckpoint != null
       && capturedFoundationCheckpoint.SchemaVersion
          == CombatFoundationWorkerProtocol.SchemaVersion
       && capturedFoundationCheckpoint.Compatibility.FeatureSchemaVersion
          == CombatPolicyValueProtocol.FeatureSchemaVersion
       && capturedFoundationCheckpoint.Compatibility.CampaignId
          == foundationRequest.TrainingCampaign.CampaignId
       && !string.IsNullOrWhiteSpace(
           capturedFoundationCheckpoint.Compatibility.TrainingCampaignHash)
       && !string.IsNullOrWhiteSpace(
           capturedFoundationCheckpoint.Compatibility.ValidationCampaignHash)
       && capturedFoundationCheckpoint.Compatibility.FeatureEncodingMode
          == "partitioned-v3"
       && capturedFoundationCheckpoint.Compatibility.StateDimensions == 128
       && capturedFoundationCheckpoint.Compatibility.HiddenDimensions == 8
       && capturedFoundationCheckpoint.CompletedCampaigns == 2
       && capturedFoundationCheckpoint.Replay.Count == 74
       && resumedFoundationTraining.Success
       && resumedFoundationTraining.Champion != null
       && foundationTraining.Champion != null
       && resumedFoundationTraining.Champion.StateWeights.SequenceEqual(
           foundationTraining.Champion.StateWeights)
       && resumedFoundationTraining.Champion.PolicyWeights.SequenceEqual(
           foundationTraining.Champion.PolicyWeights),
    "foundation checkpoints preserve generated episodes and resume at model training without replaying campaigns");
foundationRequest.Resume = null;
foundationRequest.MaximumDegreeOfParallelism = 1;
var serialFoundationTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset);
Assert(serialFoundationTraining.Success
       && serialFoundationTraining.Champion != null
       && serialFoundationTraining.EffectiveParallelism == 1
       && serialFoundationTraining.PeakConcurrentCampaigns == 1
       && foundationTraining.Champion != null
       && serialFoundationTraining.Champion.StateWeights.SequenceEqual(
           foundationTraining.Champion.StateWeights)
       && serialFoundationTraining.Champion.PolicyWeights.SequenceEqual(
           foundationTraining.Champion.PolicyWeights)
       && serialFoundationTraining.ValidationRuns.Select(item =>
               item.DifficultyId + ":" + item.WorldSeed + ":" + item.PlanHash)
           .SequenceEqual(foundationTraining.ValidationRuns.Select(item =>
                item.DifficultyId + ":" + item.WorldSeed + ":" + item.PlanHash)),
    "foundation CPU parallelism preserves deterministic seed-order replay, model weights, and validation plans");
Console.WriteLine(
    "Foundation telemetry fixture: parallel peak="
    + foundationTraining.PeakConcurrentCampaigns
    + "/"
    + foundationTraining.EffectiveParallelism
    + ", observedThreads="
    + foundationTraining.ObservedWorkerThreads
    + ", battles="
    + foundationTraining.CompletedBattles
    + ", elapsed="
    + foundationTraining.ElapsedSeconds.ToString("F3")
    + "s; serial elapsed="
    + serialFoundationTraining.ElapsedSeconds.ToString("F3")
    + "s");
var failingValidationCampaign = BuildStandardCampaign();
failingValidationCampaign.RequireAuthoritativeRules = true;
failingValidationCampaign.Player.MaxHp = 1;
failingValidationCampaign.Player.CurrentHp = 0;
foreach (var difficulty in failingValidationCampaign.Difficulties)
{
    difficulty.ApplyGameLevelShield = false;
}
foundationRequest.MaximumDegreeOfParallelism = 4;
foundationRequest.ValidationCampaign = failingValidationCampaign;
var earlyStoppedFoundationTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset,
    foundationTraining.Champion);
Assert(earlyStoppedFoundationTraining.Success
       && !earlyStoppedFoundationTraining.AcceptancePassed
       && earlyStoppedFoundationTraining.Validation.EarlyStopped
       && earlyStoppedFoundationTraining.Validation.NormalCampaigns == 4
       && earlyStoppedFoundationTraining.Validation.AdvancedCampaigns == 0
       && earlyStoppedFoundationTraining.CompletedCampaigns
          < earlyStoppedFoundationTraining.RequestedCampaigns,
    "foundation validation stops after a deterministic parallel batch once the configured normal acceptance gate is impossible");
projectedStrike.Fidelity = CombatRuleFidelity.Approximate;
var invalidPreflightTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset,
    foundationTraining.Champion);
projectedStrike.Fidelity = CombatRuleFidelity.Authoritative;
Assert(!invalidPreflightTraining.Success
       && !invalidPreflightTraining.Preflight.Passed
       && invalidPreflightTraining.Preflight.InvalidCampaigns == 2
       && invalidPreflightTraining.Preflight.Failures.Select(item =>
               item.DifficultyId + ":" + item.WorldSeed)
           .SequenceEqual(new[] { "normal:19000", "advanced:19001" })
       && invalidPreflightTraining.CompletedCampaigns == 0
       && invalidPreflightTraining.Replay.Count == 0
       && invalidPreflightTraining.Message.Contains(
           "训练前权威快检失败",
           StringComparison.Ordinal),
    "foundation preflight fails before self-play and produces no replay when authoritative execution is invalid");
foundationRequest.PreflightCampaignsPerDifficulty = 0;
projectedStrike.Fidelity = CombatRuleFidelity.Approximate;
var invalidSelfPlayTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset,
    foundationTraining.Champion);
projectedStrike.Fidelity = CombatRuleFidelity.Authoritative;
foundationRequest.PreflightCampaignsPerDifficulty = 1;
Assert(!invalidSelfPlayTraining.Success
       && invalidSelfPlayTraining.CompletedCampaigns == 2
       && invalidSelfPlayTraining.InvalidTrainingCampaigns == 2
       && invalidSelfPlayTraining.TrainingFailures.Count == 2
       && invalidSelfPlayTraining.TrainingFailures.Select(item =>
               item.WorldSeed)
           .SequenceEqual(new ulong[] { 10_000, 10_001 })
       && invalidSelfPlayTraining.TrainingFailures.All(item =>
           item.Reasons.Count > 0)
       && invalidSelfPlayTraining.TrainingFailureCounts.Count > 0
       && invalidSelfPlayTraining.Message.Contains(
           "normal/10000",
           StringComparison.Ordinal),
    "foundation self-play failures retain deterministic seed, depth, and machine-readable reasons");

var dynamicEnemyRules = new CombatRulesetBuilder("dynamic-enemy-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "observe",
        Cost = 0,
        Fidelity = CombatRuleFidelity.Authoritative
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "opening-mark",
        MaximumStacks = 99,
        Fidelity = CombatRuleFidelity.Authoritative
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "dynamic-enemy",
        MaxHp = 20,
        Fidelity = CombatRuleFidelity.Authoritative,
        InitialStatuses =
        {
            new CombatInitialStatus
            {
                StatusId = "opening-mark",
                Stacks = 2,
                ConditionExpression = new CombatSimulationValueExpression
                {
                    Operation = CombatSimulationValueOperation.GreaterThan,
                    Arguments =
                    {
                        new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.SourceVariable,
                            Key = "TagDiff"
                        },
                        new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.Constant,
                            Constant = 20
                        }
                    }
                }
            }
        },
        Intents =
        {
            new CombatEnemyIntentDefinition
            {
                IntentId = "ordinary",
                Priority = 1,
                Weight = 1
            },
            new CombatEnemyIntentDefinition
            {
                IntentId = "advanced",
                Priority = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.CopyStatuses,
                        Target = CombatSimulationTarget.Player
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.ModifyVariablePercent,
                        Target = CombatSimulationTarget.Player,
                        DefinitionId = "HealMultiplier",
                        Amount = -20
                    }
                },
                PriorityExpression = new CombatSimulationValueExpression
                {
                    Operation = CombatSimulationValueOperation.Conditional,
                    Arguments =
                    {
                        new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.GreaterThan,
                            Arguments =
                            {
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceVariable,
                                    Key = "TagDiff"
                                },
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.Constant,
                                    Constant = 20
                                }
                            }
                        },
                        new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.Constant,
                            Constant = 5
                        },
                        new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.Constant,
                            Constant = 0
                        }
                    }
                },
                Weight = 1
            }
        }
    })
    .Freeze();
CombatSimulationResult RunDynamicEnemy(double tagDiff)
{
    return new CombatSimulationEngine().Run(
        new CombatScenarioDefinition
        {
            ScenarioId = "dynamic-enemy-" + tagDiff,
            RulesetVersion = "dynamic-enemy-v1",
            Seed = 9,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Player = new CombatPlayerSetup
            {
                RoleId = "tests",
                MaxHp = 20,
                CurrentHp = 20,
                Deck = { "observe" },
                InitialStatuses =
                {
                    new CombatInitialStatus
                    {
                        StatusId = "opening-mark",
                        Stacks = 3
                    }
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = "dynamic-enemy",
                    Variables = { ["TagDiff"] = tagDiff }
                }
            },
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 20,
                MaximumCommands = 100
            }
        },
        dynamicEnemyRules.Ruleset,
        new GreedyCombatSimulationPolicy());
}
var normalDynamicEnemy = RunDynamicEnemy(0);
var advancedDynamicEnemy = RunDynamicEnemy(40);
Assert(normalDynamicEnemy.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.IntentSelected
        && item.DefinitionId == "ordinary"),
    "normal enemy variables select the ordinary intent");
Assert(normalDynamicEnemy.FinalState.LivingEnemies.Single().Statuses.All(status =>
        status.StatusId != "opening-mark"),
    "normal enemy variables skip the advanced opening status");
Assert(advancedDynamicEnemy.Events.Any(item =>
        item.Kind == CombatSimulationEventKind.IntentSelected
        && item.DefinitionId == "advanced"),
    "advanced enemy variables select the dynamically prioritized intent");
Assert(advancedDynamicEnemy.FinalState.LivingEnemies.Single().Statuses.Single(status =>
        status.StatusId == "opening-mark").Stacks == 5
       && Math.Abs(
           advancedDynamicEnemy.FinalState.Player!.Variables["HealMultiplier"] - 0.8d)
       < 0.000001d,
    "enemy definitions apply opening statuses, status copying, and percent variables");

var hiddenOrderA = CombatPlayerObservationBoundary.Normalize(
    BuildPlayerEquivalentFixture(reverseHiddenDrawOrder: false));
var hiddenOrderB = CombatPlayerObservationBoundary.Normalize(
    BuildPlayerEquivalentFixture(reverseHiddenDrawOrder: true));
Assert(hiddenOrderA.InformationBoundaryVersion == 2
       && !hiddenOrderA.Features.ContainsKey("secretRngCounter")
       && !hiddenOrderA.Player.Features.ContainsKey("ResurrectionSource")
       && hiddenOrderA.Fingerprint == hiddenOrderB.Fingerprint,
    "hidden draw order and internal variables cannot change the public observation");
var hiddenFeaturesA = CombatPolicyValueEncoding.BuildStateFeatures(hiddenOrderA);
var hiddenFeaturesB = CombatPolicyValueEncoding.BuildStateFeatures(hiddenOrderB);
Assert(hiddenFeaturesA.OrderBy(pair => pair.Key)
           .SequenceEqual(hiddenFeaturesB.OrderBy(pair => pair.Key)),
    "hidden-state permutations produce identical policy features");
var hiddenBeliefA = CombatBeliefTracker.FromObservation(hiddenOrderA);
var hiddenBeliefB = CombatBeliefTracker.FromObservation(hiddenOrderB);
var hiddenSampleSeed = CombatPublicObservationHasher.Seed(hiddenOrderA, 7);
Assert(CombatRootDeterminizer.SampleDrawPile(hiddenBeliefA, hiddenSampleSeed)
           .SequenceEqual(
               CombatRootDeterminizer.SampleDrawPile(
                   hiddenBeliefB,
                   hiddenSampleSeed)),
    "belief determinization depends on public knowledge rather than authoritative order");
var invariantProfile = new CombatDecisionProfile
{
    SearchBudgetMode = "fixed",
    SearchSimulationBudget = 24,
    SearchMinimumSimulations = 8,
    SearchNodeBudget = 512,
    SearchMaxPly = 4
};
var hiddenDecisionA = new CombatDecisionEngine().Choose(hiddenOrderA, invariantProfile);
var hiddenDecisionB = new CombatDecisionEngine().Choose(hiddenOrderB, invariantProfile);
Assert(hiddenDecisionA.Action?.CandidateId == hiddenDecisionB.Action?.CandidateId,
    "player-equivalent search is invariant to hidden draw-order permutations");

var revealedTopA = BuildPlayerEquivalentFixture(reverseHiddenDrawOrder: false);
revealedTopA.DeckKnowledge.KnownTopCardIds.Add("guard");
var revealedTopB = BuildPlayerEquivalentFixture(reverseHiddenDrawOrder: false);
revealedTopB.DeckKnowledge.KnownTopCardIds.Add("setup");
var normalizedRevealA = CombatPlayerObservationBoundary.Normalize(revealedTopA);
var normalizedRevealB = CombatPlayerObservationBoundary.Normalize(revealedTopB);
var revealedSample = CombatRootDeterminizer.SampleDrawPile(
    CombatBeliefTracker.FromObservation(normalizedRevealA),
    19);
Assert(normalizedRevealA.Fingerprint != normalizedRevealB.Fingerprint
       && revealedSample.Last() == "guard",
    "public card reveals change the observation and constrain root determinization");

var tokenSource = new object();
var tokenTarget = new object();
var tokenContext = new CombatExecutionContext { ObservationId = "battle:9" };
var currentTokenAction = new CombatActionObservation
{
    ObservationId = "battle:9",
    ActionToken = "a0",
    CandidateId = "attack"
};
tokenContext.Bind(currentTokenAction, tokenSource, tokenTarget);
var staleTokenAction = new CombatActionObservation
{
    ObservationId = "battle:10",
    ActionToken = "a0",
    CandidateId = "attack"
};
Assert(tokenContext.TryResolve(currentTokenAction, out var currentBinding)
       && ReferenceEquals(currentBinding.SourceHandle, tokenSource)
       && !tokenContext.TryResolve(staleTokenAction, out _),
    "execution bindings accept the current observation and reject stale tokens");
var aiDtoTypes = new[]
{
    typeof(PlayerCombatObservation),
    typeof(CombatStateObservation),
    typeof(CombatActionObservation),
    typeof(CombatUnitObservation)
};
Assert(aiDtoTypes.All(type =>
        type.GetProperties().All(property => property.PropertyType != typeof(object))),
    "AI observation DTOs contain no runtime object handles");
using (CombatPublicFeatureRegistry.Register(
           "Tests",
           CombatPublicFeatureScope.State,
           "visibleModCounter"))
{
    var registeredFeatureState = BuildPlayerEquivalentFixture(false);
    registeredFeatureState.Features["visibleModCounter"] = 4d;
    Assert(CombatPlayerObservationBoundary.Normalize(registeredFeatureState)
               .Features["visibleModCounter"] == 4d,
        "mods can explicitly register a player-visible derived feature");
}
var unregisteredFeatureState = BuildPlayerEquivalentFixture(false);
unregisteredFeatureState.Features["visibleModCounter"] = 4d;
Assert(!CombatPlayerObservationBoundary.Normalize(unregisteredFeatureState)
        .Features.ContainsKey("visibleModCounter"),
    "unregistered derived features fail closed at the observation boundary");

var promptRequest = CombatInteractionBroker.Begin(
    new CombatInteractionHint { Purpose = "visibility-gate" },
    1,
    null);
Assert(CombatInteractionBroker.Snapshot()?.Choices.Count == 0,
    "prompt choices remain hidden before the native UI publishes them");
CombatInteractionBroker.PublishVisibleChoices(
    promptRequest.RequestId,
    new[]
    {
        new CombatActionObservation
        {
            ObservationId = "prompt",
            ActionToken = "prompt:0",
            CandidateId = "visible-choice"
        }
    });
Assert(CombatInteractionBroker.Snapshot()?.Choices.Single().CandidateId
       == "visible-choice",
    "prompt choices become observable only after the UI visibility gate");
CombatInteractionBroker.Clear(promptRequest.RequestId);

var projectedOrderA = ProjectPlayerEquivalentHiddenOrder(
    bundledRulesV2.Ruleset,
    reverseHiddenDrawOrder: false,
    hiddenVariable: 10d);
var projectedOrderB = ProjectPlayerEquivalentHiddenOrder(
    bundledRulesV2.Ruleset,
    reverseHiddenDrawOrder: true,
    hiddenVariable: 900d);
Assert(projectedOrderA.Fingerprint == projectedOrderB.Fingerprint
       && !projectedOrderA.Features.ContainsKey("player.SecretCounter")
       && CombatPolicyValueEncoding.BuildStateFeatures(projectedOrderA)
           .OrderBy(pair => pair.Key)
           .SequenceEqual(
               CombatPolicyValueEncoding.BuildStateFeatures(projectedOrderB)
                   .OrderBy(pair => pair.Key)),
    "headless projection obeys the same hidden-state invariants as live observations");

var rebirthInsurance = BuildPlayerEquivalentFixture(false);
rebirthInsurance.Player.CurrentHp = 5;
rebirthInsurance.Player.Statuses.Add(
    new CombatStatusObservation { StatusId = "buff_rebirth", Level = 30 });
rebirthInsurance.DeckCardIds =
    new List<string> { "strike", "guard", "setup" };
rebirthInsurance.HandCardIds = new List<string> { "blood-price" };
rebirthInsurance.HandCount = 1;
rebirthInsurance.Actions = new List<CombatActionObservation>
{
    new()
    {
        CandidateId = "blood-price",
        SourceId = "blood-price",
        Kind = CombatActionKind.PlayCard,
        Cost = 0,
        Semantics = new CombatActionSemantics { SelfHpLoss = 5d }
    }
};
var insuranceAssessment = CombatArchetypePolicy.Enrich(rebirthInsurance);
Assert(insuranceAssessment.RebirthCommitment
           == CombatArchetypeCommitment.None
       && !CombatArchetypePolicy.IsLegal(
           rebirthInsurance,
           rebirthInsurance.Actions[0],
           out _),
    "rebirth remains insurance and cannot justify intentional lethal damage outside a committed build");
var insuranceForward = CombatForwardModel.Apply(
    CombatForwardModel.Create(rebirthInsurance, 1),
    rebirthInsurance.Actions[0],
    0,
    CombatForwardModel.Resolve(
        rebirthInsurance,
        rebirthInsurance.Actions[0]).Outcomes[0],
    new CombatDecisionProfile());
Assert(insuranceForward.PlayerHp == 30
       && insuranceForward.Features[
           CombatArchetypePolicy.RebirthStacksFeature] == 0d
       && insuranceForward.Features[
           CombatArchetypePolicy.ResurrectionCountFeature] == 1d,
    "the forward model still consumes a non-committed rebirth buff as automatic battle insurance");

var committedRebirth = BuildPlayerEquivalentFixture(false);
committedRebirth.Player.CurrentHp = 5;
committedRebirth.Player.Statuses.Add(
    new CombatStatusObservation { StatusId = "buff_rebirth", Level = 30 });
committedRebirth.DeckCardIds = new List<string>
{
    "Crowdfundingcard_6",
    "Crowdfundingcard_8",
    "Crowdfundingcard_10",
    "Crowdfundingcard_11"
};
committedRebirth.HandCardIds = new List<string> { "blood-price" };
committedRebirth.HandCount = 1;
committedRebirth.Actions = new List<CombatActionObservation>
{
    new()
    {
        CandidateId = "blood-price",
        SourceId = "blood-price",
        Kind = CombatActionKind.PlayCard,
        Cost = 0,
        Semantics = new CombatActionSemantics { SelfHpLoss = 5d }
    },
    new()
    {
        CandidateId = "origin",
        SourceId = "Crowdfundingcard_10",
        Kind = CombatActionKind.PlayCard,
        Cost = 1
    }
};
var committedAssessment = CombatArchetypePolicy.Enrich(committedRebirth);
Assert(committedAssessment.RebirthCommitment
           == CombatArchetypeCommitment.Committed
       && CombatArchetypePolicy.IsLegal(
           committedRebirth,
           committedRebirth.Actions[0],
           out _)
       && !CombatArchetypePolicy.IsLegal(
           committedRebirth,
           committedRebirth.Actions[1],
           out _),
    "committed rebirth builds may certify lethal conversion but preserve the 30-stack insurance floor");

var uncoveredLifeConversion = BuildPlayerEquivalentFixture(false);
uncoveredLifeConversion.Player.CurrentHp = 10;
uncoveredLifeConversion.Player.MaxHp = 30;
uncoveredLifeConversion.ExpectedIncomingDamage = 5d;
uncoveredLifeConversion.DeckCardIds = new List<string>
{
    "Crowdfundingcard_6",
    "Crowdfundingcard_8",
    "Crowdfundingcard_10",
    "Crowdfundingcard_11",
    "SpellCard_17"
};
uncoveredLifeConversion.Actions = new List<CombatActionObservation>
{
    new()
    {
        CandidateId = "starfall",
        SourceId = "SpellCard_17",
        Kind = CombatActionKind.PlayCard,
        Cost = 1
    }
};
CombatArchetypePolicy.Enrich(uncoveredLifeConversion);
var uncoveredRejected = !CombatArchetypePolicy.IsLegal(
    uncoveredLifeConversion,
    uncoveredLifeConversion.Actions[0],
    out _);
uncoveredLifeConversion.Player.Statuses.Add(
    new CombatStatusObservation { StatusId = "buff_rebirth", Level = 30 });
CombatArchetypePolicy.Enrich(uncoveredLifeConversion);
Assert(uncoveredRejected
       && CombatArchetypePolicy.IsLegal(
           uncoveredLifeConversion,
           uncoveredLifeConversion.Actions[0],
           out _),
    "high-risk rebirth support requires either a survivable post-action state or a ready insurance stack");

var emptyCage = BuildPlayerEquivalentFixture(false);
emptyCage.DeckCardIds = new List<string>
{
    "timekeeper_4",
    "timekeeper_9",
    "timekeeper_10",
    "timekeeper_14"
};
emptyCage.Actions = new List<CombatActionObservation>
{
    new()
    {
        CandidateId = "empty-cage",
        SourceId = "timekeeper_4",
        Kind = CombatActionKind.PlayCard
    }
};
var emptyCageAssessment = CombatArchetypePolicy.Enrich(emptyCage);
Assert(emptyCageAssessment.TimeCageCommitment
           == CombatArchetypeCommitment.Committed
       && !CombatArchetypePolicy.IsLegal(
           emptyCage,
           emptyCage.Actions[0],
           out _),
    "time-cage commitment does not make an empty queue operator legal");

var unsafePackage = BuildPlayerEquivalentFixture(false);
unsafePackage.DeckCardIds = new List<string>
{
    "timekeeper_9",
    "timekeeper_10",
    "timekeeper_12",
    "timekeeper_17"
};
unsafePackage.HandCardIds =
    new List<string> { "timekeeper_12", "luckycard_4" };
unsafePackage.HandCount = 2;
unsafePackage.Actions = new List<CombatActionObservation>
{
    new()
    {
        CandidateId = "unsafe-package",
        SourceId = "timekeeper_12",
        Kind = CombatActionKind.PlayCard
    }
};
CombatArchetypePolicy.Enrich(unsafePackage);
Assert(!CombatArchetypePolicy.IsLegal(
        unsafePackage,
        unsafePackage.Actions[0],
        out _),
    "package cannot hide a hard-banned curse-alchemy execution");
unsafePackage.HandCardIds =
    new List<string> { "timekeeper_12", "strike" };
unsafePackage.HandCount = 2;
CombatArchetypePolicy.Enrich(unsafePackage);
Assert(CombatArchetypePolicy.IsLegal(
        unsafePackage,
        unsafePackage.Actions[0],
        out _),
    "package remains legal for an eligible low-risk payload");

var orderedCage = BuildPlayerEquivalentFixture(false);
orderedCage.Player.CurrentHp = 20;
orderedCage.Player.MaxHp = 20;
orderedCage.Enemies[0].CurrentHp = 8;
orderedCage.Enemies[0].MaxHp = 8;
orderedCage.HandCardIds.Clear();
orderedCage.HandCount = 0;
orderedCage.DeckCardIds = new List<string>
{
    "timekeeper_4",
    "timekeeper_9",
    "timekeeper_14",
    "timekeeper_17"
};
orderedCage.DeferredEffects = new List<CombatDeferredEffectObservation>
{
    new()
    {
        Sequence = 0,
        StatusId = "buff_timelock",
        SourceId = "timekeeper_14"
    },
    new()
    {
        Sequence = 1,
        StatusId = "buff_timelock",
        SourceId = "timekeeper_17"
    }
};
CombatArchetypePolicy.Enrich(orderedCage);
var orderedCageForward = CombatForwardModel.ApplyEndTurn(
    CombatForwardModel.Create(orderedCage, 0),
    new CombatDecisionProfile());
Assert(orderedCageForward.DeferredEffects.Count == 0
       && orderedCageForward.PlayerDefend == 2
       && orderedCageForward.Enemies[0].Hp == 6,
    "time-cage effects resolve in queue order before enemy actions and then clear");
var discardedCagePayload = BuildPlayerEquivalentFixture(false);
discardedCagePayload.Player.CurrentHp = 20;
discardedCagePayload.Player.MaxHp = 20;
discardedCagePayload.Enemies[0].CurrentHp = 8;
discardedCagePayload.Enemies[0].MaxHp = 8;
discardedCagePayload.HandCardIds = new List<string> { "timekeeper_17" };
discardedCagePayload.HandCount = 1;
discardedCagePayload.Features["drawPerTurn"] = 0d;
CombatArchetypePolicy.Enrich(discardedCagePayload);
var discardedCageForward = CombatForwardModel.ApplyEndTurn(
    CombatForwardModel.Create(discardedCagePayload, 0),
    new CombatDecisionProfile());
Assert(discardedCageForward.DeferredEffects.Count == 1
       && discardedCageForward.Enemies[0].Hp == 6,
    "discard-triggered time-cage payloads are queued after the current turn resolution and apply their immediate effect");
var surplusPower = BuildPlayerEquivalentFixture(false);
surplusPower.CurrentPower = 5;
surplusPower.MaxPower = 3;
surplusPower.HandCardIds.Clear();
surplusPower.HandCount = 0;
surplusPower.Features["drawPerTurn"] = 0d;
var surplusPowerForward = CombatForwardModel.ApplyEndTurn(
    CombatForwardModel.Create(surplusPower, 0),
    new CombatDecisionProfile());
Assert(surplusPowerForward.Power == 5,
    "end-turn energy reset restores deficits but preserves energy above the normal maximum");
var reversedCage = BuildPlayerEquivalentFixture(false);
reversedCage.DeferredEffects = new List<CombatDeferredEffectObservation>
{
    new()
    {
        Sequence = 0,
        StatusId = "buff_timelock",
        SourceId = "timekeeper_17"
    },
    new()
    {
        Sequence = 1,
        StatusId = "buff_timelock",
        SourceId = "timekeeper_14"
    }
};
Assert(CombatPlayerObservationBoundary.Normalize(orderedCage).Fingerprint
       != CombatPlayerObservationBoundary.Normalize(reversedCage).Fingerprint,
    "time-cage queue order is part of the player-visible decision state");

Console.WriteLine($"AuraCombatAiShared.Tests passed: {assertions} assertions.");

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }

    assertions++;
}

CombatStateObservation BuildPlayerEquivalentFixture(bool reverseHiddenDrawOrder)
{
    var state = new CombatStateObservation
    {
        BattleSessionId = 77,
        Sequence = 4,
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            Kind = CombatTargetKind.Self,
            CurrentHp = 24,
            MaxHp = 30,
            Features =
            {
                ["ResurrectionSource"] = reverseHiddenDrawOrder ? 99d : 1d
            }
        },
        CurrentPower = 2,
        MaxPower = 3,
        HandCount = 1,
        HandCardIds = { "strike" },
        DeckCardIds = reverseHiddenDrawOrder
            ? new List<string> { "setup", "guard", "strike" }
            : new List<string> { "strike", "guard", "setup" },
        DeckKnowledge = new CombatDeckKnowledge
        {
            DrawPileCount = 2,
            KnownDeckCardIds = { "strike", "guard", "setup" }
        },
        Features =
        {
            ["turn"] = 1d,
            ["drawPileCount"] = 2d,
            ["secretRngCounter"] = reverseHiddenDrawOrder ? 700d : 3d
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                DefinitionId = "test-enemy",
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 8,
                MaxHp = 8
            }
        },
        Actions =
        {
            new CombatActionObservation
            {
                CandidateId = "strike:enemy",
                SourceId = "strike",
                Kind = CombatActionKind.PlayCard,
                RuntimeId = 2000,
                TargetRuntimeId = 2,
                TargetKind = CombatTargetKind.Enemy,
                Cost = 1,
                Semantics = new CombatActionSemantics { Damage = 5d },
                Features = { ["isCard"] = 1d }
            },
            new CombatActionObservation
            {
                CandidateId = "end-turn",
                SourceId = "end-turn",
                Kind = CombatActionKind.EndTurn,
                RuntimeId = 9000
            }
        }
    };
    return state;
}

CombatStateObservation ProjectPlayerEquivalentHiddenOrder(
    CombatRuleset ruleset,
    bool reverseHiddenDrawOrder,
    double hiddenVariable)
{
    var state = new CombatBattleState
    {
        Turn = 1,
        ActionSequence = 2,
        Phase = CombatSimulationPhase.PlayerAction,
        PlayerActorId = 1,
        Actors =
        {
            new CombatActorState
            {
                ActorId = 1,
                Kind = CombatSimulationActorKind.Player,
                Hp = 20,
                MaxHp = 20,
                Energy = 2,
                BaseEnergy = 3,
                Variables = { ["SecretCounter"] = hiddenVariable }
            }
        },
        Cards =
        {
            new CombatCardInstanceState { InstanceId = 1, CardId = "luckycard_3" },
            new CombatCardInstanceState { InstanceId = 2, CardId = "card_1" },
            new CombatCardInstanceState { InstanceId = 3, CardId = "ritualcard_1" }
        },
        Hand = { 1 }
    };
    if (reverseHiddenDrawOrder)
    {
        state.DrawPile.Add(3);
        state.DrawPile.Add(2);
    }
    else
    {
        state.DrawPile.Add(2);
        state.DrawPile.Add(3);
    }
    return PlayerEquivalentSimulationObservationProjector.Project(
        new CombatSimulationPolicyContext
        {
            Scenario = new CombatScenarioDefinition
            {
                ScenarioId = "player-equivalent-projection",
                Seed = 81,
                HandLimit = 10
            },
            Ruleset = ruleset,
            State = state,
            LegalActions = new List<CombatSimulationAction>
            {
                new CombatSimulationAction
                {
                    CandidateId = "play-visible-card",
                    Kind = CombatSimulationActionKind.PlayCard,
                    CardInstanceId = 1,
                    DefinitionId = "luckycard_3",
                    Cost = 1
                }
            }
        });
}

CombatCampaignDefinition BuildStandardCampaign()
{
    var result = new CombatCampaignDefinition
    {
        CampaignId = "tests.standard-campaign",
        CampaignVersion = "2.0.0",
        RulesetVersion = "campaign-test-rules",
        MainAttributeId = "Strength",
        SecondaryAttributeId = "Wisdom",
        Player = new CombatPlayerSetup
        {
            RoleId = "Tests",
            MaxHp = 20,
            CurrentHp = 20,
            BaseEnergy = 3,
            Deck = new List<string> { "strike" }
        },
        RolePrior = { ["burst"] = 0.5d },
        BuildTendency = { ["burst"] = 0.5d },
        BossPreference = { ["burst"] = 0.5d }
    };
    var presets = new[]
    {
        (10, 7, 5), (20, 10, 7), (25, 15, 10), (30, 20, 15),
        (35, 30, 17), (35, 35, 20), (40, 39, 20)
    };
    var route = new List<CombatCampaignEncounterKind>
    {
        CombatCampaignEncounterKind.Normal,
        CombatCampaignEncounterKind.Normal,
        CombatCampaignEncounterKind.Elite,
        CombatCampaignEncounterKind.Normal,
        CombatCampaignEncounterKind.Normal,
        CombatCampaignEncounterKind.Boss
    };
    for (var layer = 1; layer <= 7; layer++)
    {
        var preset = presets[layer - 1];
        result.Layers.Add(new CombatCampaignLayerDefinition
        {
            LayerNumber = layer,
            NativeBand = layer == 7 ? 3 : (layer - 1) / 2,
            Attributes = new CombatCampaignAttributePreset
            {
                Main = preset.Item1,
                Secondary = preset.Item2,
                Unselected = preset.Item3
            },
            Route = layer == 7
                ? new List<CombatCampaignEncounterKind>
                {
                    CombatCampaignEncounterKind.FinalBoss
                }
                : new List<CombatCampaignEncounterKind>(route)
        });
    }
    for (var band = 0; band <= 2; band++)
    {
        foreach (var kind in new[]
                 {
                     CombatCampaignEncounterKind.Normal,
                     CombatCampaignEncounterKind.Elite,
                     CombatCampaignEncounterKind.Boss
                 })
        {
            result.Encounters.Add(new CombatCampaignEncounterDefinition
            {
                EncounterId = "band-" + band + "-" + kind,
                NativeBand = band,
                Kind = kind,
                EnemyIds = new List<string> { "enemy_" + band + "_" + kind }
            });
            result.Enemies.Add(new CombatCampaignEnemyCatalogEntry
            {
                EnemyId = "enemy_" + band + "_" + kind,
                NativeLevel = band + 1
            });
        }
    }
    result.Encounters.Add(new CombatCampaignEncounterDefinition
    {
        EncounterId = "universal-normal",
        NativeBand = -1,
        Kind = CombatCampaignEncounterKind.Normal,
        EnemyIds = new List<string> { "enemy_universal" }
    });
    result.Enemies.Add(new CombatCampaignEnemyCatalogEntry
    {
        EnemyId = "enemy_universal",
        NativeLevel = 1
    });
    foreach (var finalId in new[]
             {
                 "caroline-final", "evernight-final", "demon-king-final", "judgment-final"
             })
    {
        result.Encounters.Add(new CombatCampaignEncounterDefinition
        {
            EncounterId = finalId,
            NativeBand = 3,
            Kind = CombatCampaignEncounterKind.FinalBoss,
            EnemyIds = new List<string> { "enemy_" + finalId }
        });
        result.Enemies.Add(new CombatCampaignEnemyCatalogEntry
        {
            EnemyId = "enemy_" + finalId,
            NativeLevel = 4
        });
    }
    result.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = "strike",
        Kind = CombatCampaignRewardKind.Card,
        Tier = 1,
        BaseValue = 1d,
        Fidelity = CombatRuleFidelity.Authoritative,
        Features = { ["burst"] = 1d }
    });
    result.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = "guard",
        Kind = CombatCampaignRewardKind.Card,
        Tier = 1,
        BaseValue = 0.2d,
        Fidelity = CombatRuleFidelity.Authoritative
    });
    result.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = "skip-me",
        Kind = CombatCampaignRewardKind.Card,
        Tier = 1,
        BaseValue = -2d,
        Fidelity = CombatRuleFidelity.Authoritative
    });
    result.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = "SpellCard_1",
        Kind = CombatCampaignRewardKind.Card,
        CardAcquisition = CombatCampaignCardAcquisition.GeneratedOnly,
        Tier = 4,
        BaseValue = 100d,
        Fidelity = CombatRuleFidelity.Authoritative
    });
    for (var index = 1; index <= 40; index++)
    {
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "relic_" + index,
            Kind = CombatCampaignRewardKind.Relic,
            Tier = (index - 1) % 4 + 1,
            BaseValue = index / 100d,
            Fidelity = CombatRuleFidelity.Authoritative
        });
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "blessing_" + index,
            Kind = CombatCampaignRewardKind.Blessing,
            Tier = (index - 1) % 4 + 1,
            BaseValue = index / 100d,
            Fidelity = CombatRuleFidelity.Authoritative
        });
    }
    result.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = "negative-blessing",
        Kind = CombatCampaignRewardKind.Blessing,
        Tier = 3,
        Negative = true,
        Fidelity = CombatRuleFidelity.Authoritative
    });
    result.Difficulties.Add(new CombatCampaignDifficultyDefinition
    {
        DifficultyId = "normal",
        DisplayName = "普通难度"
    });
    result.Difficulties.Add(new CombatCampaignDifficultyDefinition
    {
        DifficultyId = "advanced",
        DisplayName = "高级难度",
        EnemyHpMultiplier = 1.4d,
        EnemyAttackMultiplier = 1.4d,
        ApplyGameLevelShield = true,
        HardAffixes =
        {
            new CombatCampaignHardAffixDefinition
            {
                AffixId = "Hard_3",
                Stacks = 4,
                CombatRelevant = true,
                Implemented = true
            }
        }
    });
    return result;
}

CombatRulesetBuildResult BuildSimulationRuleset(string version = "test-v1")
{
    return new CombatRulesetBuilder(version)
        .RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "Tests",
            StatusId = "looping",
            DisplayName = "Looping",
            DecayAtRoundEnd = false,
            Triggers =
            {
                new CombatStatusTriggerDefinition
                {
                    TriggerId = "repeat-status",
                    EventKind = CombatSimulationEventKind.StatusAdded,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.AddStatus,
                            Target = CombatSimulationTarget.Self,
                            DefinitionId = "looping",
                            Amount = 1
                        }
                    }
                }
            }
        })
        .RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "Tests",
            StatusId = "training",
            DisplayName = "Training",
            DecayAtRoundEnd = false,
            Triggers =
            {
                new CombatStatusTriggerDefinition
                {
                    TriggerId = "guard-after-card",
                    EventKind = CombatSimulationEventKind.CardPlayed,
                    Priority = 10,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.GainBlock,
                            Target = CombatSimulationTarget.Self,
                            Amount = 2
                        }
                    }
                }
            }
        })
        .RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "loop-seed",
            DisplayName = "Loop Seed",
            Cost = 0,
            Exhaust = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.AddStatus,
                    Target = CombatSimulationTarget.Self,
                    DefinitionId = "looping",
                    Amount = 1
                }
            }
        })
        .RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "strike",
            DisplayName = "Strike",
            Cost = 1,
            RequiresEnemyTarget = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Damage,
                    Target = CombatSimulationTarget.SelectedEnemy,
                    Amount = 6
                }
            }
        })
        .RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "guard",
            DisplayName = "Guard",
            Cost = 1,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.GainBlock,
                    Target = CombatSimulationTarget.Self,
                    Amount = 5
                }
            }
        })
        .RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "insight",
            DisplayName = "Insight",
            Cost = 0,
            Exhaust = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Draw,
                    Target = CombatSimulationTarget.Self,
                    Amount = 1
                }
            }
        })
        .RegisterEnemy(new CombatEnemyDefinition
        {
            OwnerModId = "Tests",
            EnemyId = "dummy",
            DisplayName = "Training Dummy",
            MaxHp = 18,
            ActionCount = 2,
            Intents =
            {
                new CombatEnemyIntentDefinition
                {
                    IntentId = "hit",
                    DisplayName = "Hit",
                    Weight = 1,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.Damage,
                            Target = CombatSimulationTarget.Player,
                            Amount = 4
                        }
                    }
                },
                new CombatEnemyIntentDefinition
                {
                    IntentId = "wait",
                    DisplayName = "Wait",
                    Weight = 1,
                    Effects = new List<CombatSimulationEffectDefinition>()
                }
            }
        })
        .Freeze();
}

CombatScenarioDefinition BuildSimulationScenario(
    ulong seed,
    CombatSimulationTraceLevel traceLevel)
{
    return new CombatScenarioDefinition
    {
        ScenarioId = "training-dummy",
        RulesetVersion = "test-v1",
        Seed = seed,
        InitialDraw = 4,
        DrawPerTurn = 4,
        HandLimit = 10,
        TraceLevel = traceLevel,
        Player = new CombatPlayerSetup
        {
            RoleId = "tester",
            MaxHp = 30,
            CurrentHp = 30,
            BaseEnergy = 3,
            Deck = { "strike", "strike", "guard", "insight" },
            InitialStatuses =
            {
                new CombatInitialStatus
                {
                    StatusId = "training",
                    Stacks = 1
                }
            }
        },
        Enemies =
        {
            new CombatEnemySetup
            {
                EnemyId = "dummy",
                InstanceKey = "dummy:1"
            }
        },
        Limits = new CombatSimulationLimits
        {
            MaximumTurns = 20,
            MaximumActions = 100,
            MaximumCommands = 1000
        }
    };
}

sealed class ReentrantDiscardExtensionFactory :
    ICombatSimulationRuntimeExtensionFactory
{
    public ICombatSimulationRuntimeExtension Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        return new ReentrantDiscardExtension();
    }
}

sealed class TestResurrectionExtensionFactory :
    ICombatSimulationRuntimeExtensionFactory
{
    public ICombatSimulationRuntimeExtension Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        return new TestResurrectionExtension();
    }
}

sealed class TestLateEscapeExtensionFactory :
    ICombatSimulationRuntimeExtensionFactory
{
    public ICombatSimulationRuntimeExtension Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        return new TestLateEscapeExtension();
    }
}

sealed class TestLateEscapeExtension : ICombatSimulationRuntimeExtension
{
    public void Initialize(ICombatSimulationRuntimeContext context)
    {
    }

    public void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        if (sourceEvent.Kind != CombatSimulationEventKind.BattleEnded
            || context.State.Outcome != CombatSimulationOutcome.Defeat)
        {
            return;
        }
        var player = context.State.Player;
        if (player == null)
        {
            return;
        }
        player.Hp = Math.Min(player.MaxHp, 5);
        context.Terminate(
            CombatSimulationOutcome.Victory,
            CombatTerminationReason.Victory);
    }

    public void Complete(ICombatSimulationRuntimeContext context)
    {
    }
}

sealed class TestResurrectionExtension : ICombatSimulationRuntimeExtension
{
    public void Initialize(ICombatSimulationRuntimeContext context)
    {
    }

    public void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        if (sourceEvent.Kind != CombatSimulationEventKind.ActorDefeated)
        {
            return;
        }
        var target = context.State.FindActor(sourceEvent.TargetActorId);
        if (target?.Kind == CombatSimulationActorKind.Player)
        {
            target.Hp = Math.Min(target.MaxHp, 5);
        }
    }

    public void Complete(ICombatSimulationRuntimeContext context)
    {
    }
}

sealed class ReentrantDiscardExtension : ICombatSimulationRuntimeExtension
{
    private bool moved;

    public void Initialize(ICombatSimulationRuntimeContext context)
    {
    }

    public void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        if (moved
            || sourceEvent.Kind != CombatSimulationEventKind.CardDiscarded
            || context.State.Hand.Count == 0)
        {
            return;
        }
        var instanceId = context.State.Hand[0];
        context.State.Hand.RemoveAt(0);
        context.State.DrawPile.Add(instanceId);
        moved = true;
    }

    public void Complete(ICombatSimulationRuntimeContext context)
    {
    }
}

sealed class EndTurnSimulationPolicy : ICombatSimulationPolicy
{
    public static readonly EndTurnSimulationPolicy Instance = new();

    public string PolicyId => "tests:end-turn";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class PlayCardOnceThenEndPolicy : ICombatSimulationPolicy
{
    private readonly string cardId;
    private bool played;

    public PlayCardOnceThenEndPolicy(string cardId)
    {
        this.cardId = cardId;
    }

    public string PolicyId => "tests:play-once-then-end";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (!played)
        {
            var selected = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    cardId,
                    StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                played = true;
                return selected;
            }
        }
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
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

sealed class FixedRulesetProvider : ICombatRulesetProvider
{
    public void RegisterDefinitions(CombatRulesetBuilder builder)
    {
        var result = BuildDefinitions(builder);
        _ = result;
    }

    private static CombatRulesetBuilder BuildDefinitions(CombatRulesetBuilder builder)
    {
        builder.RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "Tests",
            StatusId = "training",
            DecayAtRoundEnd = false
        });
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "strike",
            Cost = 1,
            RequiresEnemyTarget = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Damage,
                    Target = CombatSimulationTarget.SelectedEnemy,
                    Amount = 6
                }
            }
        });
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "guard",
            Cost = 1,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.GainBlock,
                    Target = CombatSimulationTarget.Self,
                    Amount = 5
                }
            }
        });
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "insight",
            Cost = 0,
            Exhaust = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Draw,
                    Target = CombatSimulationTarget.Self,
                    Amount = 1
                }
            }
        });
        builder.RegisterEnemy(new CombatEnemyDefinition
        {
            OwnerModId = "Tests",
            EnemyId = "dummy",
            MaxHp = 18,
            Intents =
            {
                new CombatEnemyIntentDefinition
                {
                    IntentId = "hit",
                    Weight = 1,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.Damage,
                            Target = CombatSimulationTarget.Player,
                            Amount = 4
                        }
                    }
                }
            }
        });
        return builder;
    }
}

sealed class FixedThreatProvider : ICombatThreatProvider
{
    private readonly CombatThreatForecast forecast;

    public FixedThreatProvider(CombatThreatForecast forecast)
    {
        this.forecast = forecast;
    }

    public bool TryForecast(
        CombatStateObservation state,
        out CombatThreatForecast result)
    {
        result = forecast;
        return true;
    }
}

sealed class FixedScenarioProvider : ICombatScenarioProvider
{
    private readonly ulong seed;

    public FixedScenarioProvider(ulong seed)
    {
        this.seed = seed;
    }

    public IEnumerable<CombatScenarioDefinition> GetScenarios()
    {
        yield return new CombatScenarioDefinition
        {
            ScenarioId = "registered-headless",
            RulesetVersion = "registry-v1",
            Seed = seed,
            Player = new CombatPlayerSetup
            {
                RoleId = "tests",
                MaxHp = 20,
                CurrentHp = 20,
                Deck = new List<string> { "Tests:strike" }
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "Tests:dummy" }
            }
        };
    }
}

sealed class FixedEffectResolver : ICombatEffectResolver
{
    private readonly string candidateId;

    public FixedEffectResolver(string candidateId)
    {
        this.candidateId = candidateId;
    }

    public bool TryResolve(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionModel model)
    {
        model = new CombatActionModel();
        if (action.CandidateId != candidateId)
        {
            return false;
        }
        model.ModelId = "test-chance";
        model.Outcomes = new List<CombatActionOutcome>
        {
            new CombatActionOutcome
            {
                OutcomeId = "low",
                Probability = 2d,
                Effects =
                {
                    new CombatEffectOperation
                    {
                        Kind = CombatEffectKind.Damage,
                        TargetRuntimeId = action.TargetRuntimeId,
                        Magnitude = 2d
                    }
                }
            },
            new CombatActionOutcome
            {
                OutcomeId = "high",
                Probability = 2d,
                Effects =
                {
                    new CombatEffectOperation
                    {
                        Kind = CombatEffectKind.Damage,
                        TargetRuntimeId = action.TargetRuntimeId,
                        Magnitude = 6d
                    }
                }
            }
        };
        return true;
    }
}
