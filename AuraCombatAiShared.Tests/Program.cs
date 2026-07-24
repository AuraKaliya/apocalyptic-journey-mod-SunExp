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
Assert(poisonDecision.Action?.CandidateId == "poison-attack",
    "known damage-over-time threat does not make shield a survival action");

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
    FeatureSchemaVersion = 4,
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
Assert(trainingSample.ModelProtocol == "aura.combat-ai.sample.v4"
       && trainingSample.FeatureSchemaVersion == 4
       && trainingSample.Candidates.Count == state.Actions.Count
       && trainingSample.SourceId == "attack",
    "training v4 captures the selected action and every candidate");
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
    "training v4 captures terminal outcome reward");
Assert(trainingSample.Features["nonFinite"] == 0d,
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
Assert(noThreatDefendFeatures["usefulDefend"] == 0d
       && noThreatDefendFeatures["wastedDefend"] == 5d
       && noThreatDefendFeatures["semanticConfidence"] == 1d
       && neededDefendFeatures["usefulDefend"] > 0d,
    "context features distinguish useful defense from defense without a threat");

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
       && trained.Model?.FeatureSchemaVersion == 4
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
       && !double.IsNaN(guidanceTraining.Model.Value.Bias),
    "search guidance trainer produces bounded policy and value tree ensembles");
var guidanceModel = new BoundedTreeCombatSearchGuidanceModel(guidanceTraining.Model!);
Assert(!double.IsNaN(guidanceModel.PolicyLogit(originalFeatures))
       && guidanceModel.DeathRisk(humanSample.StateFeatures) >= 0d
       && guidanceModel.DeathRisk(humanSample.StateFeatures) <= 1d,
    "tree search guidance inference stays finite and bounds death risk");
var legacyContext = CombatResidualTrainer.ContextualFeatures(
    new CombatTrainingSample
    {
        ModelProtocol = "aura.combat-ai.sample.v3",
        FeatureSchemaVersion = 3,
        StateFeatures = new Dictionary<string, double>
        {
            ["playerHp"] = 20d,
            ["playerMaxHp"] = 30d,
            ["playerDefend"] = 0d,
            ["expectedBlockableDamage"] = 0d,
            ["power"] = 2d,
            ["maxPower"] = 3d,
            ["handCount"] = 2d
        }
    },
    new CombatTrainingCandidate
    {
        ActionKind = "PlayCard",
        Semantics = new CombatActionSemantics { Defend = 6d }
    });
Assert(legacyContext["wastedDefend"] == 6d
       && legacyContext["semanticConfidence"] == 1d
       && legacyContext["categoryDefend"] == 1d,
    "v3 samples are reconstructed into v4 contextual features inside the MOD trainer");

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

var coverageProfile = new CombatDecisionProfile
{
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
Assert(coverageDecision.SearchAlgorithm == "chance-puct"
       && coverageDecision.SearchSimulations >= 2
       && coverageDecision.Candidates
           .Where(candidate => candidate.Action.Kind != CombatActionKind.EndTurn)
           .All(candidate => candidate.PlanScore != 0d),
    "chance-puct gives every legal root action search evidence");

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
    new CombatDecisionProfile { SearchSimulationBudget = 128, SearchNodeBudget = 512 });
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
        SearchSimulationBudget = 256,
        SearchNodeBudget = 1024,
        SearchMaxPly = 4
    });
Assert(transpositionDecision.SearchTranspositionHits > 0,
    "commutative action orders reuse a physical-state transposition node");

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
