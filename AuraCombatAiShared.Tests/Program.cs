using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;

var assertions = 0;

var gameSubjectCatalog = new CombatGameSubjectCatalog
{
    Roles =
    {
        new CombatGameSubjectRole
        {
            Id = "career_test",
            DisplayName = "Test Role",
            SkillCardIds = { "skill_test" },
            SkillCooldownTurns = { ["skill_test"] = 3 },
            InitialSkillCooldownTurns = { ["skill_test"] = 2 },
            MaximumHp = 60,
            InitialVariables = { ["DoomPower"] = 0d },
            InitialStatuses = { ["status_test"] = 2 },
            NativeScriptHash = "native-role-hash",
            FightScript = "AddEvent(\"StartRound\", () => { });",
            NativeManagedSkillCooldownIds = { "skill_test" },
            TransformRoleIds = { "career_test_form" }
        },
        new CombatGameSubjectRole
        {
            Id = "career_test_form",
            DisplayName = "Test Role Form",
            SkillCardIds = { "skill_test" },
            SkillCooldownTurns = { ["skill_test"] = 1 },
            MaximumHp = 40
        }
    },
    Familiars =
    {
        new CombatGameSubjectFamiliar
        {
            Id = "partner_test",
            DisplayName = "Test Familiar",
            BlessingIds = { "blessing_test" }
        }
    },
    CardPacks =
    {
        new CombatGameSubjectCardPack
        {
            Id = "cardpack_1",
            Required = true
        },
        new CombatGameSubjectCardPack
        {
            Id = "cardpack_2",
            Required = true
        },
        new CombatGameSubjectCardPack
        {
            Id = "cardpack_3"
        }
    }
}.Normalize();
var gameSubject = new CombatGameSubjectPreset
{
    Id = "test-subject",
    RoleId = "career_test",
    PartnerId = "partner_test",
    EnabledRewardCardPackIds = { "cardpack_3" },
    PreferredDeckSizeMinimum = 12,
    PreferredDeckSizeMaximum = 20
};
gameSubjectCatalog.ResolveReferences(gameSubject);
var gameSubjectCampaign = new CombatCampaignDefinition
{
    Player = new CombatPlayerSetup
    {
        Deck = { "strike", "guard" }
    }
};
CombatGameSubjectPresetRuntime.Apply(gameSubject, gameSubjectCampaign);
Assert(
    gameSubjectCampaign.Player.RoleId == "career_test"
    && gameSubjectCampaign.Player.PartnerId == "partner_test"
    && gameSubjectCampaign.Player.SkillCardIds.SequenceEqual(
        new[] { "skill_test" })
    && gameSubjectCampaign.Player.SkillCooldownTurns["skill_test"] == 3
    && gameSubjectCampaign.Player.InitialSkillCooldownTurns["skill_test"] == 2
    && gameSubjectCampaign.Player.MaxHp == 60
    && gameSubjectCampaign.Player.CurrentHp == 60
    && gameSubjectCampaign.Player.Variables["DoomPower"] == 0d
    && gameSubjectCampaign.Player.InitialStatuses.Count == 0
    && gameSubject.ResolvedRoleInitialStatuses["status_test"] == 2
    && gameSubjectCampaign.Player.RoleNativeScriptHash == "native-role-hash"
    && gameSubjectCampaign.Player.NativeManagedSkillCooldownIds.SequenceEqual(
        new[] { "skill_test" })
    && gameSubjectCampaign.Player.RoleRuntimeForms.Count == 2
    && gameSubjectCampaign.Player.RoleRuntimeForms.Any(item =>
        item.RoleId == "career_test")
    && gameSubjectCampaign.Player.RoleRuntimeForms.Single(item =>
        item.RoleId == "career_test_form")
       .SkillCooldownTurns["skill_test"] == 1
    && gameSubjectCampaign.Player.FamiliarBlessingIds.SequenceEqual(
        new[] { "blessing_test" })
    && gameSubjectCampaign.EnabledRewardCardPackIds.SequenceEqual(
        new[] { "cardpack_1", "cardpack_2", "cardpack_3" })
    && gameSubjectCampaign.Player.GameParameterHash
       == CombatGameSubjectPresetRuntime.ComputeHash(
           gameSubject,
           gameSubjectCampaign.Player.Deck),
    "game subject preset resolves and applies one immutable campaign snapshot");

var lowValueTransformState = BuildHandTransformFixture(valuableHand: false);
var lowValueTransformAction = lowValueTransformState.Actions[0];
CombatHandTransformPolicy.Enrich(
    lowValueTransformState,
    lowValueTransformAction);
var lowValueTransform = CombatHandTransformPolicy.Assess(
    lowValueTransformState,
    lowValueTransformAction);
var highValueTransformState = BuildHandTransformFixture(valuableHand: true);
var highValueTransformAction = highValueTransformState.Actions[0];
CombatHandTransformPolicy.Enrich(
    highValueTransformState,
    highValueTransformAction);
var highValueTransform = CombatHandTransformPolicy.Assess(
    highValueTransformState,
    highValueTransformAction);
Assert(
    lowValueTransform.CleanupValue > 0d
    && highValueTransform.EngineLoss > lowValueTransform.EngineLoss
    && highValueTransform.EnhancementLoss > 0d
    && lowValueTransform.NetValue > highValueTransform.NetValue
    && lowValueTransform.DepletionRisk > 0d,
    "whole-hand transform values cleanup, engine sacrifice, enhancements and depletion separately");
var distributedLethalState = BuildHandTransformFixture(valuableHand: false);
distributedLethalState.HandCount = 2;
distributedLethalState.HandCardIds.RemoveAt(2);
distributedLethalState.HandCards.RemoveAt(2);
distributedLethalState.Enemies.Clear();
distributedLethalState.Enemies.AddRange(new[]
{
    new CombatUnitObservation
    {
        RuntimeId = 2,
        Kind = CombatTargetKind.Enemy,
        CurrentHp = 100,
        MaxHp = 100
    },
    new CombatUnitObservation
    {
        RuntimeId = 3,
        Kind = CombatTargetKind.Enemy,
        CurrentHp = 1,
        MaxHp = 1
    }
});
distributedLethalState.Actions[0].Semantics.HandTransform!
    .TargetCardSemantics.Damage = 30d;
distributedLethalState.Actions[0].Semantics.HandTransform!
    .TargetCardSemantics.AffectedEnemyCount = 2;
Assert(
    !CombatHandTransformPolicy.Assess(
        distributedLethalState,
        distributedLethalState.Actions[0]).LethalCertified,
    "whole-hand transform lethal certification is per enemy rather than aggregate damage");
var transformSimulation = CombatForwardModel.Create(
    lowValueTransformState,
    lowValueTransformState.Actions.Count);
var transformOutcome = CombatForwardModel.Resolve(
        lowValueTransformState,
        lowValueTransformAction,
        useRegisteredResolvers: false)
    .Outcomes.Single();
var transformedSimulation = CombatForwardModel.Apply(
    transformSimulation,
    lowValueTransformAction,
    0,
    transformOutcome,
    new CombatDecisionProfile());
Assert(
    transformedSimulation.HandCount == lowValueTransformState.HandCount
    && transformedSimulation.HandCardIds.Count
       == lowValueTransformState.HandCount
    && transformedSimulation.HandCardIds.All(id => id == "nocard_1")
    && transformedSimulation.RetainedHandCardIds.Count
       == lowValueTransformState.HandCount,
    "forward model replaces every hand instance and preserves transformed retain semantics");
var selfGrowthRoot = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 1,
        Kind = CombatTargetKind.Self,
        CurrentHp = 50,
        MaxHp = 100
    },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 2,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 100,
            MaxHp = 100
        }
    }
};
var selfGrowthAction = new CombatActionObservation
{
    CandidateId = "self-growth",
    SourceId = "self-growth",
    TargetRuntimeId = 2,
    TargetKind = CombatTargetKind.Enemy,
    Semantics = new CombatActionSemantics
    {
        Damage = 5d,
        Heal = 12d,
        StateChanges =
        {
            ["playerMaxHp"] = 12d,
            ["player.hp"] = 12d
        },
        TargetEffects =
        {
            new CombatTargetedSemanticEffect
            {
                Kind = CombatSemanticEffectKind.Heal,
                TargetRuntimeId = 1,
                RawAmount = 12d,
                EffectiveAmount = 12d
            }
        }
    }
};
var selfGrowthOutcome = CombatForwardModel.Resolve(
        selfGrowthRoot,
        selfGrowthAction,
        useRegisteredResolvers: false)
    .Outcomes.Single();
var selfGrowthSimulation = CombatForwardModel.Apply(
    CombatForwardModel.Create(selfGrowthRoot, 1),
    selfGrowthAction,
    0,
    selfGrowthOutcome,
    new CombatDecisionProfile());
Assert(selfGrowthSimulation.PlayerMaxHp == 112
       && selfGrowthSimulation.PlayerHp == 62
       && selfGrowthSimulation.Enemies.Single().Hp == 95,
    "forward model applies maximum-health growth before its paired self-heal without healing the Devour target");

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
Assert(selection.TryIssueConfirm(2, 0.3d) && !selection.TryIssueConfirm(2, 0.3d),
    "prompt confirmation can only be issued once");
Assert(selection.Observe(2, 0.4d)
       == CombatSelectionProgress.AwaitingNativeClose
       && selection.Observe(2, 2.31d)
       == CombatSelectionProgress.TimedOut,
    "confirmed prompts wait for native closure without selecting again and fail on a bounded close timeout");

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
var cycleLeafFeatures = CombatSearchFeatureProjector.ProjectLeaf(
    new CombatSimulationState
    {
        PlayerHp = 20,
        PlayerMaxHp = 20,
        HandCount = 4,
        HandLimit = 5,
        HandCardValues = { 1d, 2d, 3d, 4d },
        RetainedHandCardValues = { 3d, 4d },
        DrawPileValues = { 5d },
        DiscardPileValues = { 6d, 7d, 8d },
        Features = { ["drawPerTurn"] = 4d }
    },
    new CombatDecisionProfile());
Assert(cycleLeafFeatures["recyclableCardCount"] == 6d
       && cycleLeafFeatures["unretainedHandCount"] == 2d
       && cycleLeafFeatures["lockedHandCount"] == 2d
       && cycleLeafFeatures["availableHandSlots"] == 1d
       && cycleLeafFeatures["effectiveNextDraw"] == 3d
       && cycleLeafFeatures["drawPileShortfall"] == 2d
       && cycleLeafFeatures["reshuffleWithinNextDraw"] == 1d
       && cycleLeafFeatures["cycleAccessRate"] == 0.5d,
    "cycle features separate locked hand slots from cards that can return through the discard pile");
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
Assert(trainingSample.ModelProtocol == "aura.combat-ai.sample.v7"
       && trainingSample.FeatureSchemaVersion == 10
       && trainingSample.Candidates.Count == state.Actions.Count
       && trainingSample.Candidates.Single(candidate =>
           candidate.CandidateId == "attack").SourceId == "attack",
    "training sample captures the selected action and every candidate");
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
    "training sample captures terminal outcome reward");
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
       && trained.Model?.FeatureSchemaVersion == 10
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
var setEnergyRoot = new CombatStateObservation
{
    Player = new CombatUnitObservation { RuntimeId = 71, CurrentHp = 20, MaxHp = 20 },
    CurrentPower = 1,
    MaxPower = 4
};
var setEnergyAction = new CombatActionObservation
{
    CandidateId = "restore-energy",
    SourceId = "timekeeper_2",
    Kind = CombatActionKind.PlayCard,
    Semantics = new CombatActionSemantics { RestoreEnergyToMaximum = true }
};
var setEnergyState = CombatForwardModel.Create(setEnergyRoot, 1);
setEnergyState = CombatForwardModel.Apply(
    setEnergyState,
    setEnergyAction,
    0,
    CombatForwardModel.Resolve(setEnergyRoot, setEnergyAction).Outcomes[0],
    new CombatDecisionProfile());
Assert(setEnergyState.Power == 4,
    "authoritative set-energy semantics restore the simulated resource to its maximum");

var forwardRetrievalRoot = new CombatStateObservation
{
    Player = new CombatUnitObservation { RuntimeId = 72, CurrentHp = 20, MaxHp = 20 },
    HandCount = 1,
    HandCardIds = { "timekeeper_18" },
    CardTagsById =
    {
        ["timekeeper_18"] = new List<string> { "Froze" }
    }
};
var forwardRetrievalAction = new CombatActionObservation
{
    CandidateId = "retrieve-self",
    SourceId = "timekeeper_18",
    Kind = CombatActionKind.PlayCard,
    Features = { ["cardBaseCost"] = 0d },
    Semantics = new CombatActionSemantics
    {
        CardRetrievals =
        {
            new CombatCardRetrievalSemantic
            {
                SourceZone = CombatCardZoneKind.DiscardPile,
                DestinationZone = CombatCardZoneKind.Hand,
                Amount = 1,
                RequiredCardTag = "Froze"
            }
        }
    }
};
var forwardRetrievalState = CombatForwardModel.Create(forwardRetrievalRoot, 1);
forwardRetrievalState = CombatForwardModel.Apply(
    forwardRetrievalState,
    forwardRetrievalAction,
    0,
    CombatForwardModel.Resolve(
        forwardRetrievalRoot,
        forwardRetrievalAction).Outcomes[0],
    new CombatDecisionProfile());
Assert(forwardRetrievalState.HandCardIds.Contains("timekeeper_18")
       && forwardRetrievalState.HandCount == 1
       && forwardRetrievalState.UseCount(0) == 0,
    "generic tagged retrieval moves the real card between zones and re-enables a retrieved card for loop analysis");

var chaosRoot = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 73,
        CurrentHp = 20,
        MaxHp = 20,
        Statuses =
        {
            new CombatStatusObservation { StatusId = "buff_chaos", DisplayName = "混乱" }
        }
    },
    CurrentPower = 4,
    MaxPower = 4,
    HandCount = 2,
    Features = { ["cardCostMultiplier"] = 1d }
};
var chaosAction = new CombatActionObservation
{
    CandidateId = "chaos-card-a",
    SourceId = "chaos-card-a",
    Kind = CombatActionKind.PlayCard,
    Cost = 2,
    Features =
    {
        ["cardBaseCost"] = 2d,
        ["cardCostCap"] = 4d
    }
};
var chaosFollowUp = new CombatActionObservation
{
    CandidateId = "chaos-card-b",
    SourceId = "chaos-card-b",
    Kind = CombatActionKind.PlayCard,
    Cost = 2,
    Features =
    {
        ["cardBaseCost"] = 2d,
        ["cardCostCap"] = 4d
    }
};
var chaosModel = CombatForwardModel.Resolve(chaosRoot, chaosAction);
var chaosHashes = new List<ulong>();
var chaosCosts = chaosModel.Outcomes
    .Select(outcome =>
    {
        var branch = CombatForwardModel.Apply(
            CombatForwardModel.Create(chaosRoot, 2),
            chaosAction,
            0,
            outcome,
            new CombatDecisionProfile());
        chaosHashes.Add(branch.Hash());
        return CombatForwardModel.EffectiveCost(branch, chaosFollowUp);
    })
    .OrderBy(value => value)
    .ToArray();
Assert(chaosModel.Outcomes.Count == 3
       && chaosCosts.SequenceEqual(new[] { 0, 2, 4 })
       && chaosHashes.Distinct().Count() == 3,
    "chaos branches the post-action card-cost multiplier and recomputes later card costs in the search tree");
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
var shieldControlLoopEnd = limitedLoopEnd.Clone();
shieldControlLoopEnd.Enemies[0].Hp = 20;
shieldControlLoopEnd.PlayerDefend = 12;
var stackingControlLoopEnd = limitedLoopEnd.Clone();
stackingControlLoopEnd.Enemies[0].Hp = 20;
stackingControlLoopEnd.Features["status:buff_elements"] = 5d;
var drainingResourceLoopStart = loopStart.Clone();
drainingResourceLoopStart.Features["status:buff_revelation"] = 2d;
var drainingResourceLoopEnd = drainingResourceLoopStart.Clone();
drainingResourceLoopEnd.Features["status:buff_revelation"] = 1d;
var monotonicCycleStart = loopStart.Clone();
var monotonicCycleEnd = monotonicCycleStart.Clone();
monotonicCycleEnd.PlayerDefend = 25;
monotonicCycleEnd.SetupValue = 3d;
monotonicCycleEnd.Features["status:buff_elements"] = 7d;
monotonicCycleEnd.Power = 8;
monotonicCycleEnd.TurnActionsTaken = 12;
monotonicCycleEnd.TurnEnergySpent = 9;
var drainingEnergyLoopEnd = loopStart.Clone();
drainingEnergyLoopEnd.Power = 2;
var growingEnergyLoopEnd = loopStart.Clone();
growingEnergyLoopEnd.Power = 6;
var growingEnergyAssessment = CombatLoopSafetyAnalyzer.Analyze(
    loopStart,
    growingEnergyLoopEnd,
    new CombatDecisionProfile());
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
       == CombatLoopClassification.SustainableControl
       && CombatLoopSafetyAnalyzer.Analyze(
           loopStart,
           shieldControlLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.SustainableControl
       && CombatLoopSafetyAnalyzer.Analyze(
           loopStart,
           stackingControlLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.SustainableControl
       && CombatLoopSafetyAnalyzer.Analyze(
           drainingResourceLoopStart,
           drainingResourceLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.Fake
       && CombatLoopSafetyAnalyzer.Analyze(
           loopStart,
           drainingEnergyLoopEnd,
           new CombatDecisionProfile()).Classification
       == CombatLoopClassification.Fake
       && growingEnergyAssessment.Classification
       == CombatLoopClassification.SustainableControl
       && growingEnergyAssessment.EnergyDelta == 3
       && monotonicCycleStart.CycleHash() == monotonicCycleEnd.CycleHash(),
    "structural loop safety ignores growing energy and counters, permits energy growth, and rejects finite energy loss");

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
var guardedEndTurn = coverageDecision.Candidates.Single(candidate =>
    candidate.Action.Kind == CombatActionKind.EndTurn);
Assert(coverageDecision.Action?.Kind != CombatActionKind.EndTurn
       && !guardedEndTurn.Legal
       && guardedEndTurn.Action.Features.TryGetValue(
           CombatTurnFeatureNames.EndTurnSevereMistake,
           out var severeEndTurn)
       && severeEndTurn == 1d
       && guardedEndTurn.SearchVisits == 0,
    "end-turn safety makes passing with playable cards and unused energy a non-searchable severe mistake");
var deliberateEndTurnState = new CombatStateObservation
{
    CurrentPower = 1,
    Features =
    {
        [CombatTurnFeatureNames.EndTurnPurposeValue] = 5d
    }
};
var deliberateEndTurnAssessment = CombatEndTurnSafety.Assess(
    deliberateEndTurnState,
    coverageDecision.Candidates,
    coverageProfile);
Assert(deliberateEndTurnAssessment.HasDeliberatePurpose
       && deliberateEndTurnAssessment.Prohibited,
    "explicit end-of-round purpose cannot override a productive executable action");
var saturatedCandidates = new List<CombatCandidateEvaluation>
{
    new()
    {
        Legal = true,
        Action = new CombatActionObservation
        {
            CandidateId = "saturated-defense",
            Kind = CombatActionKind.PlayCard,
            Cost = 1,
            Semantics = new CombatActionSemantics { Defend = 5d },
            Features =
            {
                ["immediateDefend"] = 0d,
                ["effectiveDurabilityDamage"] = 0d,
                ["effectiveHeal"] = 0d,
                ["effectiveDraw"] = 0d,
                ["marginalSetupValue"] = 0d
            }
        }
    }
};
var saturatedEndTurnAssessment = CombatEndTurnSafety.Assess(
    deliberateEndTurnState,
    saturatedCandidates,
    coverageProfile);
Assert(!saturatedEndTurnAssessment.Prohibited
       && saturatedEndTurnAssessment.UnusedEnergy == 1
       && saturatedEndTurnAssessment.AvoidableUnusedEnergy == 0,
    "unused energy is rational when every recognized action is saturated");
var unknownPlayable = new CombatCandidateEvaluation
{
    Legal = true,
    Action = new CombatActionObservation
    {
        CandidateId = "unknown-playable",
        Kind = CombatActionKind.PlayCard,
        Cost = 1
    }
};
Assert(CombatActionProductivity.Assess(
           deliberateEndTurnState,
           unknownPlayable).Productive,
    "unknown playable non-curse cards conservatively outrank end turn");
unknownPlayable.Action.Features["curse"] = 1d;
Assert(!CombatActionProductivity.Assess(
        deliberateEndTurnState,
        unknownPlayable).Productive,
    "curse cards are excluded from the end-turn productivity gate");

Assert(CombatTurnTransitionRules.NextTurnPower(2, 3) == 3
       && CombatTurnTransitionRules.NextTurnPower(5, 3) == 5
       && Math.Abs(
           CombatTurnTransitionRules.EnergyCarryOpportunityCost(
               3,
               3,
               2,
               0)) < 0.000001d
       && Math.Abs(
           CombatTurnTransitionRules.EnergyCarryOpportunityCost(
               5,
               3,
               2,
               0) - 2d) < 0.000001d,
    "shared turn rules refill energy at or below the cap and price only banked surplus above it");
var bankedPowerState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 910,
        CurrentHp = 20,
        MaxHp = 20
    },
    CurrentPower = 5,
    MaxPower = 3,
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 911,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 20,
            MaxHp = 20
        }
    }
};
var lowYieldSurplusAction = new CombatCandidateEvaluation
{
    Legal = true,
    Action = new CombatActionObservation
    {
        CandidateId = "surplus-low-yield",
        Kind = CombatActionKind.PlayCard,
        Cost = 1,
        TargetRuntimeId = 911,
        Semantics = new CombatActionSemantics { Damage = 1d },
        Features = { ["effectiveDurabilityDamage"] = 1d }
    }
};
var highYieldSurplusAction = new CombatCandidateEvaluation
{
    Legal = true,
    Action = new CombatActionObservation
    {
        CandidateId = "surplus-high-yield",
        Kind = CombatActionKind.PlayCard,
        Cost = 1,
        TargetRuntimeId = 911,
        Semantics = new CombatActionSemantics { Damage = 2d },
        Features = { ["effectiveDurabilityDamage"] = 2d }
    }
};
Assert(!CombatActionProductivity.Assess(
           bankedPowerState,
           lowYieldSurplusAction).Productive
       && CombatActionProductivity.Assess(
           bankedPowerState,
           highYieldSurplusAction).Productive,
    "end-turn productivity compares action value with the next-turn cost of spending banked surplus energy");
var lethalProjectionState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 912,
        CurrentHp = 4,
        MaxHp = 20
    },
    CurrentPower = 1,
    MaxPower = 1,
    HandCount = 2,
    HandCardIds = { "guard", "keep" },
    RetainedHandCardIds = { "keep" },
    DeckKnowledge = new CombatDeckKnowledge
    {
        DrawPileCount = 0,
        DiscardPileCount = 2
    },
    Threat = new CombatThreatForecast
    {
        ExpectedBlockableDamage = 5d,
        CurrentIntentKnown = true,
        Confidence = 1d
    },
    Features =
    {
        ["handLimit"] = 10d,
        ["drawPerTurn"] = 5d,
        [CombatTurnFeatureNames.UnknownLifecycleEffectCount] = 1d
    }
};
var lifeSavingGuard = new CombatCandidateEvaluation
{
    Legal = true,
    Action = new CombatActionObservation
    {
        CandidateId = "life-saving-guard",
        Kind = CombatActionKind.PlayCard,
        Cost = 1,
        Semantics = new CombatActionSemantics { Defend = 5d },
        Features = { ["immediateDefend"] = 5d }
    }
};
var lethalEndTurnAssessment = CombatEndTurnSafety.Assess(
    lethalProjectionState,
    new[] { lifeSavingGuard },
    new CombatDecisionProfile());
Assert(lethalEndTurnAssessment.Prohibited
       && lethalEndTurnAssessment.AvoidableLethal
       && lethalEndTurnAssessment.Verdict
       == CombatEndTurnVerdict.BlockedLethal
       && lethalEndTurnAssessment.Projection.UnretainedHandCount == 1
       && lethalEndTurnAssessment.Projection.RetainedHandCount == 1
       && lethalEndTurnAssessment.Projection.ReshuffleDuringNextDraw
       && lethalEndTurnAssessment.Projection.UnretainedReturnDelayTurns == 0,
    "end-turn projection combines retain/discard/reshuffle rules with avoidable lethal intent");
var certifiedCycleAction = new CombatCandidateEvaluation
{
    Legal = true,
    Action = new CombatActionObservation
    {
        CandidateId = "certified-cycle-connector",
        Kind = CombatActionKind.PlayCard,
        Cost = 0,
        Features =
        {
            ["strategyInfinite"] = 1d,
            ["strategyExecutable"] = 1d,
            ["strategyDeterministic"] = 1d
        }
    }
};
var certifiedCycleAssessment = CombatEndTurnSafety.Assess(
    new CombatStateObservation
    {
        Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 },
        CurrentPower = 3,
        MaxPower = 3
    },
    new[] { certifiedCycleAction },
    new CombatDecisionProfile());
Assert(certifiedCycleAssessment.Prohibited
       && certifiedCycleAssessment.Verdict
       == CombatEndTurnVerdict.BlockedCycle
       && certifiedCycleAssessment.CertifiedCycleCount == 1,
    "deterministic executable infinite components block end turn even when the connector has no immediate numeric payoff");

var terminalCardState = new CombatStateObservation
{
    Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 },
    CurrentPower = 2,
    MaxPower = 2,
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 701,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 20,
            MaxHp = 20
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "terminal-card",
            SourceId = "terminal-card",
            Kind = CombatActionKind.PlayCard,
            Cost = 1,
            Semantics = new CombatActionSemantics
            {
                Damage = 3d,
                EndsTurn = true
            }
        },
        new CombatActionObservation
        {
            CandidateId = "continue-attack",
            SourceId = "continue-attack",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 701,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics { Damage = 4d }
        },
        new CombatActionObservation
        {
            CandidateId = "terminal-end",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var terminalCardDecision = new CombatDecisionEngine(
        useRuntimeRegistries: false)
    .Choose(terminalCardState, new CombatDecisionProfile());
var terminalCardEvaluation = terminalCardDecision.Candidates.Single(item =>
    item.Action.CandidateId == "terminal-card");
Assert(terminalCardDecision.Action?.CandidateId == "continue-attack"
       && !terminalCardEvaluation.Legal
       && terminalCardEvaluation.Action.Features.TryGetValue(
           CombatTurnFeatureNames.EndTurnDominated,
           out var terminalDominated)
       && terminalDominated == 1d,
    "cards that end the turn pass through the same continuation safety gate as the end-turn button");

var damageToBlockState = new CombatStateObservation
{
    Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 },
    CurrentPower = 2,
    MaxPower = 2,
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 702,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 20,
            MaxHp = 20
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "damage-to-block-setup",
            SourceId = "damage-to-block-setup",
            Kind = CombatActionKind.PlayCard,
            Cost = 1,
            Semantics = new CombatActionSemantics
            {
                DamageToBlockSetup = true,
                Buff = 1d
            }
        },
        new CombatActionObservation
        {
            CandidateId = "damage-payoff",
            SourceId = "damage-payoff",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 702,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics { Damage = 5d }
        },
        new CombatActionObservation
        {
            CandidateId = "damage-to-block-end",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var damageToBlockDecision = new CombatDecisionEngine(
        useRuntimeRegistries: false)
    .Choose(damageToBlockState, new CombatDecisionProfile());
Assert(damageToBlockDecision.Action?.CandidateId == "damage-to-block-setup"
       && damageToBlockDecision.SearchAlgorithm == "dominance",
    "damage-recording block setup strictly precedes an affordable damage payoff without card-name rules");

var crossDefinitionPackage = new CombatKnowledgePackage
{
    OwnerId = "tests",
    PackageId = "cross-definition-semantics",
    GameBuild = "test",
    SourceHash = "test-hash",
    Actions =
    {
        new CombatKnowledgeActionDefinition
        {
            SourceId = "setup-from-status",
            Fidelity = CombatKnowledgeFidelity.Authoritative,
            Semantics = new CombatActionSemantics
            {
                StateChanges = { ["status:damage-recorder"] = 1d }
            },
            TableFields = { ["UseScript"] = "ChangeRound();" }
        }
    },
    Statuses =
    {
        new CombatKnowledgeStatusDefinition
        {
            StatusId = "damage-recorder",
            Fidelity = CombatKnowledgeFidelity.Authoritative,
            Triggers = { "Hurt", "EndRound" },
            Operations =
            {
                new CombatKnowledgeOperation { Api = "ChangeDefence" }
            }
        }
    }
};
using (CombatKnowledgeRegistry.RegisterPackage(
           crossDefinitionPackage,
           out var crossDefinitionErrors))
{
    var foundCrossDefinition = CombatKnowledgeRegistry.TryDescribeAction(
        new CombatActionObservation { SourceId = "setup-from-status" },
        out var crossDefinitionSemantics,
        out _,
        out _);
    Assert(crossDefinitionErrors.Count == 0
           && foundCrossDefinition
           && crossDefinitionSemantics.EndsTurn
           && crossDefinitionSemantics.DamageToBlockSetup,
        "knowledge registration derives terminal and damage-to-block setup semantics from scripts and status lifecycle definitions");
}

var limitedDamageState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 920,
        CurrentHp = 20,
        MaxHp = 20
    },
    CurrentPower = 1,
    MaxPower = 1,
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 921,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 10,
            MaxHp = 10,
            Defend = 3,
            Features =
            {
                [CombatDamageLimitPolicy.ActiveFeature] = 1d,
                [CombatDamageLimitPolicy.RemainingFeature] = 2d
            }
        }
    }
};
var limitedAttack = new CombatActionObservation
{
    CandidateId = "limited-attack",
    Kind = CombatActionKind.PlayCard,
    RuntimeId = 922,
    TargetRuntimeId = 921,
    TargetKind = CombatTargetKind.Enemy,
    Cost = 1,
    Semantics = new CombatActionSemantics
    {
        Damage = 5d,
        TrueDamage = 4d
    }
};
var limitedProjection = CombatDamageLimitPolicy.Project(
    limitedDamageState,
    limitedAttack);
var limitedForward = CombatForwardModel.Apply(
    CombatForwardModel.Create(limitedDamageState, 1),
    limitedAttack,
    0,
    CombatForwardModel.Resolve(
        limitedDamageState,
        limitedAttack).Outcomes[0],
    new CombatDecisionProfile());
Assert(limitedProjection.BlockDamage == 3d
       && limitedProjection.HpDamage == 2d
       && limitedProjection.DurabilityDamage == 5d
       && limitedForward.Enemies[0].Defend == 0
       && limitedForward.Enemies[0].Hp == 8
       && limitedForward.Enemies[0].Features[
           CombatDamageLimitPolicy.RemainingFeature] == 0d,
    "damage projection and forward simulation consume the enemy's remaining per-turn hp-damage budget");
limitedDamageState.Enemies[0].Defend = 0;
limitedDamageState.Enemies[0].Features[
    CombatDamageLimitPolicy.RemainingFeature] = 0d;
limitedAttack.Features = CombatDecisionEngine.BuildFeatures(
    limitedDamageState,
    limitedAttack);
var exhaustedAttackEvaluation = new CombatCandidateEvaluation
{
    Action = limitedAttack,
    Legal = true
};
Assert(!CombatActionProductivity.Assess(
        limitedDamageState,
        exhaustedAttackEvaluation).Productive,
    "pure attacks stop blocking end turn once the enemy damage budget is exhausted");
limitedAttack.Semantics.Draw = 1d;
limitedAttack.Features = CombatDecisionEngine.BuildFeatures(
    limitedDamageState,
    limitedAttack);
Assert(CombatActionProductivity.Assess(
        limitedDamageState,
        exhaustedAttackEvaluation).Productive,
    "an attack with an independent useful side effect remains productive after damage is capped");

var noEffectBefore = CombatPlayerObservationBoundary.Normalize(
    limitedDamageState);
var noEffectAfter = CombatPlayerObservationBoundary.Normalize(
    new CombatStateObservation
    {
        BattleSessionId = noEffectBefore.BattleSessionId,
        Player = new CombatUnitObservation
        {
            RuntimeId = noEffectBefore.Player.RuntimeId,
            CurrentHp = noEffectBefore.Player.CurrentHp,
            MaxHp = noEffectBefore.Player.MaxHp,
            Defend = noEffectBefore.Player.Defend
        },
        CurrentPower = noEffectBefore.CurrentPower,
        MaxPower = noEffectBefore.MaxPower,
        HandCount = noEffectBefore.HandCount,
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 921,
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 10,
                MaxHp = 10,
                Features =
                {
                    [CombatDamageLimitPolicy.ActiveFeature] = 1d,
                    [CombatDamageLimitPolicy.RemainingFeature] = 0d
                }
            }
        },
        Actions = { limitedAttack }
    });
noEffectBefore.Actions.Add(limitedAttack);
noEffectBefore.Features[
    CombatTurnFeatureNames.ActionsTakenThisTurn] = 0d;
noEffectAfter.Features[
    CombatTurnFeatureNames.ActionsTakenThisTurn] = 1d;
Assert(!CombatActionSettlementPolicy.HasMeaningfulProgress(
        noEffectBefore,
        noEffectAfter,
        limitedAttack,
        out _),
    "transaction bookkeeping alone cannot make a no-effect game action settle successfully");

var divineChoice = new CombatActionObservation
{
    CandidateId = "skill:careercard_1:1:1",
    SourceId = CombatActionExecutionPolicy.DivineChoiceSourceId,
    Kind = CombatActionKind.UseSkill,
    RuntimeId = 1,
    TargetRuntimeId = 1,
    TargetKind = CombatTargetKind.Self,
    Legal = true
};
CombatStateObservation DivineChoiceState(
    int drawPileCount,
    int discardPileCount,
    int handCount,
    long battleSessionId = 77)
{
    var result = new CombatStateObservation
    {
        BattleSessionId = battleSessionId,
        Sequence = 1,
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            Kind = CombatTargetKind.Self,
            CurrentHp = 20,
            MaxHp = 20
        },
        HandCount = handCount,
        CurrentPower = 3,
        MaxPower = 3,
        IsPlayerActionWindow = true,
        DeckKnowledge = new CombatDeckKnowledge
        {
            DrawPileCount = drawPileCount,
            DiscardPileCount = discardPileCount
        },
        Actions = { divineChoice }
    };
    for (var index = 0; index < handCount; index++)
    {
        result.HandCardIds.Add("hand-" + index);
    }
    return CombatPlayerObservationBoundary.Normalize(result);
}

var emptyDivineChoiceState = DivineChoiceState(0, 5, 3);
var fullHandDivineChoiceState = DivineChoiceState(3, 0, 10);
var legalDivineChoiceState = DivineChoiceState(3, 0, 9);
var expandedHandDivineChoiceState = DivineChoiceState(3, 0, 10);
expandedHandDivineChoiceState.Features["handLimit"] = 99;
Assert(!CombatActionExecutionPolicy.IsLiveEligible(
           emptyDivineChoiceState,
           emptyDivineChoiceState.Actions[0],
           out var emptyDivineChoiceReason)
       && emptyDivineChoiceReason.Contains("draw pile", StringComparison.Ordinal)
       && !CombatActionExecutionPolicy.IsLiveEligible(
           fullHandDivineChoiceState,
           fullHandDivineChoiceState.Actions[0],
           out var fullHandDivineChoiceReason)
       && fullHandDivineChoiceReason.Contains("hand slot", StringComparison.Ordinal)
       && CombatActionExecutionPolicy.IsLiveEligible(
           legalDivineChoiceState,
           legalDivineChoiceState.Actions[0],
           out _)
       && CombatActionExecutionPolicy.IsLiveEligible(
           expandedHandDivineChoiceState,
           expandedHandDivineChoiceState.Actions[0],
           out _),
    "divine choice live eligibility requires a draw-pile card and the runtime-configured free hand slot without counting discard cards");

var unrelatedDivineChoiceAfter = DivineChoiceState(3, 0, 9);
unrelatedDivineChoiceAfter.Enemies.Add(new CombatUnitObservation
{
    RuntimeId = 2,
    Kind = CombatTargetKind.Enemy,
    CurrentHp = 8,
    MaxHp = 10
});
var settledDivineChoiceAfter = DivineChoiceState(2, 0, 10);
settledDivineChoiceAfter.HandCardIds[9] = "chosen-card";
Assert(!CombatActionSettlementPolicy.HasMeaningfulProgress(
           legalDivineChoiceState,
           unrelatedDivineChoiceAfter,
           legalDivineChoiceState.Actions[0],
           out _)
       && CombatActionSettlementPolicy.HasMeaningfulProgress(
           legalDivineChoiceState,
           settledDivineChoiceAfter,
           legalDivineChoiceState.Actions[0],
           out var divineChoiceSettlementReason)
       && divineChoiceSettlementReason.Contains(
           "draw-pile card into hand",
           StringComparison.Ordinal),
    "divine choice settlement ignores unrelated state changes and requires the card transfer postcondition");

var sameEligibilityDivineChoiceState = DivineChoiceState(8, 2, 4);
sameEligibilityDivineChoiceState.Player.CurrentHp = 7;
var changedEligibilityDivineChoiceState = DivineChoiceState(0, 10, 4);
var divineSuppressionKey =
    CombatActionExecutionPolicy.BuildFailureSuppressionKey(
        legalDivineChoiceState,
        legalDivineChoiceState.Actions[0]);
Assert(divineSuppressionKey ==
       CombatActionExecutionPolicy.BuildFailureSuppressionKey(
           sameEligibilityDivineChoiceState,
           sameEligibilityDivineChoiceState.Actions[0])
       && divineSuppressionKey !=
       CombatActionExecutionPolicy.BuildFailureSuppressionKey(
           changedEligibilityDivineChoiceState,
           changedEligibilityDivineChoiceState.Actions[0]),
    "divine choice failure suppression follows only draw and hand eligibility changes instead of unrelated fingerprints");

var divineDecision = new CombatDecision
{
    HasAction = true,
    Action = legalDivineChoiceState.Actions[0]
};
Assert(CombatDecisionFreshnessPolicy.TryBindCurrent(
           legalDivineChoiceState.BattleSessionId,
           legalDivineChoiceState.Fingerprint,
           legalDivineChoiceState,
           divineDecision,
           out var reboundDivineDecision,
           out _)
       && reboundDivineDecision.Action == legalDivineChoiceState.Actions[0]
       && !CombatDecisionFreshnessPolicy.TryBindCurrent(
           legalDivineChoiceState.BattleSessionId,
           legalDivineChoiceState.Fingerprint,
           fullHandDivineChoiceState,
           divineDecision,
           out _,
           out _),
    "background decisions bind only to the unchanged battle observation and pass execution-time eligibility again");

var isolatedSourceState = CombatPlayerObservationBoundary.Normalize(
    new CombatStateObservation
    {
        BattleSessionId = 88,
        Sequence = 1,
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            Kind = CombatTargetKind.Self,
            CurrentHp = 20,
            MaxHp = 20
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 5,
                MaxHp = 5
            }
        },
        Actions =
        {
            new CombatActionObservation
            {
                CandidateId = "isolated-reject",
                SourceId = "isolated-reject",
                Kind = CombatActionKind.PlayCard,
                TargetRuntimeId = 2,
                TargetKind = CombatTargetKind.Enemy,
                Legal = true,
                Semantics = new CombatActionSemantics { Damage = 5d }
            },
            new CombatActionObservation
            {
                CandidateId = "isolated-fallback",
                SourceId = "isolated-fallback",
                Kind = CombatActionKind.PlayCard,
                TargetRuntimeId = 2,
                TargetKind = CombatTargetKind.Enemy,
                Legal = true,
                Semantics = new CombatActionSemantics { Damage = 5d }
            }
        }
    });
var isolatedSourceEngine = new CombatDecisionEngine();
CombatStateObservation isolatedPreparedState;
CombatDecisionEngine isolatedWorker;
using (CombatAiRegistry.RegisterPreflightRule(
           "Tests",
           "IsolatedWorkerPreflight",
           new RejectCandidateRule("isolated-reject"),
           100))
{
    isolatedPreparedState =
        isolatedSourceEngine.PrepareStateForIsolatedWorker(
            isolatedSourceState);
    isolatedWorker = isolatedSourceEngine.CreateIsolatedWorker(
        isolatedSourceEngine.SnapshotSimulationRulesForIsolatedWorker());
}
var isolatedWorkerDecision = Task.Run(() => isolatedWorker.Choose(
        isolatedPreparedState,
        new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 8,
            SearchMinimumSimulations = 2,
            SearchStabilityWindow = 2,
            SearchStableChecks = 1,
            SearchMaxPly = 3,
            SearchNodeBudget = 256
        }))
    .GetAwaiter()
    .GetResult();
Assert(!isolatedPreparedState.Actions.Single(action =>
           action.CandidateId == "isolated-reject").Legal
       && isolatedWorkerDecision.Action?.CandidateId
       == "isolated-fallback",
    "isolated decision workers consume a main-thread-prepared snapshot without consulting mutable runtime registries");
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

var duplicateCardState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        RuntimeId = 951,
        CurrentHp = 20,
        MaxHp = 20
    },
    HandCount = 2,
    HandCardIds = { "duplicate-strike", "duplicate-strike" },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 952,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 12,
            MaxHp = 12
        }
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "duplicate-strike:1",
            SourceId = "duplicate-strike",
            RuntimeId = 953,
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 952,
            TargetKind = CombatTargetKind.Enemy,
            Features = { ["handIndex"] = 0d },
            Semantics = new CombatActionSemantics { Damage = 3d }
        },
        new CombatActionObservation
        {
            CandidateId = "duplicate-strike:2",
            SourceId = "duplicate-strike",
            RuntimeId = 954,
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 952,
            TargetKind = CombatTargetKind.Enemy,
            Features = { ["handIndex"] = 1d },
            Semantics = new CombatActionSemantics { Damage = 3d }
        },
        new CombatActionObservation
        {
            CandidateId = "duplicate-end",
            Kind = CombatActionKind.EndTurn
        }
    }
};
var duplicateCardDecision = new CombatDecisionEngine().Choose(
    duplicateCardState,
    new CombatDecisionProfile
    {
        SearchBudgetMode = "fixed",
        SearchSimulationBudget = 96,
        SearchMinimumSimulations = 16,
        SearchNodeBudget = 512,
        SearchMaxPly = 4
    });
Assert(duplicateCardDecision.SearchCandidateCount
       < duplicateCardDecision.SearchOriginalCandidateCount
       && duplicateCardDecision.Plan.Count(step =>
           step.SourceId == "duplicate-strike") == 2,
    "search candidate compression merges equivalent physical copies while preserving their total usable count"
    + $" (compressed={duplicateCardDecision.SearchCandidateCount}, original={duplicateCardDecision.SearchOriginalCandidateCount}, plan={string.Join(",", duplicateCardDecision.Plan.Select(step => step.CandidateId))})");

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
        Enemies =
        [
            new CombatSimulationUnit
            {
                RuntimeId = 1,
                Hp = 20,
                MaxHp = 20
            }
        ],
        Threats =
        [
            new CombatSimulationThreat
            {
                SourceRuntimeId = 1,
                BlockableDamage = 7d,
                Probability = 1d
            }
        ]
    },
    new CombatDecisionProfile());
Assert(persistentShieldTurn.PlayerHp == 30
       && persistentShieldTurn.PlayerDefend == 0
       && persistentShieldTurn.Power == 3
       && persistentShieldTurn.HandCount == 2
       && persistentShieldTurn.HandCardValues.Count == 2
       && persistentShieldTurn.DrawPileValues.Count == 1
       && persistentShieldTurn.DiscardPileValues.Count == 0
       && persistentShieldTurn.Threats.Length == 1
       && persistentShieldTurn.Threats[0].BlockableDamage > 0d,
    "end-turn baseline resolves enemy damage, clears shield, models card cycling, and projects next intent");
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
var lifecycleForwardTurn = CombatForwardModel.ApplyEndTurn(
    new CombatSimulationState
    {
        PlayerHp = 10,
        PlayerMaxHp = 20,
        PlayerDefend = 0,
        Power = 3,
        MaxPower = 3,
        HandLimit = 10,
        DrawPileKnown = true,
        Features =
        {
            ["drawPerTurn"] = 0d,
            [CombatTurnFeatureNames.EndTurnLifecycleHpLoss] = 1d,
            [CombatTurnFeatureNames.EndTurnLifecycleDefend] = 3d,
            [CombatTurnFeatureNames.StartTurnLifecycleHpLoss] = 1d,
            [CombatTurnFeatureNames.StartTurnLifecycleHeal] = 2d,
            [CombatTurnFeatureNames.StartTurnLifecycleDefend] = 4d
        },
        Enemies =
        [
            new CombatSimulationUnit
            {
                RuntimeId = 2,
                Hp = 10,
                MaxHp = 10
            }
        ],
        Threats =
        [
            new CombatSimulationThreat
            {
                SourceRuntimeId = 2,
                Probability = 1d,
                BlockableDamage = 8d
            }
        ]
    },
    new CombatDecisionProfile());
Assert(lifecycleForwardTurn.PlayerHp == 5
       && lifecycleForwardTurn.PlayerDefend == 4
       && lifecycleForwardTurn.Power == 3,
    "forward end-turn transition resolves player end effects, enemy intent, reset, and next-turn start effects in order");

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
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "clamped-set-hp",
        Cost = 0,
        Exhaust = true,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.SetHp,
                Target = CombatSimulationTarget.Self,
                Amount = 25
            },
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Damage,
                Target = CombatSimulationTarget.SelectedEnemy,
                Amount = 100
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
var clampedSetHpResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "clamped-set-hp",
        RulesetVersion = "card-lifecycle-v1",
        Seed = 3,
        InitialDraw = 1,
        DrawPerTurn = 0,
        Player = new CombatPlayerSetup
        {
            RoleId = "tester",
            MaxHp = 24,
            CurrentHp = 20,
            Deck = { "clamped-set-hp" }
        },
        Enemies =
        {
            new CombatEnemySetup { EnemyId = "lifecycle-dummy" }
        }
    },
    lifecycleRules.Ruleset,
    new GreedyCombatSimulationPolicy());
Assert(clampedSetHpResult.Outcome == CombatSimulationOutcome.Victory
       && clampedSetHpResult.FinalState.Player?.Hp == 24,
    "SetHp clamps the assigned value to current maximum HP after role transformations");

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

var surplusEnergyRules = new CombatRulesetBuilder(
        "surplus-energy-transition-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "charge-surplus",
        Cost = 0,
        Exhaust = true,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.GainEnergy,
                Target = CombatSimulationTarget.Self,
                Amount = 5
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "finish-after-charge",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Damage,
                Target = CombatSimulationTarget.SelectedEnemy,
                Amount = 50
            }
        }
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "surplus-dummy",
        MaxHp = 20,
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
var preserveSurplusPolicy = new PreserveSurplusThenFinishPolicy();
var preserveSurplusResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "preserve-surplus-between-turns",
        RulesetVersion = "surplus-energy-transition-v1",
        InitialDraw = 2,
        DrawPerTurn = 1,
        Player = new CombatPlayerSetup
        {
            MaxHp = 20,
            CurrentHp = 20,
            BaseEnergy = 3,
            Deck = { "charge-surplus", "finish-after-charge" }
        },
        Enemies = { new CombatEnemySetup { EnemyId = "surplus-dummy" } },
        Limits = new CombatSimulationLimits { MaximumTurns = 3 }
    },
    surplusEnergyRules.Ruleset,
    preserveSurplusPolicy);
Assert(surplusEnergyRules.Success
       && preserveSurplusPolicy.SecondTurnEnergy == 8,
    "authoritative simulator preserves above-cap energy at the next player turn instead of resetting it to base energy"
    + $" (rules={surplusEnergyRules.Success}, outcome={preserveSurplusResult.Outcome},"
    + $" secondTurnEnergy={preserveSurplusPolicy.SecondTurnEnergy},"
    + $" finalEnergy={preserveSurplusResult.FinalState.Player?.Energy})");

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
           .AuthoritativeActionsAudited > 0
       && authoritativeTeacherSimulation.Metrics
              .AuthoritativeSelectedActionsAudited > 0
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .AuditedSources.Count > 0
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .SourceKindAudits.Count > 0
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .InvalidActions == 0
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .SelectedInvalidActions == 0
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .ValidActions
          == authoritativeTeacherSimulation.Metrics
              .AuthoritativeActionsAudited
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .SelectedValidActions
          == authoritativeTeacherSimulation.Metrics
              .AuthoritativeSelectedActionsAudited
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .SelectedContextAdjustedActions
          == authoritativeTeacherSimulation.Metrics.SemanticAudit
              .SelectedExplainedActions
       && authoritativeTeacherSimulation.Metrics.SemanticAudit
              .SelectedUnexplainedMismatchActions
          == authoritativeTeacherSimulation.Metrics
              .AuthoritativeSelectedSemanticMismatches
       && (authoritativeTeacherSimulation.Metrics
               .AuthoritativeSelectedSemanticMismatches == 0
           || authoritativeTeacherSimulation.Metrics.SemanticAudit
                  .SelectedUnexplainedMismatchSources.Values.Sum()
              == authoritativeTeacherSimulation.Metrics
                  .AuthoritativeSelectedSemanticMismatches),
    "teacher policy audits projected choices through authoritative immutable action branches");

var teacherLegalityRules = new CombatRulesetBuilder(
        "authoritative-teacher-legality-v1")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "teacher-safe",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Damage,
                Target = CombatSimulationTarget.SelectedEnemy,
                Amount = 1
            }
        },
        RequiresEnemyTarget = true
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "teacher-prohibited",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Damage,
                Target = CombatSimulationTarget.SelectedEnemy,
                Amount = 100
            }
        },
        RequiresEnemyTarget = true
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "teacher-dummy",
        MaxHp = 50,
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
CombatAuthoritativeBranchTeacherPolicy teacherLegalityPolicy;
using (CombatAiRegistry.RegisterRoleStrategyProvider(
           "Tests",
           "teacher-legality",
           new ProhibitSourceRoleStrategyProvider("teacher-prohibited"),
           10000))
{
    teacherLegalityPolicy = new CombatAuthoritativeBranchTeacherPolicy(
        new CombatDecisionSimulationPolicy(new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 8,
            SearchNodeBudget = 64,
            SearchMaxPly = 2,
            SearchMinimumSimulations = 1
        }),
        new CombatAuthoritativeTeacherOptions
        {
            AuditProbability = 1d,
            MinimumOverrideGain = 0d,
            RandomSeed = 46
        });
}
var teacherLegalityResult = simulationEngine.Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "authoritative-teacher-legality",
        RulesetVersion = "authoritative-teacher-legality-v1",
        Seed = 46,
        InitialDraw = 2,
        DrawPerTurn = 0,
        Player = new CombatPlayerSetup
        {
            RoleId = "tester",
            MaxHp = 20,
            CurrentHp = 20,
            Deck = { "teacher-safe", "teacher-prohibited" }
        },
        Enemies =
        {
            new CombatEnemySetup { EnemyId = "teacher-dummy" }
        },
        Limits = new CombatSimulationLimits
        {
            MaximumTurns = 1,
            MaximumActions = 10,
            MaximumCommands = 100
        },
        TraceLevel = CombatSimulationTraceLevel.Full
    },
    teacherLegalityRules.Ruleset,
    teacherLegalityPolicy);
Assert(teacherLegalityRules.Success
       && teacherLegalityPolicy.LastDecision!.Candidates.Any(item =>
           item.Action.SourceId == "teacher-prohibited" && !item.Legal)
       && teacherLegalityResult.Events.All(item =>
           item.Kind != CombatSimulationEventKind.CardPlayed
           || item.DefinitionId != "teacher-prohibited")
       && teacherLegalityPolicy.LastObservation!.Features
              .GetValueOrDefault("roleStrategy:test.prepared-state") == 1d,
    "authoritative teacher intersects simulator actions with decision legality and exposes prepared role-state telemetry");

var semanticAuditState = new CombatBattleState
{
    PlayerActorId = 1,
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            Kind = CombatSimulationActorKind.Player,
            Hp = 20,
            MaxHp = 30
        },
        new CombatActorState
        {
            ActorId = 2,
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 5,
            MaxHp = 5
        }
    }
};
var semanticAuditEvents = new List<CombatSimulationEvent>
{
    new()
    {
        Kind = CombatSimulationEventKind.DamageDealt,
        TargetActorId = 2,
        Amount = 5
    },
    new()
    {
        Kind = CombatSimulationEventKind.CardDrawn,
        TargetActorId = 1,
        Amount = 1
    }
};
var matchingSemanticAudit = CombatSemanticAuditor.Audit(
    semanticAuditState,
    semanticAuditEvents,
    new CombatActionSemantics { Damage = 5d, Draw = 1d },
    new CombatSimulationAction { DefinitionId = "audit-card" });
var mismatchingSemanticAudit = CombatSemanticAuditor.Audit(
    semanticAuditState,
    semanticAuditEvents,
    new CombatActionSemantics { Defend = 10d },
    new CombatSimulationAction { DefinitionId = "audit-card" });
var invalidSemanticAudit = CombatSemanticAuditor.Audit(
    semanticAuditState,
    semanticAuditState,
    Array.Empty<CombatSimulationEvent>(),
    new CombatActionSemantics { Damage = 5d },
    new CombatSimulationAction { DefinitionId = "audit-card" },
    null);
Assert(!matchingSemanticAudit.Mismatch
       && mismatchingSemanticAudit.MismatchKinds.Contains("damage")
       && mismatchingSemanticAudit.MismatchKinds.Contains("defend")
       && invalidSemanticAudit.Invalid
       && !invalidSemanticAudit.Mismatch
       && invalidSemanticAudit.Status == CombatSemanticAuditStatus.Invalid,
    "semantic auditing compares projected action meaning with causal authoritative events instead of noisy net state deltas");
var nativeIntrinsicAudit = CombatSemanticAuditor.Audit(
    semanticAuditState,
    new[]
    {
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 41,
            HandlerId = "native:universalcard_16:on-use",
            SourceRewardId = "universalcard_16",
            Kind = CombatSimulationEventKind.BlockGained,
            TargetActorId = 1,
            Amount = 8
        },
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 41,
            HandlerId = "native:relic-trigger",
            SourceRewardId = "relic_1",
            Kind = CombatSimulationEventKind.BlockGained,
            TargetActorId = 1,
            Amount = 3
        }
    },
    new CombatActionSemantics { Defend = 8d },
    new CombatSimulationAction
    {
        CardInstanceId = 41,
        DefinitionId = "universalcard_16"
    });
Assert(!nativeIntrinsicAudit.Mismatch
       && nativeIntrinsicAudit.Comparisons.Any(item =>
           item.Kind == "defend"
           && item.Classification == "explained"
           && item.Explanation == "trigger-side-effect"),
    "native on-use events sourced from the played card are intrinsic while downstream reward triggers remain contextual");

var realizedBefore = semanticAuditState.Clone();
realizedBefore.ActionSequence = 0;
realizedBefore.Cards.Add(new CombatCardInstanceState
{
    InstanceId = 41,
    CardId = "dynamic-native-card"
});
realizedBefore.Hand.Add(41);
var realizedAfter = realizedBefore.Clone();
realizedAfter.Player!.Hp = 30;
realizedAfter.FindActor(2)!.Hp = 0;
realizedAfter.Hand.Remove(41);
realizedAfter.Cards.Add(new CombatCardInstanceState
{
    InstanceId = 42,
    CardId = "generated-card"
});
realizedAfter.Hand.Add(42);
var realizedEvents = new List<CombatSimulationEvent>
{
    new()
    {
        SourceActionId = 1,
        CardInstanceId = 41,
        SourceRewardId = "dynamic-native-card",
        Kind = CombatSimulationEventKind.DamageDealt,
        TargetActorId = 2,
        Amount = 5
    },
    new()
    {
        SourceActionId = 1,
        CardInstanceId = 41,
        SourceRewardId = "dynamic-native-card",
        Kind = CombatSimulationEventKind.VariableChanged,
        TargetActorId = 1,
        DefinitionId = "Hp",
        Message = "Hp",
        Amount = 10
    },
    new()
    {
        SourceActionId = 1,
        CardInstanceId = 42,
        SourceRewardId = "dynamic-native-card",
        Kind = CombatSimulationEventKind.CardDrawn,
        TargetActorId = 1,
        Amount = 1
    },
    new()
    {
        SourceActionId = 1,
        SourceRewardId = "dynamic-native-card",
        Kind = CombatSimulationEventKind.RandomResolved,
        Amount = 1
    }
};
var realizedAction = new CombatSimulationAction
{
    ActorId = 1,
    CardInstanceId = 41,
    TargetActorId = 2,
    DefinitionId = "dynamic-native-card"
};
var realizedSemantics = CombatSemanticAuditor.ProjectRealized(
    realizedBefore,
    realizedAfter,
    realizedEvents,
    realizedAction,
    null);
var realizedAudit = CombatSemanticAuditor.Audit(
    realizedBefore,
    realizedAfter,
    realizedEvents,
    realizedSemantics,
    realizedAction,
    null);
var untracedCreationAfter = realizedBefore.Clone();
untracedCreationAfter.Cards.Add(new CombatCardInstanceState
{
    InstanceId = 43,
    CardId = "untraced-card"
});
var untracedCreationSemantics = CombatSemanticAuditor.ProjectRealized(
    realizedBefore,
    untracedCreationAfter,
    realizedEvents.Take(2).ToList(),
    realizedAction,
    null);
var untracedCreationAudit = CombatSemanticAuditor.Audit(
    realizedBefore,
    untracedCreationAfter,
    realizedEvents.Take(2).ToList(),
    untracedCreationSemantics,
    realizedAction,
    null);
Assert(realizedSemantics.Damage == 5d
       && realizedSemantics.Heal == 10d
       && realizedSemantics.CardGeneration == 1d
       && realizedSemantics.Draw == 0d
       && realizedSemantics.RandomOutcome
       && realizedAudit.Valid
       && !realizedAudit.Mismatch
       && untracedCreationAudit.InvalidKinds.Contains("card-generation"),
    "authoritative realized projection separates created-to-hand cards from draws, represents direct HP assignment, and still rejects untraced mutations");

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
var bundledRulesV2Path = Path.Combine(
    repositoryRoot,
    "AuraToolsExp",
    "Config",
    "combat-simulation",
    "witch-base-evaluation-v2.ruleset.json");
if (!File.Exists(bundledRulesV2Path))
{
    repositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    bundledRulesV2Path = Path.Combine(
        repositoryRoot,
        "AuraToolsExp",
        "Config",
        "combat-simulation",
        "witch-base-evaluation-v2.ruleset.json");
}
var bundledJsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};
bundledJsonOptions.Converters.Add(new JsonStringEnumConverter());
var bundledCampaignPath = Path.Combine(
    repositoryRoot,
    "AuraToolsExp",
    "Config",
    "combat-simulation",
    "witch-world-simulation-v2.campaign.json");
var bundledCampaign = JsonSerializer.Deserialize<CombatCampaignDefinition>(
    File.ReadAllText(bundledCampaignPath),
    bundledJsonOptions)
    ?? throw new InvalidOperationException(
        "Bundled campaign v2 JSON could not be deserialized.");
var bundledRulesV2Document = JsonSerializer.Deserialize<CombatRulesetDocument>(
    File.ReadAllText(bundledRulesV2Path),
    bundledJsonOptions);
var bundledRulesV2 = CombatSimulationRegistry.BuildRuleset(bundledRulesV2Document);
bundledRulesV2.Ruleset.TryGetStatus("buff_burn", out var bundledBurn);
var lifecycleCoreRules = new CombatRulesetBuilder("lifecycle-core-current")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "cycle-guard",
        Cost = 0,
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
        CardId = "cycle-filler",
        Cost = 99
    })
    .RegisterEnemy(new CombatEnemyDefinition
    {
        OwnerModId = "Tests",
        EnemyId = "waiting-enemy",
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
var midDrawShuffleResult = new CombatSimulationEngine().Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "mid-draw-discard-recycle",
        RulesetVersion = lifecycleCoreRules.Ruleset.Version,
        InitialDraw = 2,
        DrawPerTurn = 0,
        InitialDiscardCards = { "cycle-filler" },
        Player = new CombatPlayerSetup
        {
            MaxHp = 30,
            CurrentHp = 30,
            Deck = { "cycle-guard" }
        },
        Enemies = { new CombatEnemySetup { EnemyId = "waiting-enemy" } },
        Limits = new CombatSimulationLimits { MaximumTurns = 1 }
    },
    lifecycleCoreRules.Ruleset,
    EndTurnSimulationPolicy.Instance);
var persistentBlockResult = new CombatSimulationEngine().Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "persistent-player-block",
        RulesetVersion = lifecycleCoreRules.Ruleset.Version,
        InitialDraw = 2,
        DrawPerTurn = 0,
        Player = new CombatPlayerSetup
        {
            MaxHp = 30,
            CurrentHp = 30,
            Deck = { "cycle-guard", "cycle-filler" }
        },
        Enemies = { new CombatEnemySetup { EnemyId = "waiting-enemy" } },
        Limits = new CombatSimulationLimits { MaximumTurns = 2 }
    },
    lifecycleCoreRules.Ruleset,
    new PlayCardsInOrderThenEndPolicy("cycle-guard"));
var waitingEnemyActorId = persistentBlockResult.FinalState.Actors
    .Single(actor => actor.Kind == CombatSimulationActorKind.Enemy)
    .ActorId;
Assert(lifecycleCoreRules.Success
       && midDrawShuffleResult.Metrics.CardsDrawn == 2
       && midDrawShuffleResult.Metrics.EmptyEndTurns == 1
       && midDrawShuffleResult.Metrics.SevereEndTurnMistakes == 1
       && midDrawShuffleResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.DeckShuffled)
       && persistentBlockResult.FinalState.Player?.Block == 0
       && persistentBlockResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.CardDiscarded
           && item.DefinitionId == "cycle-filler")
       && persistentBlockResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.TurnStarted
           && item.SourceActorId == waitingEnemyActorId)
       && persistentBlockResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.TurnEnded
           && item.SourceActorId == waitingEnemyActorId),
    "combat lifecycle clears prior-turn block, cycles cards, and emits actor-scoped enemy round events");

var ritualCourageResult = new CombatSimulationEngine().Run(
    new CombatScenarioDefinition
    {
        ScenarioId = "ritual-courage-damage-conversion",
        RulesetVersion = bundledRulesV2.Ruleset.Version,
        InitialDraw = 2,
        DrawPerTurn = 0,
        RequireAuthoritativeRules = true,
        TraceLevel = CombatSimulationTraceLevel.Full,
        Player = new CombatPlayerSetup
        {
            RoleId = "career_1",
            MaxHp = 100,
            CurrentHp = 100,
            BaseEnergy = 5,
            Deck = { "ritualcard_8", "card_1" }
        },
        Enemies = { new CombatEnemySetup { EnemyId = "enemy_10001" } },
        Limits = new CombatSimulationLimits { MaximumTurns = 1 }
    },
    bundledRulesV2.Ruleset,
    new PlayCardsInOrderThenEndPolicy("ritualcard_8", "card_1"));
Assert(ritualCourageResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.BlockGained
           && item.SourceActorId == ritualCourageResult.FinalState.PlayerActorId
           && item.Amount == 5)
       && ritualCourageResult.FinalState.Player?.Statuses.All(item =>
           item.StatusId != "buff_ritualcourage") == true,
    "ritual courage converts actual player damage into block at turn end and then ends");
var baseCardAuditState = new CombatBattleState
{
    PlayerActorId = 1,
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            Kind = CombatSimulationActorKind.Player,
            Hp = 30,
            MaxHp = 30,
            Variables = { ["Perceive"] = 5d }
        },
        new CombatActorState
        {
            ActorId = 2,
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 20,
            MaxHp = 20,
            Block = 5
        }
    }
};
var strikeAfter = baseCardAuditState.Clone();
strikeAfter.FindActor(2)!.Block = 0;
var strikeAudit = CombatSemanticAuditor.Audit(
    baseCardAuditState,
    strikeAfter,
    new[]
    {
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 101,
            Kind = CombatSimulationEventKind.DamageDealt,
            TargetActorId = 2,
            Amount = 0
        }
    },
    new CombatActionSemantics { Damage = 5d },
    new CombatSimulationAction
    {
        ActorId = 1,
        CardInstanceId = 101,
        TargetActorId = 2,
        DefinitionId = "card_1"
    },
    bundledRulesV2.Ruleset);
var defendAfter = baseCardAuditState.Clone();
defendAfter.Player!.Block = 6;
var defendAudit = CombatSemanticAuditor.Audit(
    baseCardAuditState,
    defendAfter,
    new[]
    {
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 102,
            Kind = CombatSimulationEventKind.BlockGained,
            TargetActorId = 1,
            Amount = 6
        }
    },
    new CombatActionSemantics { Defend = 5d },
    new CombatSimulationAction
    {
        ActorId = 1,
        CardInstanceId = 102,
        DefinitionId = "card_2"
    },
    bundledRulesV2.Ruleset);
var cappedBurnBefore = baseCardAuditState.Clone();
cappedBurnBefore.FindActor(2)!.Block = 0;
cappedBurnBefore.FindActor(2)!.Statuses.Add(new CombatStatusState
{
    StatusId = "buff_burn",
    Stacks = bundledBurn?.MaximumStacks ?? 99
});
var cappedBurnAfter = cappedBurnBefore.Clone();
cappedBurnAfter.FindActor(2)!.Hp -= 6;
var burningAudit = CombatSemanticAuditor.Audit(
    cappedBurnBefore,
    cappedBurnAfter,
    new[]
    {
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 103,
            Kind = CombatSimulationEventKind.StatusAdded,
            TargetActorId = 2,
            DefinitionId = "buff_burn",
            Amount = 2
        },
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 103,
            Kind = CombatSimulationEventKind.DamageDealt,
            TargetActorId = 2,
            Amount = 6
        }
    },
    new CombatActionSemantics { Damage = 6d },
    new CombatSimulationAction
    {
        ActorId = 1,
        CardInstanceId = 103,
        TargetActorId = 2,
        DefinitionId = "burningcard_2"
    },
    bundledRulesV2.Ruleset);
var elementBefore = baseCardAuditState.Clone();
elementBefore.FindActor(2)!.Block = 0;
var elementAfter = elementBefore.Clone();
elementAfter.FindActor(2)!.Hp -= 7;
elementAfter.Player!.Statuses.Add(new CombatStatusState
{
    StatusId = "buff_elements",
    Stacks = 1
});
elementAfter.FindActor(2)!.Statuses.Add(new CombatStatusState
{
    StatusId = "buff_burn",
    Stacks = 2
});
var elementAudit = CombatSemanticAuditor.Audit(
    elementBefore,
    elementAfter,
    new[]
    {
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 104,
            Kind = CombatSimulationEventKind.StatusAdded,
            TargetActorId = 1,
            DefinitionId = "buff_elements",
            Amount = 1
        },
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 104,
            Kind = CombatSimulationEventKind.StatusAdded,
            TargetActorId = 2,
            DefinitionId = "buff_burn",
            Amount = 2
        },
        new CombatSimulationEvent
        {
            SourceActionId = 1,
            CardInstanceId = 104,
            Kind = CombatSimulationEventKind.DamageDealt,
            TargetActorId = 2,
            Amount = 7
        }
    },
    new CombatActionSemantics
    {
        Damage = 7d,
        Buff = 1d,
        Debuff = 2d
    },
    new CombatSimulationAction
    {
        ActorId = 1,
        CardInstanceId = 104,
        TargetActorId = 2,
        DefinitionId = "elementscard_9"
    },
    bundledRulesV2.Ruleset);
Assert(!strikeAudit.Mismatch
       && strikeAudit.ExplainedKinds.Contains("damage")
       && !defendAudit.Mismatch
       && defendAudit.ExplainedKinds.Contains("defend")
       && !burningAudit.Mismatch
       && !burningAudit.MismatchKinds.Contains("debuff")
       && !elementAudit.Mismatch,
    "base deck semantic audits distinguish block, Perceive and status caps from unexplained card projection errors");

var phasedSemanticState = new CombatBattleState
{
    Turn = 1,
    Phase = CombatSimulationPhase.PlayerAction,
    PlayerActorId = 1,
    NextActorId = 4,
    NextCardInstanceId = 105,
    Actors =
    {
        new CombatActorState
        {
            ActorId = 1,
            InstanceKey = "player",
            DefinitionId = "career_1",
            Kind = CombatSimulationActorKind.Player,
            Hp = 100,
            MaxHp = 100,
            Energy = 10,
            BaseEnergy = 10
        },
        new CombatActorState
        {
            ActorId = 2,
            InstanceKey = "semantic-enemy-a",
            DefinitionId = "enemy_10001",
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 200,
            MaxHp = 200
        },
        new CombatActorState
        {
            ActorId = 3,
            InstanceKey = "semantic-enemy-b",
            DefinitionId = "enemy_10001",
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 200,
            MaxHp = 200
        }
    },
    Cards =
    {
        new CombatCardInstanceState
        {
            InstanceId = 101,
            CardId = "burningcard_1",
            ApparentCardId = "burningcard_1"
        },
        new CombatCardInstanceState
        {
            InstanceId = 102,
            CardId = "burningcard_2",
            ApparentCardId = "burningcard_2"
        },
        new CombatCardInstanceState
        {
            InstanceId = 103,
            CardId = "card_4",
            ApparentCardId = "card_4"
        },
        new CombatCardInstanceState
        {
            InstanceId = 104,
            CardId = "elementscard_9",
            ApparentCardId = "elementscard_9"
        }
    },
    Hand = { 101, 102, 103, 104 }
};
bundledRulesV2.Ruleset.TryGetCard(
    "burningcard_1",
    out var goldenFireRain);
bundledRulesV2.Ruleset.TryGetCard(
    "burningcard_2",
    out var goldenBlazingNova);
bundledRulesV2.Ruleset.TryGetCard(
    "card_4",
    out var goldenHeavyBlade);
bundledRulesV2.Ruleset.TryGetCard(
    "elementscard_9",
    out var goldenCannedElement);
var fireRainAction = new CombatSimulationAction
{
    Kind = CombatSimulationActionKind.PlayCard,
    ActorId = 1,
    CardInstanceId = 101,
    TargetActorId = 2,
    DefinitionId = "burningcard_1"
};
var fireRainProjection = CombatAuthoritativeSemanticProjector.Project(
    bundledRulesV2.Ruleset,
    phasedSemanticState,
    goldenFireRain!,
    fireRainAction);
var fireRainImmediate = fireRainProjection.TargetEffects.Where(item =>
    item.Phase == CombatSemanticEffectPhase.Immediate
    && item.Kind == CombatSemanticEffectKind.Damage).ToList();
var fireRainBurn = fireRainProjection.TargetEffects.Where(item =>
    item.Phase == CombatSemanticEffectPhase.Immediate
    && item.Kind == CombatSemanticEffectKind.AddStatus
    && item.DefinitionId == "buff_burn").ToList();
var fireRainDeferred = fireRainProjection.TargetEffects.Where(item =>
    item.Phase == CombatSemanticEffectPhase.Deferred
    && item.Kind == CombatSemanticEffectKind.DirectHpLoss
    && item.DefinitionId == "buff_burn").ToList();
Assert(fireRainImmediate.Count == 2
       && fireRainImmediate.All(item =>
           item.RawAmount == 4d
           && item.EffectiveAmount == 4d)
       && fireRainProjection.ImmediateHpDamage == 8d
       && fireRainProjection.AffectedEnemyCount == 2
       && fireRainBurn.Count == 2
       && fireRainBurn.All(item => item.EffectiveAmount == 2d)
       && fireRainDeferred.Count == 2
       && fireRainDeferred.All(item =>
           item.EffectiveAmount == 4d
           && item.BypassesBlock)
       && fireRainProjection.DeferredHpDamage == 8d,
    "fire rain projects per-enemy immediate damage, aggregate damage, two burn stacks, and deferred shield-bypassing burn");

var novaProjection = CombatAuthoritativeSemanticProjector.Project(
    bundledRulesV2.Ruleset,
    phasedSemanticState,
    goldenBlazingNova!,
    new CombatSimulationAction
    {
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 102,
        TargetActorId = 2,
        DefinitionId = "burningcard_2"
    });
Assert(novaProjection.TargetEffects.Count(item =>
           item.Phase == CombatSemanticEffectPhase.Immediate
           && item.Kind == CombatSemanticEffectKind.Damage) == 1
       && novaProjection.ImmediateHpDamage == 6d
       && novaProjection.TargetEffects.Single(item =>
           item.Phase == CombatSemanticEffectPhase.Immediate
           && item.Kind == CombatSemanticEffectKind.AddStatus
           && item.DefinitionId == "buff_burn").EffectiveAmount == 2d,
    "blazing nova keeps its single-target six damage and two burn semantics");

var empoweredState = phasedSemanticState.Clone();
empoweredState.Player!.Variables["Strength"] = 10d;
empoweredState.Player.Statuses.Add(new CombatStatusState
{
    StatusId = "buff_extraordinary",
    Stacks = 20
});
var heavyBladeProjection = CombatAuthoritativeSemanticProjector.Project(
    bundledRulesV2.Ruleset,
    empoweredState,
    goldenHeavyBlade!,
    new CombatSimulationAction
    {
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 103,
        TargetActorId = 2,
        DefinitionId = "card_4"
    });
Assert(heavyBladeProjection.TargetEffects.Count(item =>
           item.Phase == CombatSemanticEffectPhase.Immediate
           && item.Kind == CombatSemanticEffectKind.Damage
           && item.RawAmount == 6d
           && item.EffectiveAmount == 9d) == 2
       && heavyBladeProjection.ImmediateHpDamage == 18d,
    "heavy blade uses the shared Strength and extraordinary resolver for every enemy");

var phasedScenario = new CombatScenarioDefinition
{
    ScenarioId = "targeted-phased-golden",
    RulesetVersion = bundledRulesV2.Ruleset.Version,
    InitialDraw = 0,
    DrawPerTurn = 0,
    HandLimit = 10,
    RequireAuthoritativeRules = true,
    TraceLevel = CombatSimulationTraceLevel.Full,
    Player = new CombatPlayerSetup
    {
        RoleId = "career_1",
        MaxHp = 100,
        CurrentHp = 100,
        BaseEnergy = 10
    },
    Enemies =
    {
        new CombatEnemySetup { EnemyId = "enemy_10001" },
        new CombatEnemySetup { EnemyId = "enemy_10001" }
    },
    Limits = new CombatSimulationLimits
    {
        MaximumTurns = 2,
        MaximumActions = 20,
        MaximumCommands = 1000,
        MaximumCommandsPerAction = 500,
        MaximumTriggerWavesPerAction = 50
    }
};
var phasedEngine = new CombatSimulationEngine();
var goldenFireActions = phasedEngine.GetLegalPlayerActions(
    phasedScenario,
    bundledRulesV2.Ruleset,
    phasedSemanticState);
var goldenFireAction = goldenFireActions.Single(item =>
    item.Kind == CombatSimulationActionKind.PlayCard
    && item.DefinitionId == "burningcard_1");
var goldenFireApplied = phasedEngine.ForkAndApplyPlayerAction(
    phasedScenario,
    bundledRulesV2.Ruleset,
    phasedSemanticState,
    goldenFireAction,
    captureSemanticEvents: true);
var goldenFireAudit = CombatSemanticAuditor.Audit(
    phasedSemanticState,
    goldenFireApplied.State,
    goldenFireApplied.Events,
    fireRainProjection,
    goldenFireAction,
    bundledRulesV2.Ruleset);
Assert(goldenFireApplied.Success
       && !goldenFireAudit.Invalid
       && !goldenFireAudit.Mismatch
       && goldenFireAudit.AuditedKinds.Contains("damage:target:2")
       && goldenFireAudit.AuditedKinds.Contains("damage:target:3"),
    "multi-target audit compares the same per-target vector and aggregate observed by the simulator");

var elementTimingState = phasedSemanticState.Clone();
elementTimingState.Actors.RemoveAll(item => item.ActorId == 3);
elementTimingState.Hand.Clear();
elementTimingState.Hand.AddRange(new[] { 104, 101 });
var elementActions = phasedEngine.GetLegalPlayerActions(
    phasedScenario,
    bundledRulesV2.Ruleset,
    elementTimingState);
var elementApplied = phasedEngine.ForkAndApplyPlayerAction(
    phasedScenario,
    bundledRulesV2.Ruleset,
    elementTimingState,
    elementActions.Single(item =>
        item.Kind == CombatSimulationActionKind.PlayCard
        && item.DefinitionId == "elementscard_9"),
    captureSemanticEvents: true);
var elementProjection = CombatAuthoritativeSemanticProjector.Project(
    bundledRulesV2.Ruleset,
    elementTimingState,
    goldenCannedElement!,
    elementActions.Single(item =>
        item.Kind == CombatSimulationActionKind.PlayCard
        && item.DefinitionId == "elementscard_9"));
var elementTimingScenario = new CombatScenarioDefinition
{
    ScenarioId = "element-acquisition-action-boundary",
    RulesetVersion = bundledRulesV2.Ruleset.Version,
    InitialDraw = 2,
    DrawPerTurn = 0,
    HandLimit = 10,
    RequireAuthoritativeRules = true,
    TraceLevel = CombatSimulationTraceLevel.Full,
    Player = new CombatPlayerSetup
    {
        RoleId = "career_1",
        MaxHp = 100,
        CurrentHp = 100,
        BaseEnergy = 10,
        Deck = { "elementscard_9", "burningcard_1" }
    },
    Enemies =
    {
        new CombatEnemySetup { EnemyId = "enemy_10001", HpScale = 5d }
    },
    Limits = new CombatSimulationLimits
    {
        MaximumTurns = 1,
        MaximumActions = 20,
        MaximumCommands = 1000,
        MaximumCommandsPerAction = 500,
        MaximumTriggerWavesPerAction = 50
    }
};
var elementSequenceResult = phasedEngine.Run(
    elementTimingScenario,
    bundledRulesV2.Ruleset,
    new PlayCardsInOrderThenEndPolicy(
        "elementscard_9",
        "burningcard_1"));
Assert(elementProjection.TargetEffects.All(item =>
           item.Phase != CombatSemanticEffectPhase.PostAction
           || item.DefinitionId != "buff_extraordinary"),
    "the phased semantic projection excludes newly acquired elements from the acquisition action");
Assert(elementSequenceResult.Outcome != CombatSimulationOutcome.Invalid
       && !elementSequenceResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.StatusAdded
           && item.DefinitionId == "buff_extraordinary"
           && item.SourceActionId == 1)
       && elementSequenceResult.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.StatusAdded
           && item.DefinitionId == "buff_extraordinary"
           && item.SourceActionId == 2
           && item.Amount == 2)
       && elementSequenceResult.FinalState.Player?.Statuses.Single(item =>
           item.StatusId == "buff_extraordinary").Stacks == 2,
    "new element stacks do not answer their acquisition action and start adding extraordinary after the next action: outcome="
    + elementSequenceResult.Outcome
    + ", statuses="
    + string.Join(",", elementSequenceResult.FinalState.Player?.Statuses.Select(
        item => item.StatusId + "=" + item.Stacks) ?? Array.Empty<string>())
    + ", unsupported="
    + string.Join(",", elementSequenceResult.UnsupportedDefinitions));

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
bundledRulesV2.Ruleset.TryGetCard(
    "timekeeper_2",
    out var bundledReturnEnergy);
bundledRulesV2.Ruleset.TryGetCard(
    "Crowdfundingcard_16",
    out var bundledRestoreEnergy);
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
bundledRulesV2.Ruleset.TryGetCard(
    "careercard_1",
    out var bundledDivineChoice);
CombatCampaignWorldPlanner.Validate(bundledCampaign);
bundledCampaign.TraceLevel = CombatSimulationTraceLevel.Summary;
var bundledSemanticProbe = CombatFoundationSemanticProbe.Validate(
    bundledCampaign,
    bundledRulesV2.Ruleset,
    new CombatSimulationEngine());
Assert(
    bundledSemanticProbe.Success
    && bundledCampaign.TraceLevel == CombatSimulationTraceLevel.Summary
    && bundledSemanticProbe.Version
       == CombatPolicyValueProtocol.TrainingSemanticsVersion
    && bundledSemanticProbe.CanaryVersion
       == CombatFoundationSemanticProbeResult.CurrentCanaryVersion,
    "foundation semantic probe covers Blade and Shield, limit damage, "
    + "resource recurrence, retain, reshuffle, and the Summary-trace "
    + "semantic audit pipeline: "
    + "success="
    + bundledSemanticProbe.Success
    + ", trace="
    + bundledCampaign.TraceLevel
    + ", version="
    + bundledSemanticProbe.Version
    + ", canary="
    + bundledSemanticProbe.CanaryVersion
    + ", errors="
    + string.Join("; ", bundledSemanticProbe.Errors));
var bundledCampaignNormal = CombatCampaignWorldPlanner.Build(
    bundledCampaign,
    "normal",
    23816797UL);
var bundledCampaignAdvanced = CombatCampaignWorldPlanner.Build(
    bundledCampaign,
    "advanced",
    23816797UL);
var bundledThresholdRewardIds = bundledCampaign.AttributeThresholdRewards
    .Select(item => item.RewardId)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var expectedThresholdRewards = new Dictionary<string, string[]>(
    StringComparer.OrdinalIgnoreCase)
{
    ["Strength"] =
        new[] { "blessing_101", "blessing_105", "blessing_109", "blessing_113" },
    ["Lucky"] =
        new[] { "blessing_102", "blessing_106", "blessing_110", "blessing_114" },
    ["Perceive"] =
        new[] { "blessing_104", "blessing_108", "blessing_112", "blessing_116" },
    ["Wisdom"] =
        new[] { "blessing_103", "blessing_107", "blessing_111", "blessing_115" }
};
Assert(
    bundledCampaign.CampaignVersion == "3.0.0"
    && bundledCampaign.AttributeThresholdRewards.Count == 16
    && expectedThresholdRewards.All(pair =>
        bundledCampaign.AttributeThresholdRewards
            .Where(item => string.Equals(
                item.AttributeId,
                pair.Key,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Threshold)
            .Select(item => item.RewardId)
            .SequenceEqual(pair.Value))
    && bundledCampaignNormal.Encounters.All(item =>
        !bundledThresholdRewardIds.Contains(item.RewardOffer.BlessingId))
    && bundledCampaignAdvanced.Encounters.All(item =>
        !bundledThresholdRewardIds.Contains(item.RewardOffer.BlessingId)),
    "base-game origin threshold blessings are mapped authoritatively and excluded from ordinary blessing offers");

foreach (var pair in expectedThresholdRewards)
{
    foreach (var value in new[] { 9, 10, 19, 20, 29, 30, 39, 40 })
    {
        var boundaryState = new CombatCampaignState();
        foreach (var attributeId in bundledCampaign.AttributeIds)
        {
            var attributeValue = string.Equals(
                attributeId,
                pair.Key,
                StringComparison.OrdinalIgnoreCase)
                ? value
                : 0;
            boundaryState.Attributes[attributeId] = attributeValue;
            boundaryState.LayerBaseAttributes[attributeId] = attributeValue;
            boundaryState.PermanentAttributeBonuses[attributeId] = 0;
            boundaryState.AttributeUpperBounds[attributeId] = 100;
        }
        var expectedIds = pair.Value.Take(value / 10).ToList();
        var granted = CombatCampaignAttributeThresholdRewardReconciler.Reconcile(
            bundledCampaign,
            boundaryState);
        var grantedAgain =
            CombatCampaignAttributeThresholdRewardReconciler.Reconcile(
                bundledCampaign,
                boundaryState);
        Assert(
            boundaryState.Blessings
                .OrderBy(item => item, StringComparer.Ordinal)
                .SequenceEqual(expectedIds.OrderBy(
                    item => item,
                    StringComparer.Ordinal))
            && granted == expectedIds.Count
            && grantedAgain == 0
            && boundaryState.Blessings.Count == expectedIds.Count,
            pair.Key + " origin threshold " + value
            + " grants every reached blessing exactly once");
    }
}
var projectedThresholdState = new CombatCampaignState
{
    Attributes = { ["Strength"] = 10 },
    LayerBaseAttributes = { ["Strength"] = 10 },
    PermanentAttributeBonuses = { ["Strength"] = 0 },
    AttributeUpperBounds = { ["Strength"] = 40 }
};
CombatCampaignAttributeThresholdRewardReconciler.Reconcile(
    bundledCampaign,
    projectedThresholdState);
Assert(
    CombatCampaignRewardRuleProjector.Build(
            bundledCampaign,
            projectedThresholdState)
        .Any(item => item.RewardId == "blessing_101"
                     && !string.IsNullOrWhiteSpace(item.FightScript)),
    "reconciled origin blessings project their authoritative fight scripts into the next battle");
var firstBand = bundledCampaign.Encounters.Where(item =>
    item.NativeBand is 0 or -1).ToList();
Assert(bundledRulesV2.Success
       && bundledRulesV2.Ruleset.CardCount == 273
       && bundledRulesV2.Ruleset.EnemyCount == 55
        && bundledRulesV2.Ruleset.StatusCount == 129
        && bundledRulesV2.Ruleset.SnapshotCards().Count(item =>
            item.Fidelity == CombatRuleFidelity.Authoritative) == 273
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
        && bundledReturnEnergy.Effects.Single().Kind
        == CombatSimulationEffectKind.SetEnergy
        && bundledReturnEnergy.Effects.Single().AmountExpression?.Operation
        == CombatSimulationValueOperation.Maximum
        && bundledRestoreEnergy.Effects.Any(effect =>
            effect.Kind == CombatSimulationEffectKind.Draw
            && effect.Amount == 5)
        && bundledRestoreEnergy.Effects.Any(effect =>
            effect.Kind == CombatSimulationEffectKind.SetEnergy
            && effect.AmountExpression?.Operation
            == CombatSimulationValueOperation.SourceMaxEnergy)
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
       && !bundledDivineChoice.RequiresEnemyTarget
       && bundledDivineChoice.VerificationSource
          == "Decompiler:v1.0.23816797"
       && bundledDivineChoice.ActionContract?.Version
          == CombatActionContractProtocol.Version
       && bundledDivineChoice.ActionContract.Preconditions.Count == 2
       && bundledDivineChoice.ActionContract
              .MinimumCardsMovedFromDrawPileToHandOnApplied == 1
        && bundledCampaign.Encounters.Count == 48
       && bundledCampaign.Rewards.Count == 514
       && bundledCampaign.Rewards
           .Where(item => item.RewardId is
               "SpellCard_1"
               or "SpellCard_2"
               or "SpellCard_3"
               or "SpellCard_4")
           .All(item => item.CardAcquisition
               == CombatCampaignCardAcquisition.GeneratedOnly)
       && bundledCampaign.RequireAuthoritativeRules
       && bundledCampaign.InitialMoney == 100
       && bundledCampaign.Player.RoleId == "career_1"
       && bundledCampaign.Player.PartnerId == "Partner_10001"
       && bundledCampaign.Player.SkillCardIds.SequenceEqual(
           new[] { "careercard_1" })
       && bundledCampaign.Player.FamiliarBlessingIds.SequenceEqual(
           new[] { "blessing_38" })
       && bundledCampaign.Strategies.Count == 8
       && bundledCampaign.EnabledRewardCardPackIds.Contains("cardpack_1")
       && bundledCampaign.EnabledRewardCardPackIds.Contains("cardpack_2")
       && !bundledCampaign.EnabledRewardCardPackIds.Contains("cardpack_13")
       && bundledCampaign.TargetDeckSizeMinimum == 15
       && bundledCampaign.TargetDeckSizeMaximum == 24
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
           .Where(item => item.CardAcquisition
               == CombatCampaignCardAcquisition.RewardPool)
           .All(item => item.OfferWeight is 8d or 5d or 2d or 1d)
       && bundledCampaign.Rewards
           .Where(item => item.Kind == CombatCampaignRewardKind.Card
                          && item.CardAcquisition
                             != CombatCampaignCardAcquisition.RewardPool)
           .All(item => item.OfferWeight == 0d)
       && bundledCampaign.Rewards.Single(item =>
               item.RewardId == "ritualcard_8").BaseValue == 1.2d
       && bundledCampaign.Rewards.Single(item =>
               item.RewardId == "ritualcard_8").Features["defense"] == 1d
       && bundledCampaign.Rewards.Single(item =>
               item.RewardId == "ritualcard_8").Features["cycling"] == 0.8d
       && bundledCampaign.Rewards
           .Where(item =>
           item.Kind == CombatCampaignRewardKind.Card
           && item.RewardId.StartsWith(
               "curse",
               StringComparison.OrdinalIgnoreCase))
           .All(item => item.CardAcquisition
               == CombatCampaignCardAcquisition.CurseOnly)
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
      "bundled campaign v3 fixes seven layers, acquisition classes, game presets, strategies, final bosses, and paired difficulty worlds");
var bundledRewardLookup = bundledCampaign.Rewards
    .GroupBy(item => item.RewardId, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(
        group => group.Key,
        group => group.First(),
        StringComparer.OrdinalIgnoreCase);
foreach (var strategy in bundledCampaign.Strategies)
{
    var strategyState = new CombatCampaignState
    {
        Deck = new List<string>(strategy.RequiredCardIds),
        Relics = new List<string>(strategy.RequiredRelicIds),
        Blessings = new List<string>(strategy.RequiredBlessingIds)
    };
    var strategyProgress = CombatCampaignStrategyEvaluator.Evaluate(
            bundledCampaign,
            strategyState,
            bundledRewardLookup)
        .Single(item => string.Equals(
            item.StrategyId,
            strategy.StrategyId,
            StringComparison.OrdinalIgnoreCase));
    Assert(strategy.Deterministic
           && strategyProgress.Accessible
           && strategyProgress.Executable
           && Math.Abs(strategyProgress.Completion - 1d) < 0.0001d
           && strategy.RequiredCardIds.All(cardId =>
               bundledRewardLookup.TryGetValue(cardId, out var reward)
               && CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
                   reward,
                   bundledCampaign.EnabledRewardCardPackIds))
           && strategy.RequiredRelicIds.All(
               bundledRewardLookup.ContainsKey),
        "bundled deterministic strategy is attainable and executable: "
        + strategy.StrategyId);
}

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
var recordedEpochMetrics =
    new List<CombatPolicyValueEpochMetrics>();
var policyValueTraining = CombatPolicyValueTrainer.Train(
    episodes,
    "balanced",
    new CombatPolicyValueTrainingOptions
    {
        Epochs = 12,
        LearningRate = 0.01d,
        MinimumEpisodes = 4,
        RandomSeed = 17
    },
    CancellationToken.None,
    new CombatPolicyValueTrainingSession
    {
        EpochCompleted = metrics =>
            recordedEpochMetrics.Add(metrics)
    });
var policyValueNetworkValid = CombatPolicyValueNetworkValidator.TryValidate(
    policyValueTraining.Model,
    out var policyValueValidationDiagnostic);
var invalidPolicyEpochs = policyValueTraining.EpochHistory
    .Where(item => !item.Calibrated)
    .Count(item =>
        item.Training.CompositeLoss <= 0d
        || item.Validation.CompositeLoss <= 0d
        || item.TrainingMeasurement != "online-minibatch"
        || string.IsNullOrWhiteSpace(item.TrainingSplitHash)
        || string.IsNullOrWhiteSpace(item.ValidationSplitHash));
var invalidPolicyCandidates = policyValueTraining.CandidateModels.Count(candidate =>
    candidate.Model.PolicyTemperature is < 0.5d or > 3d
    || !candidate.Model.Metrics.ContainsKey("validationCompositeLoss")
    || candidate.Model.Metrics["policyTemperature"]
       != candidate.Model.PolicyTemperature);
var policyValueDiagnosticChecks = new Dictionary<string, bool>
{
    ["metric:testCompositeLoss"] =
        policyValueTraining.Model?.Metrics.ContainsKey("testCompositeLoss")
        == true,
    ["metric:optimizerAdamW"] =
        policyValueTraining.Model?.Metrics.GetValueOrDefault(
            "optimizerAdamW") == 1d,
    ["metric:temperature"] =
        policyValueTraining.Model?.Metrics.GetValueOrDefault(
            "policyTemperature")
        == policyValueTraining.Model?.PolicyTemperature,
    ["metric:validationPolicyCrossEntropy"] =
        policyValueTraining.Model?.Metrics.ContainsKey(
            "validationPolicyCrossEntropy") == true,
    ["metric:validationCriticalPolicyAccuracy"] =
        policyValueTraining.Model?.Metrics.ContainsKey(
            "validationCriticalPolicyAccuracy") == true,
    ["metric:validationDeathBrier"] =
        policyValueTraining.Model?.Metrics.ContainsKey(
            "validationDeathBrier") == true,
    ["metric:validationCompositeLoss"] =
        policyValueTraining.Model?.Metrics.ContainsKey(
            "validationCompositeLoss") == true,
    ["metric:trainingCompositeLoss"] =
        policyValueTraining.Model?.Metrics.ContainsKey(
            "trainingCompositeLoss") == true,
    ["frame-counts"] =
        policyValueTraining.TrainingMetrics.FrameCount > 0
        && policyValueTraining.ValidationMetrics.FrameCount > 0
        && policyValueTraining.ValidationMetrics.RunCount > 0,
    ["ci-order"] =
        policyValueTraining.ValidationMetrics.CompositeLossCiUpper
        >= policyValueTraining.ValidationMetrics.CompositeLossCiLower,
    ["epochs"] =
        policyValueTraining.EpochHistory.Count
        >= policyValueTraining.CompletedEpochs
        && invalidPolicyEpochs == 0,
    ["events"] =
        recordedEpochMetrics.Count
        == policyValueTraining.CompletedEpochs + 1,
    ["candidates"] =
        policyValueTraining.CandidateModels.Count is > 0 and <= 3
        && invalidPolicyCandidates == 0,
    ["strata"] =
        policyValueTraining.FrameStratificationProtocol
        == CombatPolicyValueFrameStratificationProtocol.Version
        && policyValueTraining.FrameStrata.Count >= 4
        && policyValueTraining.MinimumFrameWeight
        >= CombatPolicyValueFrameStratificationProtocol.MinimumWeight
        && policyValueTraining.MaximumFrameWeight <= 3d,
    ["network"] = policyValueNetworkValid
};
var failedPolicyValueChecks = string.Join(
    ",",
    policyValueDiagnosticChecks
        .Where(item => !item.Value)
        .Select(item => item.Key));
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
       && policyValueTraining.Model.ModelProtocol
          == "aura.combat-policy-value.mlp.v2"
       && policyValueTraining.Model.ProtocolVersion == 2
       && policyValueTraining.Model.ActionQuantileCount == 16
       && policyValueTraining.Model.ActionQuantileWeights.Length
          == policyValueTraining.Model.HiddenDimensions * 16
       && policyValueTraining.Model.Metrics.ContainsKey(
           "validationActionQuantilePinball")
       && policyValueTraining.Model.Metrics.ContainsKey(
           "validationCompositeLoss")
       && policyValueTraining.Model.Metrics.ContainsKey(
           "trainingCompositeLoss")
       && policyValueTraining.TrainingMetrics.FrameCount > 0
       && policyValueTraining.ValidationMetrics.FrameCount > 0
       && policyValueTraining.ValidationMetrics.RunCount > 0
       && policyValueTraining.ValidationMetrics
              .CompositeLossCiUpper
          >= policyValueTraining.ValidationMetrics
              .CompositeLossCiLower
       && policyValueTraining.EpochHistory.Count
          >= policyValueTraining.CompletedEpochs
       && policyValueTraining.EpochHistory.Any(item => item.Calibrated)
       && policyValueTraining.EpochHistory
           .Where(item => !item.Calibrated)
           .All(item =>
               item.Training.CompositeLoss > 0d
               && item.Validation.CompositeLoss > 0d
               && item.TrainingMeasurement
                  == "online-minibatch"
               && !string.IsNullOrWhiteSpace(
                   item.TrainingSplitHash)
               && !string.IsNullOrWhiteSpace(
                   item.ValidationSplitHash))
       && recordedEpochMetrics.Count
          == policyValueTraining.CompletedEpochs + 1
       && recordedEpochMetrics.Count(item =>
           item.EventKind == "epoch")
          == policyValueTraining.CompletedEpochs
       && recordedEpochMetrics.Count(item =>
           item.EventKind == "calibrated")
          == 1
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
       && policyValueNetworkValid,
    "complete episodes train a validated managed policy-value network, retain Top-K checkpoints, and select by multi-objective validation"
    + $" (success={policyValueTraining.Success}, model={policyValueTraining.Model != null},"
    + $" runs={policyValueTraining.Model?.Metrics.GetValueOrDefault("trainingRunCount")}/"
    + $"{policyValueTraining.Model?.Metrics.GetValueOrDefault("validationRunCount")}/"
    + $"{policyValueTraining.Model?.Metrics.GetValueOrDefault("testRunCount")},"
    + $" frames={policyValueTraining.TrainingMetrics.FrameCount}/"
    + $"{policyValueTraining.ValidationMetrics.FrameCount},"
    + $" epochs={policyValueTraining.CompletedEpochs}/{policyValueTraining.EpochHistory.Count}/"
    + $"{recordedEpochMetrics.Count}, candidates={policyValueTraining.CandidateModels.Count},"
    + $" strata={policyValueTraining.FrameStrata.Count},"
    + $" weights={policyValueTraining.MinimumFrameWeight:F3}-"
    + $"{policyValueTraining.MaximumFrameWeight:F3},"
    + $" endFrames={policyValueTraining.EndTurnDecisionFrames},"
    + $" unsafeEndFrames={policyValueTraining.UnsafeEndTurnFrames},"
    + $" temp={policyValueTraining.Model?.PolicyTemperature:F3},"
    + $" calibrated={policyValueTraining.EpochHistory.Count(item => item.Calibrated)},"
    + $" epochEvents={recordedEpochMetrics.Count(item => item.EventKind == "epoch")},"
    + $" calibratedEvents={recordedEpochMetrics.Count(item => item.EventKind == "calibrated")},"
    + $" badEpochs={invalidPolicyEpochs}, badCandidates={invalidPolicyCandidates},"
    + $" ci={policyValueTraining.ValidationMetrics.CompositeLossCiLower:F3}-"
    + $"{policyValueTraining.ValidationMetrics.CompositeLossCiUpper:F3},"
    + $" optimizer={policyValueTraining.Model?.Metrics.GetValueOrDefault("optimizerStep")},"
    + $" failed={failedPolicyValueChecks},"
    + $" protocol={policyValueTraining.FrameStratificationProtocol},"
    + $" valid={policyValueNetworkValid}:{policyValueValidationDiagnostic})");
var originalEpisodeFrames = episodes
    .Select(episode => episode.Frames.ToList())
    .ToList();
for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
{
    while (episodes[episodeIndex].Frames.Count < 20)
    {
        episodes[episodeIndex].Frames.Add(
            episodes[episodeIndex].Frames[0]);
    }
}
var cappedFrameTraining = CombatPolicyValueTrainer.Train(
    episodes,
    "balanced",
    new CombatPolicyValueTrainingOptions
    {
        Epochs = 5,
        MinimumEpisodes = 4,
        MaximumFramesPerEpisode = 8,
        RandomSeed = 18
    });
for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
{
    episodes[episodeIndex].Frames = originalEpisodeFrames[episodeIndex];
}
Assert(cappedFrameTraining.Success
       && cappedFrameTraining.FrameCount == episodes.Count * 8
       && cappedFrameTraining.DroppedFramesByEpisodeCap
          == episodes.Count * 12,
    "frame-balanced training uniformly caps each episode so long opening battles cannot dominate a minibatch");
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
                    if (checkpoint.CompletedEpochs >= 4)
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
           && capturedBatchCheckpoint?.CompletedEpochs == 4
           && capturedBatchCheckpoint.Optimizer?.Step > 0
           && capturedBatchCheckpoint.Optimizer.FirstMoment.Length
              == capturedBatchCheckpoint.Optimizer.SecondMoment.Length
           && batchProgress.Any(progress =>
               progress.Stage == "encoding")
           && batchProgress.Any(progress =>
               progress.Stage == "training"
               && progress.CompletedFrames > 0),
        "batch policy-value training reports frame progress and keeps periodic resumable checkpoints");
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
var policyValueInput = new CombatPolicyValueInput
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
};
var policyValuePrediction = policyValueModel.Evaluate(policyValueInput);
Assert(policyValuePrediction.PolicyLogits.Count
       == firstEpisodeFrame.Candidates.Count(candidate => candidate.Legal)
       && policyValuePrediction.ActionReturnQuantiles.Count
          == policyValuePrediction.PolicyLogits.Count
       && policyValuePrediction.ActionReturnQuantiles.Values.All(values =>
           values.Count == 16
           && values.All(value => value is >= -1d and <= 1d))
       && policyValuePrediction.WinProbability is >= 0d and <= 1d
       && policyValuePrediction.DeathProbability is >= 0d and <= 1d,
    "managed policy-value inference returns masked action logits and calibrated probability ranges");
var trainedQuantileHeadReady = policyValueTraining.Model!.ActionQuantileHeadReady;
policyValueTraining.Model.ActionQuantileHeadReady = false;
var unreadyQuantilePrediction =
    new ManagedCombatPolicyValueModel(policyValueTraining.Model)
        .Evaluate(policyValueInput);
policyValueTraining.Model.ActionQuantileHeadReady = trainedQuantileHeadReady;
Assert(trainedQuantileHeadReady
       && unreadyQuantilePrediction.ActionReturnQuantiles.Count == 0,
    "managed inference withholds randomly initialized action quantiles until supervised labels have trained and validated the head");
var batchPolicyPredictions = policyValueModel.EvaluateBatch(
    new[] { policyValueInput, policyValueInput });
Assert(batchPolicyPredictions.Count == 2
       && batchPolicyPredictions.All(prediction =>
           Math.Abs(
               prediction.ExpectedReturn
               - policyValuePrediction.ExpectedReturn) < 0.000000001d
           && Math.Abs(
               prediction.WinProbability
               - policyValuePrediction.WinProbability) < 0.000000001d
           && prediction.PolicyLogits.Count
              == policyValuePrediction.PolicyLogits.Count
           && prediction.ActionReturnQuantiles.All(pair =>
               pair.Value.Zip(
                       policyValuePrediction.ActionReturnQuantiles[pair.Key],
                       (left, right) => Math.Abs(left - right))
                   .All(delta => delta < 0.000000001d))
           && prediction.PolicyLogits.All(pair =>
               Math.Abs(
                   pair.Value
                   - policyValuePrediction.PolicyLogits[pair.Key])
               < 0.000000001d)),
    "managed policy-value batch inference evaluates a shared state/action matrix with scalar-equivalent outputs");
var encodingBuffer = new double[Math.Max(
    policyValueTraining.Model!.StateDimensions,
    policyValueTraining.Model.ActionDimensions)];
CombatPolicyValueEncoding.EncodeStateInto(
    policyValueInput.StateFeatures,
    encodingBuffer,
    policyValueTraining.Model.StateDimensions,
    policyValueTraining.Model.FeatureEncodingMode);
CombatPolicyValueEncoding.EncodeCandidateInto(
    policyValueInput.Candidates[0],
    encodingBuffer,
    policyValueTraining.Model.ActionDimensions,
    policyValueTraining.Model.FeatureEncodingMode);
var encodingAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
for (var index = 0; index < 512; index++)
{
    CombatPolicyValueEncoding.EncodeStateInto(
        policyValueInput.StateFeatures,
        encodingBuffer,
        policyValueTraining.Model.StateDimensions,
        policyValueTraining.Model.FeatureEncodingMode);
    CombatPolicyValueEncoding.EncodeCandidateInto(
        policyValueInput.Candidates[index % policyValueInput.Candidates.Count],
        encodingBuffer,
        policyValueTraining.Model.ActionDimensions,
        policyValueTraining.Model.FeatureEncodingMode);
}
var encodingAllocatedBytes =
    GC.GetAllocatedBytesForCurrentThread() - encodingAllocationBefore;
Console.WriteLine(
    $"Encoding hot-path allocation: {encodingAllocatedBytes:N0} bytes / 512 state+action pairs");
Assert(encodingAllocatedBytes < 64 * 1024,
    "policy-value encoding keeps steady-state hot-path allocation bounded");
Assert(typeof(CombatLeafEvaluation).IsValueType,
    "leaf inference encoding avoids per-call dictionaries and leaf result objects");
var concurrentBatchModel = new ConcurrentBatchedCombatPolicyValueModel(
    policyValueModel,
    4,
    TimeSpan.FromMilliseconds(50));
var concurrentPredictions = new CombatPolicyValuePrediction?[4];
var concurrentErrors = new Exception?[4];
using (var concurrentBarrier = new Barrier(4))
{
    var inferenceThreads = Enumerable.Range(0, 4)
        .Select(index => new Thread(() =>
        {
            try
            {
                concurrentBarrier.SignalAndWait();
                concurrentPredictions[index] =
                    concurrentBatchModel.Evaluate(policyValueInput);
            }
            catch (Exception exception)
            {
                concurrentErrors[index] = exception;
            }
        }))
        .ToArray();
    foreach (var thread in inferenceThreads)
    {
        thread.Start();
    }
    foreach (var thread in inferenceThreads)
    {
        thread.Join();
    }
}
Assert(concurrentErrors.All(error => error == null)
       && concurrentPredictions.All(prediction =>
           prediction != null
           && Math.Abs(
               prediction.ExpectedReturn
               - policyValuePrediction.ExpectedReturn) < 0.000000001d)
       && concurrentBatchModel.BatchedInputCount == 4
       && concurrentBatchModel.BatchEvaluationCount == 1,
    "parallel campaign inference coalesces synchronous calls into one true model batch");
var shardedBatchModel = new ShardedBatchedCombatPolicyValueModel(
    policyValueModel,
    laneCount: 2,
    maximumBatchSizePerLane: 2,
    coalescingWindow: TimeSpan.FromMilliseconds(20));
var shardedPredictions = new CombatPolicyValuePrediction?[8];
var shardedErrors = new Exception?[8];
using (var shardedBarrier = new Barrier(8))
{
    var shardedThreads = Enumerable.Range(0, 8)
        .Select(index => new Thread(() =>
        {
            try
            {
                shardedBarrier.SignalAndWait();
                shardedPredictions[index] =
                    shardedBatchModel.Evaluate(policyValueInput);
            }
            catch (Exception exception)
            {
                shardedErrors[index] = exception;
            }
        }))
        .ToArray();
    foreach (var thread in shardedThreads)
    {
        thread.Start();
    }
    foreach (var thread in shardedThreads)
    {
        thread.Join();
    }
}
Assert(shardedBatchModel.LaneCount == 2
       && shardedErrors.All(error => error == null)
       && shardedPredictions.All(prediction =>
           prediction != null
           && Math.Abs(
               prediction.ExpectedReturn
               - policyValuePrediction.ExpectedReturn) < 0.000000001d)
       && shardedBatchModel.BatchedInputCount == 8
       && shardedBatchModel.BatchEvaluationCount is >= 2 and <= 8,
    "high campaign parallelism uses independent inference lanes without changing predictions");
var adaptiveBatchModel = new ConcurrentBatchedCombatPolicyValueModel(
    NullCombatPolicyValueModel.Instance,
    maximumBatchSize: 4,
    coalescingWindow: TimeSpan.Zero);
var adaptiveDiagnosticsBefore = CombatPolicyValueBatchDiagnostics.Capture();
for (var index = 0; index < 2050; index++)
{
    _ = adaptiveBatchModel.Evaluate(policyValueInput);
}
var adaptiveDiagnostics = CombatPolicyValueBatchDiagnostics.Capture()
    .DeltaFrom(adaptiveDiagnosticsBefore);
Assert(adaptiveBatchModel.AdaptiveFallbackActive
       && adaptiveDiagnostics.AdaptiveFallbackActivations == 1
       && adaptiveDiagnostics.DirectFallbackRequests > 0,
    "persistently empty inference batches switch to direct execution automatically");
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

var rewardPoolPackRule = new CombatCampaignRewardDefinition
{
    RewardId = "pack-3-card",
    Kind = CombatCampaignRewardKind.Card,
    RewardCardPackId = "cardpack_3",
    CardAcquisition = CombatCampaignCardAcquisition.RewardPool
};
var blankPackRule = new CombatCampaignRewardDefinition
{
    RewardId = "blank-pack-card",
    Kind = CombatCampaignRewardKind.Card,
    CardAcquisition = CombatCampaignCardAcquisition.RewardPool
};
var roleSkillRule = new CombatCampaignRewardDefinition
{
    RewardId = "careercard_test",
    Kind = CombatCampaignRewardKind.Card,
    RewardCardPackId = "cardpack_1",
    CardAcquisition = CombatCampaignCardAcquisition.SkillOnly
};
Assert(!CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
           rewardPoolPackRule,
           new[] { "cardpack_1", "cardpack_2" })
       && CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
           rewardPoolPackRule,
           new[] { "cardpack_1", "cardpack_2", "cardpack_3" })
       && CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
           blankPackRule,
           Array.Empty<string>())
       && !CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
           roleSkillRule,
           new[] { "cardpack_1", "cardpack_2" }),
    "reward packs filter only RewardPool cards, map blank ownership to cardpack_1, and never leak role skills");

var strategyCampaign = new CombatCampaignDefinition
{
    Player = new CombatPlayerSetup
    {
        RoleId = "strategy-role",
        SkillCardIds = { "careercard_test" }
    },
    EnabledRewardCardPackIds =
    {
        "cardpack_1",
        "cardpack_2",
        "cardpack_3"
    },
    TargetDeckSizeMinimum = 1,
    TargetDeckSizeMaximum = 24,
    DeckSizeAlertThreshold = 25
};
var strategyCards = new[]
{
    "cycle-a", "cycle-b", "cycle-c", "cycle-d",
    "cycle-e", "cycle-f", "cycle-g", "cycle-h"
};
for (var index = 0; index < strategyCards.Length; index++)
{
    strategyCampaign.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = strategyCards[index],
        Kind = CombatCampaignRewardKind.Card,
        RewardCardPackId = index == 7 ? "cardpack_18" : "cardpack_3",
        CardAcquisition = CombatCampaignCardAcquisition.RewardPool
    });
    strategyCampaign.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = "strategy-relic-" + index,
        Kind = CombatCampaignRewardKind.Relic
    });
    strategyCampaign.Strategies.Add(new CombatCampaignStrategyDefinition
    {
        StrategyId = "deterministic-strategy-" + index,
        Kind = index < 2
            ? CombatCampaignStrategyKind.Infinite
            : CombatCampaignStrategyKind.Cycle,
        Deterministic = true,
        RequiredCardIds = { strategyCards[index] },
        RequiredRelicIds = { "strategy-relic-" + index },
        MaximumActiveDeckSize = 24,
        RewardCompletionBonus = 4d
    });
}
var strategyLookup = strategyCampaign.Rewards.ToDictionary(
    item => item.RewardId,
    StringComparer.OrdinalIgnoreCase);
var completedStrategyState = new CombatCampaignState
{
    Deck = strategyCards.ToList(),
    Relics = Enumerable.Range(0, 8)
        .Select(index => "strategy-relic-" + index)
        .ToList()
};
var completedStrategies = CombatCampaignStrategyEvaluator.Evaluate(
    strategyCampaign,
    completedStrategyState,
    strategyLookup);
Assert(completedStrategies.Count == 8
       && completedStrategies.All(item =>
           item.Accessible
           && item.Executable
           && Math.Abs(item.Completion - 1d) < 0.0001d),
    "all eight deterministic cycle and infinite strategy presets certify when their active-deck components are present");
strategyCampaign.EnabledRewardCardPackIds.RemoveAll(item =>
    string.Equals(item, "cardpack_18", StringComparison.OrdinalIgnoreCase));
var disabledComponentProgress = CombatCampaignStrategyEvaluator.Evaluate(
        strategyCampaign,
        new CombatCampaignState(),
        strategyLookup)
    .Single(item => item.StrategyId == "deterministic-strategy-7");
Assert(!disabledComponentProgress.Accessible
       && disabledComponentProgress.Completion == 0d,
    "components from disabled reward packs cannot contribute phantom strategy completion");
strategyCampaign.EnabledRewardCardPackIds.Add("cardpack_18");
var partialStrategyState = new CombatCampaignState
{
    Relics = { "strategy-relic-0" }
};
var componentScore = CombatCampaignStrategyEvaluator.MarginalRewardValue(
    strategyCampaign,
    partialStrategyState,
    strategyLookup,
    strategyLookup["cycle-a"]);
Assert(componentScore > 0d,
    "a reward that closes a deterministic strategy receives a positive completion bonus");

var strategyProjectionRulesBuilder =
    new CombatRulesetBuilder("strategy-projection");
strategyProjectionRulesBuilder.RegisterCard(new CombatCardDefinition
{
    OwnerModId = "Tests",
    CardId = "cycle-a",
    Cost = 0,
    Effects =
    {
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.GainBlock,
            Target = CombatSimulationTarget.Self,
            Amount = 1
        }
    }
});
var strategyProjectionRules = strategyProjectionRulesBuilder.Freeze();
var strategyProjectionState = new CombatBattleState
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
            Energy = 1
        }
    },
    Cards =
    {
        new CombatCardInstanceState
        {
            InstanceId = 1,
            CardId = "cycle-a"
        }
    },
    Hand = { 1 }
};
var strategyProjectionAction = new CombatSimulationAction
{
    CandidateId = "card:1",
    Kind = CombatSimulationActionKind.PlayCard,
    ActorId = 1,
    CardInstanceId = 1,
    DefinitionId = "cycle-a"
};
var strategyProjectionPolicy = new CombatDecisionSimulationPolicy(
    new CombatDecisionProfile
    {
        SearchBudgetMode = "fixed",
        SearchSimulationBudget = 1,
        SearchNodeBudget = 8,
        SearchMaxPly = 1,
        SearchMinimumSimulations = 1
    });
strategyProjectionPolicy.SelectAction(new CombatSimulationPolicyContext
{
    Scenario = new CombatScenarioDefinition
    {
        StrategyProgress =
        {
            new CombatScenarioStrategyProgress
            {
                StrategyId = "deterministic-strategy-0",
                Kind = "Infinite",
                Deterministic = true,
                Executable = true,
                Completion = 1d,
                PlayPriority = 1.5d,
                ComponentCardIds = { "cycle-a" }
            }
        }
    },
    Ruleset = strategyProjectionRules.Ruleset,
    State = strategyProjectionState,
    LegalActions = new List<CombatSimulationAction>
    {
        strategyProjectionAction,
        new CombatSimulationAction
        {
            CandidateId = "end-turn",
            Kind = CombatSimulationActionKind.EndTurn,
            ActorId = 1
        }
    }
});
var projectedStrategyAction =
    strategyProjectionPolicy.LastObservation!.Actions.Single(item =>
        item.CandidateId == strategyProjectionAction.CandidateId);
Assert(projectedStrategyAction.Features["synergy"] > 0d
       && projectedStrategyAction.Features["strategyInfinite"] == 1d,
    "completed deterministic strategy progress is projected into card-play ordering features");

CombatDecisionSimulationPolicy frozenPreparationPolicy;
using (CombatAiRegistry.RegisterSemanticProvider(
           "Tests",
           "frozen-preparation-semantic",
           new FrozenPreparationSemanticProvider("cycle-a"),
           10000))
using (CombatAiRegistry.RegisterRoleStrategyProvider(
           "Tests",
           "frozen-preparation-role",
           new FrozenPreparationRoleStrategyProvider(),
           10000))
{
    frozenPreparationPolicy = new CombatDecisionSimulationPolicy(
        new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 1,
            SearchNodeBudget = 8,
            SearchMaxPly = 1,
            SearchMinimumSimulations = 1
        });
}
frozenPreparationPolicy.SelectAction(new CombatSimulationPolicyContext
{
    Scenario = new CombatScenarioDefinition(),
    Ruleset = strategyProjectionRules.Ruleset,
    State = strategyProjectionState.Clone(),
    LegalActions = new List<CombatSimulationAction>
    {
        strategyProjectionAction,
        new CombatSimulationAction
        {
            CandidateId = "end-turn-frozen",
            Kind = CombatSimulationActionKind.EndTurn,
            ActorId = 1
        }
    }
});
var frozenPreparedAction = frozenPreparationPolicy.LastDecision!.Candidates
    .Single(item => item.Action.CandidateId
                    == strategyProjectionAction.CandidateId)
    .Action;
Assert(
    frozenPreparedAction.SemanticFidelity
    == CombatKnowledgeFidelity.Authoritative
    && frozenPreparedAction.Semantics.Buff == 17d
    && frozenPreparedAction.Features[CombatRoleStrategyFeatureNames.Active]
       == 1d,
    "isolated simulation policies retain a frozen semantic and role-strategy snapshot after registry lifetimes end");

var countingSemanticProvider = new CountingSemanticProvider("cycle-a");
using (CombatAiRegistry.RegisterSemanticProvider(
           "Tests",
           "single-pass-decision-semantics",
           countingSemanticProvider,
           20000))
{
    var countingState = CombatPlayerObservationBoundary.Normalize(
        frozenPreparationPolicy.LastObservation!);
    new CombatDecisionEngine().Choose(
        countingState,
        new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 1,
            SearchNodeBudget = 8,
            SearchMaxPly = 1,
            SearchMinimumSimulations = 1
        });
}
Assert(countingSemanticProvider.CallCount == 1,
    "decision preparation applies authoritative semantics once per legal candidate");

var roleSkillRulesBuilder = new CombatRulesetBuilder("role-skill-rules");
roleSkillRulesBuilder.RegisterCard(new CombatCardDefinition
{
    OwnerModId = "Tests",
    CardId = "careercard_test",
    Cost = 9,
    Effects =
    {
        new CombatSimulationEffectDefinition
        {
            Kind = CombatSimulationEffectKind.GainBlock,
            Target = CombatSimulationTarget.Self,
            Amount = 3
        }
    }
});
var roleSkillRules = roleSkillRulesBuilder.Freeze();
var roleSkillState = new CombatBattleState
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
            Energy = 0
        }
    },
    Cards =
    {
        new CombatCardInstanceState
        {
            InstanceId = 1,
            CardId = "careercard_test"
        }
    },
    SkillCards = { 1 },
    SkillCooldowns = { [1] = 0 }
};
var roleSkillScenario = new CombatScenarioDefinition
{
    Player = new CombatPlayerSetup
    {
        SkillCardIds = { "careercard_test" },
        SkillCooldownTurns = { ["careercard_test"] = 5 }
    }
};
var roleSkillEngine = new CombatSimulationEngine();
var legalRoleSkill = roleSkillEngine.GetLegalPlayerActions(
        roleSkillScenario,
        roleSkillRules.Ruleset,
        roleSkillState)
    .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
var appliedRoleSkill = roleSkillEngine.ForkAndApplyPlayerAction(
    roleSkillScenario,
    roleSkillRules.Ruleset,
    roleSkillState,
    legalRoleSkill);
Assert(appliedRoleSkill.Success
       && appliedRoleSkill.State.SkillCards.SequenceEqual(new[] { 1 })
       && appliedRoleSkill.State.Hand.Count == 0
       && appliedRoleSkill.State.DiscardPile.Count == 0
       && appliedRoleSkill.State.Player?.Block == 3
       && appliedRoleSkill.State.Player?.Energy == 0
       && appliedRoleSkill.State.SkillCooldowns[1] == 5
       && appliedRoleSkill.State.SkillActivationCounts.GetValueOrDefault(
           "careercard_test") == 1,
    "role skills remain outside the deck, ignore printed card energy cost, and use role-specific cooldowns");

var projectedRoleSkillState = PlayerEquivalentSimulationObservationProjector.Project(
    new CombatSimulationPolicyContext
    {
        Scenario = roleSkillScenario,
        Ruleset = roleSkillRules.Ruleset,
        State = roleSkillState,
        LegalActions = new List<CombatSimulationAction> { legalRoleSkill }
    });
var projectedRoleSkill = projectedRoleSkillState.Actions.Single();
Assert(projectedRoleSkill.Features.GetValueOrDefault(
           CombatSkillTimingFeatureNames.ResetsEachBattle) == 1d
       && projectedRoleSkill.Features.GetValueOrDefault(
           CombatSkillTimingFeatureNames.CooldownAfterUse) == 5d
       && projectedRoleSkill.Features.GetValueOrDefault(
           CombatSkillTimingFeatureNames.CurrentCooldown) == 0d
       && projectedRoleSkill.Features.GetValueOrDefault(
           CombatSkillTimingFeatureNames.ActivationsThisBattle) == 0d,
    "simulation observations expose generic per-battle role-skill lifecycle features");

var waitForSkill = new CombatActionObservation
{
    SourceId = "timing-skill",
    Kind = CombatActionKind.UseSkill,
    Features =
    {
        [CombatSkillTimingFeatureNames.Active] = 1d,
        [CombatSkillTimingFeatureNames.OngoingEffectValue] = 2d,
        [CombatSkillTimingFeatureNames.DelayGain] = 5d
    }
};
var waitTiming = CombatSkillTimingPolicy.Enrich(waitForSkill);
var waitUtility = CombatDecisionEngine.BuildUtility(
    new CombatStateObservation
    {
        Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 }
    },
    waitForSkill,
    new CombatDecisionProfile());
Assert(waitTiming.Active
       && waitTiming.BetterToWait
       && waitTiming.TimingAdvantage == -3d
       && waitForSkill.Features.GetValueOrDefault(
           CombatSkillTimingFeatureNames.PositiveOpportunity) == 0d
       && waitUtility.Risk >= 3d
       && waitForSkill.Features.GetValueOrDefault(
           CombatRoleStrategyFeatureNames.StrategicallyProhibited) == 0d,
    "generic skill timing can prefer waiting without turning non-use into a hard prohibition");

waitForSkill.Features[CombatSkillTimingFeatureNames.ExpiryRisk] = 6d;
var useNowTiming = CombatSkillTimingPolicy.Enrich(waitForSkill);
var useNowUtility = CombatDecisionEngine.BuildUtility(
    new CombatStateObservation
    {
        Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 }
    },
    waitForSkill,
    new CombatDecisionProfile());
Assert(useNowTiming.PositiveOpportunity
       && useNowTiming.TimingAdvantage == 3d
       && useNowUtility.Coordination >= 3d,
    "generic skill timing favors activation when ongoing value and expiry risk exceed the value of waiting");

var contractRulesBuilder = new CombatRulesetBuilder("action-contract-rules");
contractRulesBuilder.RegisterCard(new CombatCardDefinition
{
    OwnerModId = "Tests",
    CardId = "contract_draw_skill",
    VerificationSource = "Decompiler:test",
    ActionContract = new CombatActionContractDefinition
    {
        Preconditions =
        {
            new CombatActionPreconditionDefinition
            {
                Kind = CombatActionPreconditionKind.DrawPileCountAtLeast,
                Amount = 1
            },
            new CombatActionPreconditionDefinition
            {
                Kind = CombatActionPreconditionKind.AvailableHandSlotsAtLeast,
                Amount = 1
            }
        },
        MinimumCardsMovedFromDrawPileToHandOnApplied = 1
    },
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
contractRulesBuilder.RegisterCard(new CombatCardDefinition
{
    OwnerModId = "Tests",
    CardId = "contract_draw_target"
});
var contractRules = contractRulesBuilder.Freeze();
Assert(contractRules.Success
       && contractRules.Ruleset.TryGetCard(
           "contract_draw_skill",
           out var frozenContractSkill)
       && frozenContractSkill.VerificationSource == "Decompiler:test"
       && frozenContractSkill.ActionContract?.Version
       == CombatActionContractProtocol.Version,
    "action contracts are validated, cloned, and retained by the ruleset");
var invalidContractRules = new CombatRulesetBuilder("invalid-action-contract")
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "invalid_contract_skill",
        ActionContract = new CombatActionContractDefinition
        {
            MinimumCardsMovedFromDrawPileToHandOnApplied = 1
        }
    })
    .Freeze();
Assert(!invalidContractRules.Success
       && invalidContractRules.Errors.Any(item =>
           item.Contains(
               "postcondition lacks matching prerequisites",
               StringComparison.Ordinal)),
    "ruleset validation rejects interactive postconditions without matching prerequisites");
var contractScenario = new CombatScenarioDefinition
{
    HandLimit = 1,
    Player = new CombatPlayerSetup
    {
        SkillCardIds = { "contract_draw_skill" },
        SkillCooldownTurns = { ["contract_draw_skill"] = 1 }
    }
};
CombatBattleState ContractState(bool withDrawCard, bool handFull)
{
    var state = new CombatBattleState
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
                Energy = 0
            }
        },
        Cards =
        {
            new CombatCardInstanceState
            {
                InstanceId = 1,
                CardId = "contract_draw_skill"
            }
        },
        SkillCards = { 1 },
        SkillCooldowns = { [1] = 0 },
        NextCardInstanceId = 2
    };
    if (withDrawCard)
    {
        state.Cards.Add(new CombatCardInstanceState
        {
            InstanceId = 2,
            CardId = "contract_draw_target"
        });
        state.DrawPile.Add(2);
        state.NextCardInstanceId = 3;
    }
    if (handFull)
    {
        state.Cards.Add(new CombatCardInstanceState
        {
            InstanceId = 3,
            CardId = "contract_draw_target"
        });
        state.Hand.Add(3);
        state.NextCardInstanceId = 4;
    }
    return state;
}

var contractEngine = new CombatSimulationEngine();
var emptyDrawState = ContractState(withDrawCard: false, handFull: false);
var invocableEmptyDrawSkill = contractEngine.GetInvocablePlayerActions(
        contractScenario,
        contractRules.Ruleset,
        emptyDrawState)
    .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
Assert(invocableEmptyDrawSkill.GameInvocable
       && !invocableEmptyDrawSkill.PolicyEligible
       && invocableEmptyDrawSkill.ExpectedOutcome
       == CombatActionApplicationOutcome.NoEffect
       && !contractEngine.GetLegalPlayerActions(
               contractScenario,
               contractRules.Ruleset,
               emptyDrawState)
           .Any(item => item.Kind == CombatSimulationActionKind.UseSkill),
    "a guaranteed no-effect skill stays game-invocable but is excluded from policy candidates");
var forcedEmptyDraw = contractEngine.ForkAndApplyPlayerAction(
    contractScenario,
    contractRules.Ruleset,
    emptyDrawState,
    invocableEmptyDrawSkill,
    allowPolicyIneligible: true);
Assert(forcedEmptyDraw.Success
       && forcedEmptyDraw.Outcome == CombatActionApplicationOutcome.NoEffect
       && forcedEmptyDraw.State.ActionSequence == emptyDrawState.ActionSequence
       && forcedEmptyDraw.State.Hand.Count == 0
       && forcedEmptyDraw.State.DrawPile.Count == 0
       && forcedEmptyDraw.State.SkillCooldowns[1] == 0
       && forcedEmptyDraw.State.NoEffectActionAttemptsThisTurn.Values.Single()
       == 1,
    "forced no-effect execution preserves battle state and records controller failure memory");
var suppressedEmptyDrawSkill = contractEngine.GetInvocablePlayerActions(
        contractScenario,
        contractRules.Ruleset,
        forcedEmptyDraw.State)
    .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
var forcedRepeatedNoEffect = contractEngine.ForkAndApplyPlayerAction(
    contractScenario,
    contractRules.Ruleset,
    forcedEmptyDraw.State,
    suppressedEmptyDrawSkill,
    allowPolicyIneligible: true);
Assert(forcedRepeatedNoEffect.Success
       && forcedRepeatedNoEffect.Outcome
       == CombatActionApplicationOutcome.NoEffect
       && forcedRepeatedNoEffect.State.NoEffectActionAttemptsThisTurn
           .Values.Single() == 2,
    "no-effect attempt memory persists for the remainder of the simulated turn");

var fullHandState = ContractState(withDrawCard: true, handFull: true);
var fullHandSkill = contractEngine.GetInvocablePlayerActions(
        contractScenario,
        contractRules.Ruleset,
        fullHandState)
    .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
Assert(!fullHandSkill.PolicyEligible
       && fullHandSkill.ExpectedOutcome
       == CombatActionApplicationOutcome.NoEffect,
    "hand-capacity preconditions exclude a guaranteed no-effect skill");

var applicableContractState =
    ContractState(withDrawCard: true, handFull: false);
var applicableContractSkill = contractEngine.GetLegalPlayerActions(
        contractScenario,
        contractRules.Ruleset,
        applicableContractState)
    .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
var appliedContractSkill = contractEngine.ForkAndApplyPlayerAction(
    contractScenario,
    contractRules.Ruleset,
    applicableContractState,
    applicableContractSkill);
Assert(appliedContractSkill.Success
       && appliedContractSkill.Outcome
       == CombatActionApplicationOutcome.Applied
       && appliedContractSkill.State.DrawPile.Count == 0
       && appliedContractSkill.State.Hand.SequenceEqual(new[] { 2 })
       && appliedContractSkill.State.SkillCooldowns[1] == 1,
    "successful contract execution satisfies its draw-to-hand postcondition before cooldown");

var causalContract = new CombatActionContractDefinition
{
    MinimumCardsMovedFromDrawPileToHandOnApplied = 1
};
var causalBeforeState = new CombatBattleState
{
    DrawPile = { 2 },
    Hand = { 3 }
};
var causalAfterState = new CombatBattleState
{
    DrawPile = { 4, 5 },
    Hand = { 2, 3 }
};
var causalPostconditionPassed =
    CombatActionContractEvaluator.AppliedPostconditionsSatisfied(
        causalContract,
        CombatActionContractSnapshot.Capture(causalBeforeState),
        CombatActionContractSnapshot.Capture(causalAfterState),
        new[]
        {
            new CombatSimulationEvent
            {
                Kind = CombatSimulationEventKind.CardDrawn,
                CardInstanceId = 2,
                SourceActionId = 7
            },
            new CombatSimulationEvent
            {
                Kind = CombatSimulationEventKind.CardCreated,
                CardInstanceId = 4,
                SourceActionId = 7
            }
        },
        7,
        out _);
Assert(causalPostconditionPassed,
    "action-contract-v2 accepts a causally proven draw-to-hand move even when concurrent card creation makes both net zone counts grow");

var familiarRule = new CombatCampaignRewardDefinition
{
    RewardId = "familiar-blessing",
    Kind = CombatCampaignRewardKind.Blessing,
    BlessingAcquisition = CombatCampaignBlessingAcquisition.FamiliarInnate,
    FightScript = "SetStatus(\"Self\");"
};
var familiarRules = CombatCampaignRewardRuleProjector.Build(
    new CombatCampaignDefinition
    {
        Rewards = { familiarRule }
    },
    new CombatCampaignState
    {
        InnateBlessings = { "familiar-blessing" }
    });
Assert(familiarRules.Count == 1
       && familiarRules[0].RewardId == "familiar-blessing"
       && familiarRules[0].Stacks == 1,
    "familiar blessings are projected as innate battle rules without entering ordinary blessing rewards");

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
var phaseTruthRules = new CombatRulesetBuilder("phase-truth-v1")
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "phase-life",
        MaximumStacks = 3,
        DecayAtRoundEnd = false
    })
    .RegisterStatus(new CombatStatusDefinition
    {
        OwnerModId = "Tests",
        StatusId = "buff_rotten",
        MaximumStacks = 1,
        DecayAtRoundEnd = false,
        Triggers =
        {
            new CombatStatusTriggerDefinition
            {
                TriggerId = "rotten-action",
                EventKind = CombatSimulationEventKind.ActionResolved,
                OwnerRelation =
                    CombatStatusTriggerOwnerRelation.EventSource,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.SetBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 0
                    }
                }
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "phase-down",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.AddStatus,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "phase-life",
                Amount = -1
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "phase-up",
        Cost = 0,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.AddStatus,
                Target = CombatSimulationTarget.Self,
                DefinitionId = "phase-life",
                Amount = 1
            }
        }
    })
    .RegisterCard(new CombatCardDefinition
    {
        OwnerModId = "Tests",
        CardId = "rotten-guard",
        Cost = 0,
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
        CardId = "filtered-strike",
        Cost = 0,
        RequiresEnemyTarget = true,
        Effects =
        {
            new CombatSimulationEffectDefinition
            {
                Kind = CombatSimulationEffectKind.Damage,
                Target = CombatSimulationTarget.SelectedEnemy,
                Amount = 10
            }
        }
    })
    .Freeze();
CombatBattleState BuildPhaseTruthState(
    string cardId,
    int phaseStacks = 0)
{
    var state = new CombatBattleState
    {
        PlayerActorId = 1,
        Phase = CombatSimulationPhase.PlayerAction,
        NextActorId = 3,
        NextCardInstanceId = 2,
        Actors =
        {
            new CombatActorState
            {
                ActorId = 1,
                InstanceKey = "player",
                Kind = CombatSimulationActorKind.Player,
                Hp = 20,
                MaxHp = 20,
                Energy = 3,
                BaseEnergy = 3
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
            new CombatCardInstanceState
            {
                InstanceId = 1,
                CardId = cardId
            }
        },
        Hand = { 1 }
    };
    if (phaseStacks > 0)
    {
        state.Player!.Statuses.Add(new CombatStatusState
        {
            StatusId = "phase-life",
            Stacks = phaseStacks
        });
    }
    return state;
}
var phaseTruthScenario = new CombatScenarioDefinition
{
    ScenarioId = "phase-truth",
    RulesetVersion = "phase-truth-v1",
    Player = new CombatPlayerSetup
    {
        RoleId = "Tests",
        MaxHp = 20,
        CurrentHp = 20,
        Deck = { "phase-down" }
    },
    Enemies = { new CombatEnemySetup { EnemyId = "unused" } }
};
var phaseDecrement = new CombatSimulationEngine().ForkAndApplyPlayerAction(
    phaseTruthScenario,
    phaseTruthRules.Ruleset,
    BuildPhaseTruthState("phase-down", 3),
    new CombatSimulationAction
    {
        CandidateId = "card:1",
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 1,
        DefinitionId = "phase-down"
    });
var phaseRemoval = new CombatSimulationEngine().ForkAndApplyPlayerAction(
    phaseTruthScenario,
    phaseTruthRules.Ruleset,
    BuildPhaseTruthState("phase-down", 1),
    new CombatSimulationAction
    {
        CandidateId = "card:1",
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 1,
        DefinitionId = "phase-down"
    });
var phaseAtMaximum = new CombatSimulationEngine().ForkAndApplyPlayerAction(
    phaseTruthScenario,
    phaseTruthRules.Ruleset,
    BuildPhaseTruthState("phase-up", 3),
    new CombatSimulationAction
    {
        CandidateId = "card:1",
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 1,
        DefinitionId = "phase-up"
    });
var rottenState = BuildPhaseTruthState("rotten-guard");
rottenState.Player!.Statuses.Add(new CombatStatusState
{
    StatusId = "buff_rotten",
    Stacks = 1
});
var rottenGuard = new CombatSimulationEngine().ForkAndApplyPlayerAction(
    phaseTruthScenario,
    phaseTruthRules.Ruleset,
    rottenState,
    new CombatSimulationAction
    {
        CandidateId = "card:1",
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 1,
        DefinitionId = "rotten-guard"
    });
var filteredState = BuildPhaseTruthState("filtered-strike");
filteredState.FindActor(2)!.Variables["DamageFilter.Normal"] = 60d;
var filteredStrike = new CombatSimulationEngine().ForkAndApplyPlayerAction(
    phaseTruthScenario,
    phaseTruthRules.Ruleset,
    filteredState,
    new CombatSimulationAction
    {
        CandidateId = "card:1:target:2",
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 1,
        TargetActorId = 2,
        DefinitionId = "filtered-strike"
    });
var phaseLockedState = BuildPhaseTruthState("filtered-strike");
phaseLockedState.FindActor(2)!.Variables["MaxChangeHp"] = 0d;
var phaseLockedStrike = new CombatSimulationEngine().ForkAndApplyPlayerAction(
    phaseTruthScenario,
    phaseTruthRules.Ruleset,
    phaseLockedState,
    new CombatSimulationAction
    {
        CandidateId = "card:1:target:2",
        Kind = CombatSimulationActionKind.PlayCard,
        ActorId = 1,
        CardInstanceId = 1,
        TargetActorId = 2,
        DefinitionId = "filtered-strike"
    });
var rottenProjection = CombatSemanticAuditor.ProjectEffective(
    rottenState,
    new CombatSimulationAction
    {
        ActorId = 1,
        CardInstanceId = 1,
        DefinitionId = "rotten-guard"
    },
    new CombatActionSemantics { Defend = 5d });
var filteredProjection = CombatSemanticAuditor.ProjectEffective(
    filteredState,
    new CombatSimulationAction
    {
        ActorId = 1,
        CardInstanceId = 1,
        TargetActorId = 2,
        DefinitionId = "filtered-strike"
    },
    new CombatActionSemantics { Damage = 10d });
Assert(phaseDecrement.Success
       && phaseDecrement.State.Player!.Statuses.Single(
           item => item.StatusId == "phase-life").Stacks == 2
       && phaseRemoval.Success
       && phaseRemoval.State.Player!.Statuses.All(
           item => item.StatusId != "phase-life")
       && phaseRemoval.Events.Any(item =>
           item.Kind == CombatSimulationEventKind.StatusRemoved
           && item.DefinitionId == "phase-life")
       && phaseAtMaximum.Success
       && phaseAtMaximum.State.Player!.Statuses.Single(
           item => item.StatusId == "phase-life").Stacks == 3
       && phaseAtMaximum.Events.All(item =>
           item.Kind != CombatSimulationEventKind.StatusAdded
           || item.DefinitionId != "phase-life")
       && rottenGuard.Success
       && rottenGuard.State.Player!.Block == 0
       && filteredStrike.Success
       && filteredStrike.State.FindActor(2)!.Hp == 16
       && phaseLockedStrike.Success
       && phaseLockedStrike.State.FindActor(2)!.Hp == 20
       && rottenProjection.IntrinsicDefend == 5d
       && rottenProjection.Defend == 0d
       && filteredProjection.Damage == 4d,
    "signed status deltas consume phase lives, capped statuses emit no false level change, corrosion clears newly gained block after the action, and typed filters plus phase limits constrain authoritative damage");
var unsafeFinaleState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        CurrentHp = 100,
        MaxHp = 100
    },
    CurrentPower = 5,
    MaxPower = 5,
    HandCount = 5,
    HandCardIds =
    {
        "Crowdfundingcard_43",
        "engine_a",
        "engine_b",
        "engine_c",
        "engine_d"
    },
    DeckCardIds =
    {
        "engine_a",
        "engine_b",
        "engine_c",
        "engine_d",
        "engine_e",
        "engine_f"
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "finale",
            SourceId = "Crowdfundingcard_43",
            Cost = 3
        }
    }
};
CombatArchetypePolicy.Enrich(unsafeFinaleState);
var unsafeFinaleLegal = CombatArchetypePolicy.IsLegal(
    unsafeFinaleState,
    unsafeFinaleState.Actions[0],
    out var unsafeFinaleReason);
var protectedFinaleState =
    CombatPlayerObservationBoundary.Normalize(unsafeFinaleState);
protectedFinaleState.Player.Statuses.Add(new CombatStatusObservation
{
    StatusId = "buff_chrysalis",
    Level = 1
});
CombatArchetypePolicy.Enrich(protectedFinaleState);
var protectedFinaleLegal = CombatArchetypePolicy.IsLegal(
    protectedFinaleState,
    protectedFinaleState.Actions[0],
    out _);
var rottenPolicyState = new CombatStateObservation
{
    Player = new CombatUnitObservation
    {
        CurrentHp = 20,
        MaxHp = 20,
        Statuses =
        {
            new CombatStatusObservation
            {
                StatusId = "buff_rotten",
                Level = 1
            }
        }
    }
};
var rottenPolicyLegal = CombatArchetypePolicy.IsLegal(
    rottenPolicyState,
    new CombatActionObservation
    {
        SourceId = "card_2",
        Semantics = new CombatActionSemantics { Defend = 5d }
    },
    out var rottenPolicyReason);
Assert(!unsafeFinaleLegal
       && unsafeFinaleReason.Contains("Solar", StringComparison.Ordinal)
       && protectedFinaleLegal
       && !rottenPolicyLegal
       && rottenPolicyReason.Contains("corrosion", StringComparison.Ordinal),
    "high-risk starter and corrosion legality gates reject unsupported Finale and block-only actions while admitting a protected coherent launch");
var concentratedDeckDefinition = new CombatCampaignDefinition
{
    CampaignId = "deck-concentration",
    TargetDeckSizeMinimum = 15,
    TargetDeckSizeMaximum = 21,
    Player = new CombatPlayerSetup
    {
        Deck =
        {
            "card_1", "card_1", "card_1",
            "card_2", "card_2", "card_2", "card_2"
        }
    }
};
var reserveAcquisitionDefinition = new CombatCampaignDefinition
{
    AllowSkipCardReward = false,
    TargetDeckSizeMinimum = 1,
    TargetDeckSizeMaximum = 2,
    Rewards =
    {
        new CombatCampaignRewardDefinition
        {
            RewardId = "reward-to-reserve",
            Kind = CombatCampaignRewardKind.Card,
            RewardCardPackId = "cardpack_1",
            BaseValue = 5d
        }
    }
};
var reserveAcquisitionState = new CombatCampaignState
{
    Deck = { "fixed-starter" },
    AttributeUpperBounds =
    {
        ["Strength"] = 40,
        ["Lucky"] = 20,
        ["Perceive"] = 20,
        ["Wisdom"] = 39
    }
};
var reserveAcquisitionDecision = CombatCampaignRewardSelector.Apply(
    reserveAcquisitionDefinition,
    new CombatCampaignPlannedEncounter
    {
        EncounterId = "normal-reward",
        EndsLayer = false,
        RewardOffer = new CombatCampaignRewardOffer
        {
            CardRounds =
            {
                new List<string> { "reward-to-reserve" }
            }
        }
    },
    reserveAcquisitionState);
Assert(reserveAcquisitionDecision.Cards.Single().SelectedId
       == "reward-to-reserve"
       && reserveAcquisitionState.Deck.SequenceEqual(
           new[] { "fixed-starter" })
       && reserveAcquisitionState.ReserveCards.SequenceEqual(
           new[] { "reward-to-reserve" })
       && !reserveAcquisitionDecision.DeckAdjustment.Applied,
    "card rewards enter reserve without mutating the fixed active deck before a layer-end adjustment");
var concentratedDeckState = new CombatCampaignState
{
    CurrentLayer = 3,
    CurrentHp = 80,
    MaxHp = 100,
    Deck =
    {
        "card_1", "card_1", "card_1",
        "card_2", "card_2", "card_2", "card_2",
        "engine_1", "engine_2", "engine_3", "engine_4",
        "engine_5", "engine_6", "engine_7", "engine_8",
        "engine_9", "engine_10", "engine_11", "engine_12",
        "engine_13", "engine_14", "engine_15"
    }
};
foreach (var cardId in concentratedDeckState.Deck.Distinct(
             StringComparer.OrdinalIgnoreCase))
{
    concentratedDeckDefinition.Rewards.Add(new CombatCampaignRewardDefinition
    {
        RewardId = cardId,
        Kind = CombatCampaignRewardKind.Card,
        RewardCardPackId = "cardpack_1",
        BaseValue = cardId.StartsWith("engine_", StringComparison.Ordinal)
            ? 5d
            : 0d
    });
}
var concentratedDeckDecision = CombatCampaignRewardSelector.Apply(
    concentratedDeckDefinition,
    new CombatCampaignPlannedEncounter
    {
        EncounterId = "layer-3-end",
        LayerNumber = 3,
        EndsLayer = true
    },
    concentratedDeckState);
Assert(concentratedDeckDecision.RemovedCardIds.Count == 0
       && concentratedDeckDecision.DeckAdjustment.Applied
       && concentratedDeckDecision.DeckAdjustment.MovedToReserveIds.Count >= 1
       && concentratedDeckDecision.DeckAdjustment.MovedToReserveIds.All(id =>
           id == "card_1" || id == "card_2")
       && concentratedDeckState.Deck.Count is >= 15 and <= 21
       && concentratedDeckState.Deck.Count
          + concentratedDeckState.ReserveCards.Count == 22,
    "layer-end adjustment concentrates the active deck within its tendency interval, moves weak base cards to reserve, and does not delete ownership");
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
var publicCampaignContext = new Dictionary<string, double>(
    StringComparer.OrdinalIgnoreCase);
CombatCampaignContextFeatureNames.ProjectScenario(
    new CombatScenarioDefinition
    {
        Player = new CombatPlayerSetup
        {
            Variables =
            {
                [CombatCampaignPublicContextKeys.BattleIndex] = 8d,
                [CombatCampaignPublicContextKeys.TotalBattles] = 37d,
                [CombatCampaignPublicContextKeys.RemainingBattles] = 28d,
                [CombatCampaignPublicContextKeys.Progress] = 8d / 36d,
                [CombatCampaignPublicContextKeys.LayerNumber] = 2d,
                [CombatCampaignPublicContextKeys.TotalLayers] = 4d,
                [CombatCampaignPublicContextKeys.EncounterKind] = 1d,
                [CombatCampaignPublicContextKeys.GameLevel] = 9d,
                [CombatCampaignPublicContextKeys.FinalBoss] = 0d
            }
        }
    },
    publicCampaignContext);
Assert(publicCampaignContext.GetValueOrDefault(
           CombatCampaignContextFeatureNames.ContextKnown) == 1d
       && publicCampaignContext.GetValueOrDefault(
           CombatCampaignContextFeatureNames.BattleIndex) == 8d
       && publicCampaignContext.GetValueOrDefault(
           CombatCampaignContextFeatureNames.Progress) == 8d / 36d
       && publicCampaignContext.GetValueOrDefault(
           CombatCampaignContextFeatureNames.FinalBoss) == 0d,
    "campaign context projection exposes current public progress without future encounter or reward data");
var roleSpecificBuildDefinition = new CombatCampaignDefinition
{
    RolePrior = { ["doom-growth"] = 1d },
    BuildTendency = { ["doom-growth"] = 0.5d },
    Rewards =
    {
        new CombatCampaignRewardDefinition
        {
            RewardId = "doom-growth-card",
            Kind = CombatCampaignRewardKind.Card,
            Features = { ["doom-growth"] = 1d }
        }
    }
};
var roleSpecificPlan = CombatCampaignRewardSelector.RefreshBuildPlan(
    roleSpecificBuildDefinition,
    new CombatCampaignState
    {
        CurrentLayer = 1,
        Deck = { "doom-growth-card" }
    });
Assert(roleSpecificPlan.PrimaryArchetype == "doom-growth",
    "campaign build planning accepts role-owned archetypes declared through public campaign priors");
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
           item.BuildPlan.TargetDeckSizeMaximum
           == campaign.TargetDeckSizeMaximum),
    "campaign runner carries full state, applies the configured deck-size tendency, and records the build plan separately from battle policy");
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
localEncounterCheckpoint.State.Blessings.RemoveAll(item =>
    string.Equals(
        item,
        "blessing_105",
        StringComparison.OrdinalIgnoreCase));
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
       && localEncounterResult.Checkpoint.NextEncounterIndex == 11
       && localEncounterResult.FinalState.Blessings.Contains(
           "blessing_105",
           StringComparer.OrdinalIgnoreCase),
    "campaign resume repairs origin threshold blessings before replaying one failed encounter");
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
var advancedFloorPlan = CombatFoundationCurriculum.Evaluate(
    true,
    iteration: 0,
    normalWins: 0,
    normalTrials: 0,
    advancedWins: 0,
    advancedTrials: 0);
CombatCampaignFoundationTrainer.ApplyAdvancedTrainingFloor(
    advancedFloorPlan,
    0.35d);
Assert(curriculumOpening.Count(item => item == "advanced") == 0
       && Math.Abs(advancedFloorPlan.AdvancedShare - 0.35d) < 0.000001d
       && Math.Abs(advancedFloorPlan.MinimumAdvancedShare - 0.35d)
          < 0.000001d
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
    SearchBudgetContext = "deployment",
    SearchTimeBudgetMilliseconds = 125
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
var resourceCycleBudget = CombatSearchBudgetPolicy.Resolve(
    budgetState,
    new[]
    {
        BudgetCandidate(
            "resource-cycle",
            new CombatActionSemantics
            {
                Draw = 1,
                EnergyGain = 1,
                CardGeneration = 1
            }),
        BudgetCandidate("ordinary-1"),
        BudgetCandidate("ordinary-2"),
        BudgetCandidate("ordinary-3"),
        BudgetCandidate("ordinary-4")
    },
    budgetProfile);
Assert(forcedBudget.Tier == "forced"
       && forcedBudget.SimulationBudget == 1
       && simpleBudget.Tier == "simple"
       && simpleBudget.SimulationBudget == 96
       && normalBudget.Tier == "normal"
       && normalBudget.SimulationBudget == 128
       && normalBudget.TimeBudgetMilliseconds == 125
       && difficultBudget.Tier == "difficult"
       && difficultBudget.SimulationBudget == 192
       && fakeLoopBudget.Tier == "complex"
       && fakeLoopBudget.SimulationBudget == 256
       && resourceCycleBudget.Tier == "normal"
       && fakeLoopBudget.MaxPly == 16,
    "deployment search caps latency, reserves bounded deep budgets for true loop risk, and does not overclassify ordinary resource generation");
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
       && replaySelectionFixture.AdvancedDefeatEpisodes == 1
       && Math.Abs(
           replaySelectionFixture.TargetAdvancedDefeatShare - 0.25d)
          < 0.0001d
       && replaySelectionFixture.SuccessfulEpisodes > 0
       && replaySelectionFixture.QuotaShortfalls.TryGetValue(
           "advanced:defeat",
           out var advancedDefeatShortfall)
       && advancedDefeatShortfall == 1,
    "foundation replay stratification preserves the advanced quota, reports scarcity, and never silently backfills it with normal episodes");
var failedAdvancedJourneyStratum =
    CombatPolicyValueBatchTrainer.FrameStratum(
        new CombatEpisode
        {
            JourneyBattleIndex = 12,
            Campaign = new CombatCampaignEpisodeMetadata
            {
                DifficultyId = "advanced",
                FinalBossVictory = false,
                OutcomeClass = "battle-victory"
            }
        },
        critical: true);
var successfulHardEncounterStratum =
    CombatPolicyValueBatchTrainer.FrameStratum(
        new CombatEpisode
        {
            JourneyBattleIndex = 3,
            Campaign = new CombatCampaignEpisodeMetadata
            {
                DifficultyId = "advanced",
                FinalBossVictory = false,
                OutcomeClass = "encounter-victory"
            }
        },
        critical: false);
Assert(failedAdvancedJourneyStratum
           == "advanced:middle:defeat:critical"
       && successfulHardEncounterStratum
          == "advanced:opening:victory:regular",
    "frame stratification labels every stage of a failed journey as defeat while preserving local hard-encounter victories");
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
var concentratedPolicyTargets =
    CombatPolicyValueBatchTrainer.PolicyTargets(
        new[]
        {
            new CombatEpisodeCandidate
            {
                CandidateId = "dominant",
                SearchVisits = 100
            },
            new CombatEpisodeCandidate
            {
                CandidateId = "alternative",
                SearchVisits = 0
            }
        },
        "dominant",
        temperature: 1.25d,
        maximumProbability: 0.80d);
Assert(Math.Abs(concentratedPolicyTargets.Sum() - 1d) < 0.000001d
       && concentratedPolicyTargets.Max() <= 0.800001d
       && concentratedPolicyTargets.Min() >= 0.199999d,
    "policy target temperature and cap preserve probability mass without one-hot collapse");
var teacherCandidates = new[]
{
    new CombatEpisodeCandidate
    {
        CandidateId = "dominant",
        TransformerTeacherProbability = 0.10d
    },
    new CombatEpisodeCandidate
    {
        CandidateId = "alternative",
        TransformerTeacherProbability = 0.90d
    }
};
var distilledPolicyTargets = concentratedPolicyTargets.ToArray();
Assert(CombatPolicyValueBatchTrainer.BlendTransformerTeacherTargets(
           distilledPolicyTargets,
           teacherCandidates,
           weight: 0.50d,
           maximumProbability: 0.95d)
       && Math.Abs(distilledPolicyTargets.Sum() - 1d) < 0.000001d
       && Math.Abs(distilledPolicyTargets[0] - 0.45d) < 0.000001d
       && Math.Abs(distilledPolicyTargets[1] - 0.55d) < 0.000001d,
    "Transformer teacher probabilities distill into bounded tactical policy targets without replacing search supervision");
teacherCandidates[1].TransformerTeacherProbability = -1d;
var rejectedTeacherTargets = concentratedPolicyTargets.ToArray();
Assert(!CombatPolicyValueBatchTrainer.BlendTransformerTeacherTargets(
           rejectedTeacherTargets,
           teacherCandidates,
           weight: 0.50d,
           maximumProbability: 0.95d)
       && rejectedTeacherTargets.SequenceEqual(concentratedPolicyTargets),
    "incomplete Transformer annotations are rejected instead of partially corrupting a policy target");
var normalizedTeacherOptions = new CombatTransformerTeacherOptions
{
    Backend = "CUDA",
    HiddenDimensions = 65,
    AttentionHeads = 8,
    DistillationWeight = 4d
}.Normalized();
Assert(normalizedTeacherOptions.Backend
           == CombatTransformerTeacherBackendNames.Cuda
       && normalizedTeacherOptions.HiddenDimensions
          % normalizedTeacherOptions.AttentionHeads == 0
       && normalizedTeacherOptions.DistillationWeight == 0.75d,
    "Transformer teacher settings normalize portable CPU/CUDA configuration and attention dimensions");
var illegalExecutedFrame = new CombatEpisodeFrame
{
    ExecutedCandidateId = "prohibited",
    Candidates =
    {
        new CombatEpisodeCandidate
        {
            CandidateId = "safe",
            Legal = true,
            SearchVisits = 0
        },
        new CombatEpisodeCandidate
        {
            CandidateId = "prohibited",
            Legal = false,
            SearchVisits = 100,
            Features =
            {
                [CombatRoleStrategyFeatureNames.StrategicallyProhibited] = 1d
            }
        }
    }
};
Assert(!CombatPolicyValueBatchTrainer.PolicyIntegrityValidForTraining(
           illegalExecutedFrame),
    "batch training rejects frames whose executed action is outside the decision-legal policy set");
var dominatedEndTurnTargets = new[] { 0.25d, 0.75d };
CombatPolicyValueBatchTrainer.SuppressPolicyTarget(
    dominatedEndTurnTargets,
    1);
Assert(Math.Abs(dominatedEndTurnTargets[0] - 1d) < 0.000001d
       && dominatedEndTurnTargets[1] == 0d,
    "counterfactual policy targets assign zero mass to a deterministically dominated end turn");
var endTurnTrainingEpisodes = Enumerable.Range(0, 4)
    .Select(index => new CombatEpisode
    {
        EpisodeId = "end-turn-specialist-" + index,
        JourneyRunId = "end-turn-specialist:" + index,
        JourneyBattleIndex = index,
        Authoritative = true,
        DecisionProfile = "balanced",
        Campaign = new CombatCampaignEpisodeMetadata
        {
            DifficultyId = index % 2 == 0 ? "normal" : "advanced",
            OutcomeClass = index % 2 == 0 ? "defeat" : "victory",
            FinalBossVictory = index % 2 != 0
        },
        Frames =
        {
            new CombatEpisodeFrame
            {
                ExecutedCandidateId = index == 0 ? "end" : "play",
                StateFeatures =
                {
                    ["power"] = 2d
                },
                Candidates =
                {
                    new CombatEpisodeCandidate
                    {
                        CandidateId = "play",
                        SourceId = "card:test",
                        Legal = true,
                        SearchVisits = 7
                    },
                    new CombatEpisodeCandidate
                    {
                        CandidateId = "end",
                        SourceId = "simulation:end-turn",
                        Legal = index != 1,
                        SearchVisits = 3,
                        Features = index == 1
                            ? new Dictionary<string, double>
                            {
                                [CombatTurnFeatureNames.EndTurnDominated] = 1d
                            }
                            : new Dictionary<string, double>()
                    }
                }
            }
        }
    })
    .ToList();
var endTurnSpecialistTraining = CombatPolicyValueTrainer.Train(
    endTurnTrainingEpisodes,
    "balanced",
    new CombatPolicyValueTrainingOptions
    {
        Epochs = 5,
        MinimumEpisodes = 2,
        StateDimensions = 16,
        ActionDimensions = 16,
        HiddenDimensions = 8,
        EnableEndTurnSpecialization = true,
        EndTurnFrameWeight = 2d,
        MaximumDegreeOfParallelism = 1
    });
Assert(endTurnSpecialistTraining.Success
       && endTurnSpecialistTraining.EndTurnDecisionFrames == 4
       && endTurnSpecialistTraining.UnsafeEndTurnFrames == 2
       && endTurnSpecialistTraining.FrameStrata.Keys.Any(key =>
           key.EndsWith(
               ":unsafe-end-turn",
               StringComparison.Ordinal)),
    "end-turn specialist identifies discretionary and unsafe end turns as dedicated weighted strata");
var imbalancedEndTurnEpisodes = Enumerable.Range(0, 100)
    .Select(index =>
    {
        var template = endTurnTrainingEpisodes[index < 90 ? 0 : 2];
        var clone = JsonSerializer.Deserialize<CombatEpisode>(
            JsonSerializer.Serialize(template))!;
        clone.EpisodeId = "end-turn-cap-" + index;
        clone.JourneyRunId = "end-turn-cap:" + index;
        return clone;
    })
    .ToList();
var balancedEndTurnTraining = CombatPolicyValueTrainer.Train(
    imbalancedEndTurnEpisodes,
    "balanced",
    new CombatPolicyValueTrainingOptions
    {
        Epochs = 5,
        MinimumEpisodes = 2,
        StateDimensions = 16,
        ActionDimensions = 16,
        HiddenDimensions = 8,
        MaximumUnsafeEndTurnFrameShare = 0.35d,
        MaximumDegreeOfParallelism = 1
    });
Assert(balancedEndTurnTraining.Success
       && balancedEndTurnTraining.DroppedUnsafeEndTurnFrames > 0
       && balancedEndTurnTraining.TrainingFrameCount > 0
       && (double)balancedEndTurnTraining.UnsafeEndTurnFrames
          / balancedEndTurnTraining.TrainingFrameCount <= 0.351d,
    "large imbalanced training sets deterministically cap unsafe end-turn frames at the configured share");
var priorityReplayFixture = Enumerable.Range(0, 10)
    .Select(index => new CombatEpisode
    {
        EpisodeId = "priority-" + index,
        JourneyRunId = "priority:normal:" + index,
        JourneyBattleIndex = index,
        Campaign = new CombatCampaignEpisodeMetadata
        {
            DifficultyId = "normal",
            OutcomeClass = "defeat",
            FailureBattleIndex = index == 9 ? index : 36
        },
        Frames =
        {
            new CombatEpisodeFrame
            {
                LongTermReturn = index == 9 ? -1d : 0d,
                ExecutedCandidateId = "play",
                Candidates =
                {
                    new CombatEpisodeCandidate
                    {
                        CandidateId = "play",
                        SourceId = "card:test",
                        Legal = true,
                        SearchVisits = 8,
                        SearchValue = index == 9 ? 1d : 0d,
                        SearchDeathRisk = index == 9 ? 1d : 0d
                    },
                    new CombatEpisodeCandidate
                    {
                        CandidateId = "end",
                        SourceId = "simulation:end-turn",
                        Legal = true,
                        SearchVisits = 2
                    }
                }
            }
        }
    })
    .ToList();
var priorityReplay = CombatFoundationReplaySampler.Select(
    priorityReplayFixture,
    4,
    enabled: true,
    balance: new CombatFoundationReplayBalanceOptions
    {
        EnablePrioritySampling = true
    });
Assert(priorityReplay.Episodes.Any(item => item.EpisodeId == "priority-9")
       && priorityReplay.SelectedPriorityMean
          >= priorityReplay.SourcePriorityMean
       && priorityReplay.SelectedHighPriorityEpisodes > 0,
    "prioritized replay retains high-error, high-risk end-turn decisions before low-information recent episodes");
Assert(
    CombatCampaignFoundationTrainer.RequiredWilsonVictories(200, 0.80d)
    > 160,
    "final validation derives victory requirements from the Wilson lower bound instead of the point estimate");

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
    "case-campaign-fingerprint",
    "case-native-package",
    CombatFoundationTrainingProtocol.TrainingPolicyVersion,
    "balanced",
    "model-success",
    new[] { caseEpisode });
var failedObservation = CombatFoundationCaseLearning.Observe(
    failedCaseCampaign,
    "arena",
    1,
    "champion",
    "case-rules",
    "case-campaign-fingerprint",
    "case-native-package",
    CombatFoundationTrainingProtocol.TrainingPolicyVersion,
    "balanced",
    "model-failure");
var policyInvalidCaseEpisode = new CombatEpisode
{
    EpisodeId = "case-policy-invalid-episode",
    RulesetHash = "case-rules",
    Authoritative = true,
    SemanticCoverage = 1d,
    Campaign = new CombatCampaignEpisodeMetadata
    {
        FinalBossVictory = true,
        IntegrityValid = true,
        DifficultyId = "normal",
        OutcomeClass = "victory"
    },
    Frames = { illegalExecutedFrame }
};
var policyInvalidObservation = CombatFoundationCaseLearning.Observe(
    successfulCaseCampaign,
    "arena",
    1,
    "candidate",
    "case-rules",
    "case-campaign-fingerprint",
    "case-native-package",
    CombatFoundationTrainingProtocol.TrainingPolicyVersion,
    "balanced",
    "model-policy-invalid",
    new[] { policyInvalidCaseEpisode });
var caseAnalysis = CombatFoundationCaseLearning.Analyze(
    new[] { successfulObservation, failedObservation });
Assert(successfulObservation.ArchiveEligible
       && successfulObservation.PolicyIntegrityValid
       && !policyInvalidObservation.PolicyIntegrityValid
       && !policyInvalidObservation.ArchiveEligible
       && successfulObservation.RobustnessScore > 0d
       && caseAnalysis.SuccessfulCases == 1
       && caseAnalysis.FailedCases == 1
       && caseAnalysis.MatchedPairs == 1
       && caseAnalysis.Pairs[0].SuccessSeed
       == caseAnalysis.Pairs[0].FailureSeed,
    "foundation success learning archives only policy-valid authoritative wins and builds same-seed comparisons");
var archivedCase = CombatFoundationCaseLearning.CreateSuccessCase(
    successfulCaseCampaign,
    successfulObservation,
    new[] { caseEpisode });
var compatibleExpertEpisodes =
    CombatFoundationCaseLearning.SelectExpertEpisodes(
        new[] { archivedCase },
        "case-learning",
        "1",
        "case-campaign-fingerprint",
        "case-rules",
        "case-native-package",
        CombatFoundationTrainingProtocol.TrainingPolicyVersion,
        8);
var incompatibleExpertEpisodes =
    CombatFoundationCaseLearning.SelectExpertEpisodes(
        new[] { archivedCase },
        "case-learning",
        "1",
        "case-campaign-fingerprint",
        "different-rules",
        "case-native-package",
        CombatFoundationTrainingProtocol.TrainingPolicyVersion,
        8);
Assert(compatibleExpertEpisodes.Count == 1
       && incompatibleExpertEpisodes.Count == 0
       && CombatFoundationCaseLearning.CompatibilityKey(
           "case-learning",
           "1",
           "case-campaign-fingerprint",
           "case-rules",
           "case-native-package",
           CombatFoundationTrainingProtocol.TrainingPolicyVersion)
       == successfulObservation.CompatibilityKey,
    "foundation expert replay is bounded and isolated by campaign, ruleset and feature protocol");
var stratifiedCompatibilityKey =
    CombatFoundationCaseLearning.CompatibilityKey(
        "case-learning",
        "1",
        "case-campaign-fingerprint",
        "case-rules",
        "case-native-package",
        CombatFoundationTrainingProtocol.TrainingPolicyVersion);
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
                PolicyIntegrityValid = true,
                CampaignId = "case-learning",
                CampaignVersion = "1",
                RulesetHash = "case-rules",
                CompatibilityKey = stratifiedCompatibilityKey,
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
        "case-campaign-fingerprint",
        "case-rules",
        "case-native-package",
        CombatFoundationTrainingProtocol.TrainingPolicyVersion,
        episodeLimit: 16,
        targetAdvancedShare: 0.35d,
        maximumEpisodesPerRun: 2);
Assert(stratifiedExpertSelection.Episodes.Count == 16
       && stratifiedExpertSelection.SelectedAdvancedEpisodes == 4
       && stratifiedExpertSelection.SelectedNormalEpisodes == 12
       && stratifiedExpertSelection.DistinctRuns == 8
       && stratifiedExpertSelection.QuotaShortfalls["advanced"] == 2
       && !stratifiedExpertSelection.QuotaShortfalls.ContainsKey("normal"),
    "expert replay preserves normal evidence and fills unused capacity while reporting scarce advanced success cases");
Assert(CombatCampaignFoundationTrainer.EffectiveAdvancedTrainingFloor(
           0.35d,
           stratifiedExpertSelection) > 0.35d,
    "advanced expert replay shortages raise the next training curriculum floor instead of being silently replaced by normal episodes");
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
var weightedHardSeedHistory = Enumerable.Range(0, 10)
    .Select(index => new CombatFoundationHardSeedHistoryEntry
    {
        WorldSeed = (ulong)(60_000 + index),
        DifficultyId = "normal",
        TerminalScenarioId = index < 5
            ? "campaign:5:level_10011"
            : index < 8
                ? "campaign:36:final-boss-" + index
                : index == 8
                    ? "campaign:5:level_10040"
                    : "campaign:5:other",
        FailureOccurrences = 1,
        FirstSeenIteration = 1,
        LastSeenIteration = 1
    })
    .ToList();
var weightedHardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
    weightedHardSeedHistory,
    campaignCount: 20,
    replayShare: 0.5d,
    iteration: 2,
    runSeed: 321UL,
    enabled: true,
    encounterWeights: new Dictionary<string, double>
    {
        ["level_10011"] = 0.50d,
        ["@final-boss"] = 0.30d,
        ["level_10040"] = 0.10d,
        ["@other"] = 0.10d
    });
Assert(weightedHardSeedPlan.SourceCategories["target:level_10011"] == 5
       && weightedHardSeedPlan.SourceCategories["target:@final-boss"] == 3
       && weightedHardSeedPlan.SourceCategories["target:level_10040"] == 1
       && weightedHardSeedPlan.SourceCategories["target:@other"] == 1,
    "content-owned encounter weights reserve hard-seed curriculum capacity for the two gatekeepers and final bosses");
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
var buildLimitedHardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
    new[]
    {
        new CombatFoundationHardSeedHistoryEntry
        {
            WorldSeed = 66_002UL,
            DifficultyId = "advanced",
            TerminalScenarioId = "build-limited-gate",
            FailureOccurrences = 4,
            SolvabilityClass = "build-limited"
        }
    },
    campaignCount: 8,
    replayShare: 0.35d,
    iteration: 6,
    runSeed: 124UL,
    enabled: true);
Assert(buildLimitedHardSeedPlan.SourceCampaigns == 0
       && buildLimitedHardSeedPlan.Seeds.Count == 0,
    "oracle-rejected build-limited seeds leave combat-policy replay and are routed away from repeated local action training");
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
    FinalState = new CombatCampaignState
    {
        CurrentHp = 77,
        MaxHp = 143,
        SpecialVariables = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["DoomPower"] = "19"
        }
    },
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
       && terminalCreditEpisodes[2].Frames[^1].LongTermReturn == -1d
       && terminalCreditEpisodes.All(episode =>
           episode.Campaign.TerminalSnapshotKnown
           && episode.Campaign.TerminalBattleIndex == 2
           && episode.Campaign.TerminalPlayerHp == 77
           && episode.Campaign.TerminalPlayerMaxHp == 143
           && episode.Campaign.TerminalDoomPower == 19),
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
    HardSeedReplayShare = 0.35d,
    AdvancedAcceptanceRate = 0.30d
};
Assert(Math.Abs(
           CombatCampaignFoundationTrainer.EffectiveHardSeedReplayShare(
               adaptiveHardRequest,
               ineffectiveHardIterations)
           - adaptiveHardRequest.HardSeedReplayShare)
       < 0.000001d,
    "hard-seed replay share remains configured until advanced arena evidence reaches its acceptance target");
ineffectiveHardIterations[1].ValidAdvancedArenaPairs = 32;
ineffectiveHardIterations[1].CandidateAdvancedWinRate = 0.30d;
ineffectiveHardIterations[1].Promoted = true;
Assert(Math.Abs(
           CombatCampaignFoundationTrainer.EffectiveHardSeedReplayShare(
               adaptiveHardRequest,
               ineffectiveHardIterations)
           - CombatFoundationStagnationProtocol.ReducedHardSeedReplayShare)
       < 0.000001d,
    "hard-seed replay share may decay only after advanced performance reaches its acceptance target");
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
    new() { Promoted = true, WorkingModelAccepted = true },
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
Assert(!CombatCampaignFoundationTrainer.ShouldStopForStagnation(
           new CombatCampaignFoundationTrainingRequest
           {
               MaximumConsecutiveRejectedIterations = 3
           },
           stagnationIterations,
           hasChampion: true,
           startIndex: stagnationIterations.Count),
    "a resumed training attempt resets its rejection streak instead of immediately inheriting historical stagnation");
var longArchiveRoot =
    @"D:\Steam\steamapps\common\Witch's Apocalyptic Journey\Witch's Apocalyptic Journey_Data\ModsData\AuraShared\Logs\AuraToolsExp\combat-simulation-results\foundation-success-cases";
var fullCompatibilityKey = new string('a', 64);
var fullCaseId = new string('b', 64);
var compactArchivePath = CombatFoundationCaseArchiveProtocol.EntryPath(
    longArchiveRoot,
    fullCompatibilityKey,
    CombatFoundationCaseArchiveProtocol.ExpertDirectoryName,
    fullCaseId);
Assert(CombatFoundationCaseArchiveProtocol.Version
           == "success-case-archive-worker-v4"
       && compactArchivePath.Length < 260
       && compactArchivePath.Contains(
           Path.DirectorySeparatorChar
           + "v4"
           + Path.DirectorySeparatorChar,
           StringComparison.Ordinal)
       && !compactArchivePath.Contains(
           fullCompatibilityKey,
           StringComparison.Ordinal),
    "case archive v4 keeps long install paths bounded while payload ids remain authoritative");
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
       && CombatFoundationWorkerProtocol.SchemaVersion == 10
       && CombatFoundationTerminalCreditProtocol.Version
          == "terminal-credit-v2"
       && CombatFoundationCounterfactualProtocol.Version
          == "hard-encounter-counterfactual-v2"
       && CombatFoundationStagnationProtocol.Version
          == "foundation-stagnation-v1"
       && CombatPolicyValueFrameStratificationProtocol.Version
          == "frame-strata-v5-end-turn-counterfactual"
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
var checkpointStorageRoot = Path.Combine(
    Path.GetTempPath(),
    "aura-foundation-checkpoint-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(checkpointStorageRoot);
try
{
    var persistedCheckpointVersions = new List<int>();
    using (var checkpointWriteStarted = new ManualResetEventSlim(false))
    using (var releaseCheckpointWrite = new ManualResetEventSlim(false))
    using (var checkpointPipeline =
           new CombatFoundationLatestWritePipeline<string>(value =>
           {
               if (value == "v1")
               {
                   checkpointWriteStarted.Set();
                   releaseCheckpointWrite.Wait();
               }
               lock (persistedCheckpointVersions)
               {
                   persistedCheckpointVersions.Add(int.Parse(value[1..]));
               }
           }))
    {
        checkpointPipeline.Enqueue("v1");
        checkpointWriteStarted.Wait();
        checkpointPipeline.Enqueue("v2");
        checkpointPipeline.Enqueue("v3");
        releaseCheckpointWrite.Set();
        checkpointPipeline.Drain();
        Assert(checkpointPipeline.EnqueuedCount == 3
               && checkpointPipeline.ExecutedCount == 2
               && checkpointPipeline.CoalescedCount == 1
               && persistedCheckpointVersions.SequenceEqual(
                   new[] { 1, 3 }),
            "foundation checkpoint pipeline overlaps one durable write and coalesces queued states without losing the latest state");
    }
    var checkpointPointerPath = Path.Combine(
        checkpointStorageRoot,
        CombatFoundationWorkerProtocol.CheckpointFileName);
    var checkpointEpisodesBasePath = Path.Combine(
        checkpointStorageRoot,
        CombatFoundationWorkerProtocol.CheckpointEpisodesFileName);
    var snapshotValues = Enumerable.Range(1, 64).ToArray();
    var snapshotSerializerThreads =
        new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
    var firstSnapshot =
        CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
            checkpointEpisodesBasePath,
            snapshotValues,
            value =>
            {
                snapshotSerializerThreads.TryAdd(
                    Environment.CurrentManagedThreadId,
                    0);
                Thread.SpinWait(2000);
                return "{\"episode\":" + value + "}";
            },
            "replay-a",
            maximumDegreeOfParallelism: 4);
    var loadedSnapshot =
        CombatFoundationCheckpointStorage.ReadAndValidateJsonLines(
            firstSnapshot,
            line => line);
    Assert(firstSnapshot.StorageVersion
               == CombatFoundationCheckpointStorage.SnapshotStorageVersion
           && firstSnapshot.EpisodeCount == snapshotValues.Length
           && firstSnapshot.Length > 0
           && firstSnapshot.ContentSha256.Length == 64
           && File.Exists(firstSnapshot.Path)
           && loadedSnapshot.SequenceEqual(snapshotValues.Select(value =>
               "{\"episode\":" + value + "}"))
           && snapshotSerializerThreads.Count > 1,
        "foundation checkpoint storage serializes bounded chunks in parallel while publishing immutable ordered snapshots");

    CombatFoundationCheckpointStorage.WriteAtomicText(
        checkpointPointerPath,
        "pointer-v1");
    using (var blockedPointer = new FileStream(
               checkpointPointerPath,
               FileMode.Open,
               FileAccess.Read,
               FileShare.Read))
    {
        var releasePointer = Task.Run(() =>
        {
            Thread.Sleep(180);
            blockedPointer.Dispose();
        });
        CombatFoundationCheckpointStorage.WriteAtomicText(
            checkpointPointerPath,
            "pointer-v2");
        releasePointer.Wait();
    }
    Assert(CombatFoundationCheckpointStorage.ReadAllTextShared(
               checkpointPointerPath)
               == "pointer-v2"
           && CombatFoundationCheckpointStorage.ReadAllTextShared(
               CombatFoundationCheckpointStorage.BackupPath(
                   checkpointPointerPath))
               == "pointer-v1",
        "foundation checkpoint pointer replacement retries transient Windows delete-sharing locks and retains the previous pointer");

    var streamedArtifactPath = Path.Combine(
        checkpointStorageRoot,
        "streamed-artifact.json");
    CombatFoundationCheckpointStorage.WriteAtomicStream(
        streamedArtifactPath,
        stream =>
        {
            using var writer = new StreamWriter(
                stream,
                new System.Text.UTF8Encoding(false),
                1024,
                leaveOpen: true);
            writer.Write("{\"mode\":\"streamed\",\"ok\":true}");
            writer.Flush();
        },
        retainBackup: false);
    Assert(CombatFoundationCheckpointStorage.ReadAllTextShared(
               streamedArtifactPath)
               == "{\"mode\":\"streamed\",\"ok\":true}",
        "foundation storage atomically publishes streamed artifacts without constructing a full output string");

    var secondSnapshot =
        CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
            checkpointEpisodesBasePath,
            snapshotValues.Select(value => value == snapshotValues.Length
                ? "{\"episode\":999}"
                : "{\"episode\":" + value + "}"),
            "replay-b");
    Assert(firstSnapshot.EpisodeCount == secondSnapshot.EpisodeCount
           && !string.Equals(
               firstSnapshot.ContentSha256,
               secondSnapshot.ContentSha256,
               StringComparison.Ordinal)
           && !string.Equals(
               firstSnapshot.ReplayIdentity,
               secondSnapshot.ReplayIdentity,
               StringComparison.Ordinal),
        "foundation checkpoint snapshots detect same-count replay replacement instead of relying on episode count alone");

    File.AppendAllText(secondSnapshot.Path, "corrupt");
    var corruptedSnapshotRejected = false;
    try
    {
        CombatFoundationCheckpointStorage.ReadAndValidateJsonLines(
            secondSnapshot,
            line => line);
    }
    catch (InvalidDataException)
    {
        corruptedSnapshotRejected = true;
    }
    Assert(corruptedSnapshotRejected,
        "foundation checkpoint resume rejects truncated or modified episode snapshots before deserialization");

    var orphanTemporaryPath =
        checkpointEpisodesBasePath + ".tmp-orphan";
    File.WriteAllText(orphanTemporaryPath, "orphan");
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        checkpointPointerPath,
        checkpointEpisodesBasePath,
        new[] { firstSnapshot.Path },
        retainNewestSnapshots: 1);
    Assert(File.Exists(firstSnapshot.Path)
           && !File.Exists(orphanTemporaryPath),
        "foundation checkpoint cleanup preserves the referenced snapshot and removes orphan temporary files");
}
finally
{
    if (Directory.Exists(checkpointStorageRoot))
    {
        Directory.Delete(checkpointStorageRoot, recursive: true);
    }
}
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
       && versionDiagnostic.Contains(
           "worker=" + (CombatFoundationWorkerProtocol.SchemaVersion - 1),
           StringComparison.Ordinal)
       && versionDiagnostic.Contains(
           "host=" + CombatFoundationWorkerProtocol.SchemaVersion,
           StringComparison.Ordinal),
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
var capabilityGateRequest = new CombatCampaignFoundationTrainingRequest
{
    RequireCapabilityProbeBaselineGain = true,
    CapabilityProbeMinimumVictoryGain = 1,
    CapabilityProbeMinimumDepthGain = 0.5d
};
var capabilityGateProbe = new CombatFoundationCapabilityProbe
{
    Arms =
    {
        new CombatFoundationCapabilityProbeArm
        {
            ArmId = "rule-baseline",
            NormalCampaigns = 12,
            NormalVictories = 10,
            AdvancedCampaigns = 12,
            AdvancedVictories = 3,
            AverageCompletedBattles = 20d
        },
        new CombatFoundationCapabilityProbeArm
        {
            ArmId = "champion-deployment",
            NormalCampaigns = 12,
            NormalVictories = 10,
            AdvancedCampaigns = 12,
            AdvancedVictories = 3,
            AverageCompletedBattles = 20.1d
        },
        new CombatFoundationCapabilityProbeArm
        {
            ArmId = "champion-teacher-hard",
            NormalCampaigns = 12,
            NormalVictories = 11,
            AdvancedCampaigns = 12,
            AdvancedVictories = 5,
            AverageCompletedBattles = 22d
        }
    },
    Pairs =
    {
        new CombatFoundationCapabilityProbePair
        {
            DifficultyId = "normal",
            WorldSeed = 1,
            BaselineVictory = true,
            ChampionVictory = true,
            BaselineCompletedBattles = 20,
            ChampionCompletedBattles = 20
        },
        new CombatFoundationCapabilityProbePair
        {
            DifficultyId = "advanced",
            WorldSeed = 1,
            BaselineVictory = false,
            ChampionVictory = false,
            BaselineCompletedBattles = 18,
            ChampionCompletedBattles = 19
        }
    }
};
CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
    capabilityGateRequest,
    capabilityGateProbe);
Assert(capabilityGateProbe.PassedBaselineGate
       && capabilityGateProbe.BaselineGateVerdict == "inconclusive"
       && capabilityGateProbe.BaselineGateReason.Contains(
           "deployment=normal 10/12, advanced 3/12",
           StringComparison.Ordinal)
       && capabilityGateProbe.BaselineGateReason.Contains(
           "teacher-hard=normal 11/12, advanced 5/12",
           StringComparison.Ordinal),
    "capability probe preserves a tied paired result as inconclusive instead of rejecting the champion");
capabilityGateProbe.Arms[1].NormalVictories = 9;
CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
    capabilityGateRequest,
    capabilityGateProbe);
Assert(capabilityGateProbe.PassedBaselineGate
       && capabilityGateProbe.BaselineGateVerdict == "inconclusive"
       && capabilityGateProbe.ChampionVictoryGain == -1,
    "capability probe does not reject a champion from unpaired aggregate noise when paired evidence is statistically inconclusive");
capabilityGateProbe.Arms[1].NormalVictories = 10;
capabilityGateProbe.Pairs.Clear();
for (var pairedIndex = 0; pairedIndex < 24; pairedIndex++)
{
    capabilityGateProbe.Pairs.Add(
        new CombatFoundationCapabilityProbePair
        {
            DifficultyId = pairedIndex < 12 ? "normal" : "advanced",
            WorldSeed = (ulong)pairedIndex,
            BaselineVictory = pairedIndex >= 20,
            ChampionVictory = pairedIndex < 22,
            BaselineCompletedBattles = 18,
            ChampionCompletedBattles = 20
        });
}
capabilityGateProbe.Arms[1].NormalVictories = 12;
capabilityGateProbe.Arms[1].AdvancedVictories = 10;
CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
    capabilityGateRequest,
    capabilityGateProbe);
Assert(capabilityGateProbe.PassedBaselineGate
       && capabilityGateProbe.BaselineGateVerdict == "pass"
       && capabilityGateProbe.ChampionOnlyWins == 20
       && capabilityGateProbe.BaselineOnlyWins == 2
       && capabilityGateProbe.PairedWinWilsonLowerBound > 0.5d,
    "capability probe promotes only a credible paired-seed win advantage");
foreach (var pair in capabilityGateProbe.Pairs)
{
    (pair.BaselineVictory, pair.ChampionVictory) =
        (pair.ChampionVictory, pair.BaselineVictory);
}
CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
    capabilityGateRequest,
    capabilityGateProbe);
Assert(!capabilityGateProbe.PassedBaselineGate
       && capabilityGateProbe.BaselineGateVerdict == "fail",
    "capability probe rejects a credible paired-seed regression");
Assert(new CombatPolicyValueTrainingOptions
       {
           GradientShardCount = 24
       }.Normalized().GradientShardCount == 24
       && new CombatPolicyValueTrainingOptions
       {
           GradientShardCount = 32
       }.Normalized().GradientShardCount == 32,
    "policy-value training preserves 24 and 32 gradient shard presets for high-parallelism hosts");
Assert(CombatCampaignFoundationTrainer.EstimateTuningCampaigns(
           3,
           32,
           64,
           progressive: true,
           screeningNormalCampaigns: 8,
           screeningAdvancedCampaigns: 16,
           finalistCount: 2) == 216
       && CombatCampaignFoundationTrainer.EstimateTuningCampaigns(
           3,
           32,
           64,
           progressive: false,
           screeningNormalCampaigns: 8,
           screeningAdvancedCampaigns: 16,
           finalistCount: 2) == 288,
    "progressive tuning screens all checkpoints on paired seeds and reserves the full arena for finalists");
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
    RequireCapabilityProbeBaselineGain = false,
    MaximumDegreeOfParallelism = 4,
    TuningNormalCampaigns = 2,
    TuningAdvancedCampaigns = 2,
    TuningScreeningNormalCampaigns = 1,
    TuningScreeningAdvancedCampaigns = 1,
    TuningFinalistCount = 1,
    CaseArchiveLoad = new CombatFoundationCaseArchiveLoadDiagnostics
    {
        ArchiveExists = true,
        LoadedCases = 3,
        LoadedObservations = 9,
        Message = "fixture"
    },
    CaseArchiveCompatibilityKey = "frozen-archive-key",
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
        Epochs = 3,
        RetainedModelCandidates = 3,
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
var incrementallyRecordedModelMetrics =
    new List<CombatPolicyValueEpochMetrics>();
foundationRequest.ObservationRecorded = _ =>
    incrementallyObservedFoundationCases++;
foundationRequest.SuccessCaseRecorded = _ =>
    incrementallyArchivedFoundationCases++;
foundationRequest.ModelMetricRecorded = metrics =>
    incrementallyRecordedModelMetrics.Add(metrics);
var foundationTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset);
var foundationDepthBucketCampaigns =
    foundationTraining.Depth1To5Campaigns
    + foundationTraining.Depth6To10Campaigns
    + foundationTraining.Depth11To20Campaigns
    + foundationTraining.Depth21To30Campaigns
    + foundationTraining.Depth31To37Campaigns;
Assert(foundationTraining.Success
       && foundationTraining.AcceptancePassed
       && foundationTraining.Champion != null
       && foundationTraining.Preflight.Passed
       && foundationTraining.Preflight.CompletedCampaigns
          == 2 + CombatFoundationIntegritySeedCorpus.KnownFailures.Count
       && foundationTraining.Preflight.RegressionSeedCampaigns
          == CombatFoundationIntegritySeedCorpus.KnownFailures.Count
       && foundationTraining.Preflight.SemanticGatePassed
       && foundationTraining.Preflight.InvalidCampaigns == 0
       && foundationTraining.Replay.Count is > 0 and <= 16
       && foundationTraining.Replay.All(episode => episode.Authoritative)
       && foundationTraining.ValidationRuns.Count == 10
       && foundationTraining.Validation.NormalCampaigns == 5
       && foundationTraining.Validation.AdvancedCampaigns == 5
       && foundationTraining.Validation.RequiredNormalVictories == 5
       && foundationTraining.Validation.RequiredAdvancedVictories == 4
       && Math.Abs(
           foundationTraining.Validation.RequiredNormalWinRate - 0.8d)
          < 0.0001d
       && Math.Abs(
           foundationTraining.Validation.RequiredAdvancedWinRate - 0.3d)
          < 0.0001d
       && foundationTraining.CompletedCampaigns < 999
       && foundationTraining.CaseArchiveLoad.LoadedCases == 3
       && foundationTraining.CaseArchiveLoad.LoadedObservations == 9
       && foundationTraining.Validation.NormalWinRate == 1d
       && foundationTraining.Validation.AdvancedWinRate == 1d
       && foundationTraining.Validation.NormalWilsonLowerBound > 0.56d
       && foundationTraining.Validation.AdvancedWilsonLowerBound > 0.56d
       && foundationTraining.EffectiveParallelism == 4
       && foundationTraining.InferenceLaneCount == 1
       && foundationTraining.InferenceBatchSizePerLane == 4
       && foundationTraining.PeakConcurrentCampaigns >= 1
       && foundationTraining.ObservedWorkerThreads >= 1
       && foundationTraining.CompletedBattles > 0
       && foundationTraining.MaximumCompletedBattleDepth == 37
       && foundationDepthBucketCampaigns == foundationTraining.CompletedCampaigns
       && foundationTraining.ProjectedBattleDepth == 37d
       && foundationTraining.PolicyDecisions > 0
       && foundationTraining.SearchSimulations > 0
       && foundationTraining.SearchNodes > 0
       && foundationTraining.AllocatedBytes > 0
       && foundationTraining.CpuSeconds > 0d
       && foundationTraining.PhaseElapsedSeconds.Count > 0
       && foundationTraining.PhaseElapsedSeconds.ContainsKey("self-play")
       && foundationTraining.PhaseElapsedSeconds.ContainsKey("model-training")
       && foundationTraining.PhaseElapsedSeconds.ContainsKey("validation")
       && foundationTraining.PhaseCpuSeconds.ContainsKey("self-play")
       && foundationTraining.PhaseCpuSeconds.Values.Sum() > 0d
       && foundationTraining.PhaseAllocatedBytes.ContainsKey("self-play")
       && foundationTraining.PhaseAllocatedBytes.Values.Sum() > 0L
       && foundationTraining.ModelTrainingLoss > 0d
       && foundationTraining.ModelValidationLoss > 0d
       && foundationTraining.ModelEpochHistory.Count > 0
       && foundationTraining.Iterations.All(item =>
            item.ModelEpochHistory.Count > 0
            && item.ModelTrainingMetrics.FrameCount > 0
            && item.ModelValidationMetrics.FrameCount > 0
            && item.TuningCandidateCount >= 2
            && item.TuningFinalistCount == 1
            && item.TuningCampaignsExecuted > 0
            && item.TuningCampaignsSaved > 0)
       && incrementallyRecordedModelMetrics.Count > 0
       && incrementallyRecordedModelMetrics.All(item =>
           item.Iteration > 0
           && item.Epoch > 0)
       && foundationTraining.CapabilityProbe.Arms.Count == 3
       && foundationTraining.CapabilityProbe.Arms.All(arm =>
           arm.NormalCampaigns == 1
           && arm.AdvancedCampaigns == 1
           && arm.InvalidCampaigns == 0)
       && incrementallyObservedFoundationCases
          == foundationTraining.CampaignObservations.Count
       && foundationTraining.CampaignObservations.All(item =>
           item.CompatibilityKey == "frozen-archive-key")
       && incrementallyArchivedFoundationCases
          == foundationTraining.SuccessCases.Count
       && incrementallyArchivedFoundationCases > 0
       && foundationTraining.ElapsedSeconds > 0d,
    "foundation trainer reports telemetry and streams successful cases as campaigns complete"
    + $" (success={foundationTraining.Success}, acceptance={foundationTraining.AcceptancePassed},"
    + $" preflight={foundationTraining.Preflight.Passed}/{foundationTraining.Preflight.CompletedCampaigns}/"
    + $"{foundationTraining.Preflight.InvalidCampaigns}, replay={foundationTraining.Replay.Count},"
    + $" validationRuns={foundationTraining.ValidationRuns.Count}, completed={foundationTraining.CompletedCampaigns},"
    + $" depthBuckets={foundationDepthBucketCampaigns}, probeArms={foundationTraining.CapabilityProbe.Arms.Count},"
    + $" observations={incrementallyObservedFoundationCases}/{foundationTraining.CampaignObservations.Count},"
    + $" cases={incrementallyArchivedFoundationCases}/{foundationTraining.SuccessCases.Count},"
    + $" elapsed={foundationTraining.ElapsedSeconds:F6})");
var packageJob = new CombatFoundationWorkerJob
{
    JobId = "foundation-package-test",
    Request = foundationRequest,
    Ruleset = new CombatRulesetDocument
    {
        Version = campaignRules.Ruleset.Version,
        Cards = campaignRules.Ruleset.SnapshotCards().ToList(),
        Enemies = campaignRules.Ruleset.SnapshotEnemies().ToList(),
        Statuses = campaignRules.Ruleset.SnapshotStatuses().ToList()
    }
};
var packageOriginalRoleId =
    packageJob.Request.TrainingCampaign.Player.RoleId;
var packageOriginalPartnerId =
    packageJob.Request.TrainingCampaign.Player.PartnerId;
var packageOriginalPresetId =
    packageJob.Request.TrainingCampaign.Player.GameParameterPresetId;
var packageOriginalParameterHash =
    packageJob.Request.TrainingCampaign.Player.GameParameterHash;
var packageOriginalPacks = new List<string>(
    packageJob.Request.TrainingCampaign.EnabledRewardCardPackIds);
var packageOriginalDeckMinimum =
    packageJob.Request.TrainingCampaign.TargetDeckSizeMinimum;
var packageOriginalDeckMaximum =
    packageJob.Request.TrainingCampaign.TargetDeckSizeMaximum;
packageJob.Request.TrainingCampaign.Player.RoleId = "career_1";
packageJob.Request.TrainingCampaign.Player.PartnerId = "Partner_10001";
packageJob.Request.TrainingCampaign.Player.GameParameterPresetId = "standard";
packageJob.Request.TrainingCampaign.Player.GameParameterHash =
    "foundation-package-game-parameters";
packageJob.Request.TrainingCampaign.EnabledRewardCardPackIds =
    new List<string> { "cardpack_1", "cardpack_2", "cardpack_3" };
packageJob.Request.TrainingCampaign.TargetDeckSizeMinimum = 1;
packageJob.Request.TrainingCampaign.TargetDeckSizeMaximum = 24;
var packageResult = new CombatFoundationWorkerResult
{
    JobId = packageJob.JobId,
    Success = true,
    CompletionKind = "training-accepted",
    RulesetHash = foundationTraining.Compatibility.RulesetHash,
    Training = foundationTraining
};
var foundationPackage = CombatFoundationModelPackageProtocol.Create(
    packageJob,
    packageResult,
    "ABCDEF");
Assert(CombatFoundationModelPackageProtocol.TryValidate(
           foundationPackage,
           out var foundationPackageDiagnostic)
       && string.IsNullOrEmpty(foundationPackageDiagnostic)
       && foundationPackage.Model != null
       && foundationPackage.Model.ModelId
          == foundationTraining.Champion!.ModelId
       && foundationPackage.PartnerId == "Partner_10001"
       && foundationPackage.EnabledRewardCardPackIds.Contains("cardpack_3")
       && foundationPackage.TrainingSubject?.RoleId == "career_1"
       && foundationPackage.TrainingSubject?.PartnerId == "Partner_10001"
       && foundationPackage.TrainingSubject.EnabledRewardCardPackIds
           .Contains("cardpack_3")
       && foundationPackage.DeclaredCoverage?.EntityCoverageKnown == true
       && foundationPackage.Validation.Passed,
    "accepted worker results export a self-contained foundation model package");
var avoidableEndTurnGateRejected = false;
foundationTraining.Validation.AvoidableEndTurnsWithUnusedEnergy = 1;
try
{
    CombatFoundationModelPackageProtocol.Create(
        packageJob,
        packageResult,
        "ABCDEF");
}
catch (InvalidOperationException)
{
    avoidableEndTurnGateRejected = true;
}
finally
{
    foundationTraining.Validation.AvoidableEndTurnsWithUnusedEnergy = 0;
}
Assert(avoidableEndTurnGateRejected,
    "foundation export rejects validation with avoidable unused-energy end turns");
var endTurnCounterfactualGateRejected = false;
foundationTraining.Validation.DominatedEndTurns = 1;
foundationTraining.Validation.EndTurnsIntoAvoidableLethal = 1;
foundationTraining.Validation.EndTurnsWithCertifiedCycle = 1;
try
{
    CombatFoundationModelPackageProtocol.Create(
        packageJob,
        packageResult,
        "ABCDEF");
}
catch (InvalidOperationException)
{
    endTurnCounterfactualGateRejected = true;
}
finally
{
    foundationTraining.Validation.DominatedEndTurns = 0;
    foundationTraining.Validation.EndTurnsIntoAvoidableLethal = 0;
    foundationTraining.Validation.EndTurnsWithCertifiedCycle = 0;
}
Assert(endTurnCounterfactualGateRejected,
    "foundation export rejects dominated, avoidable-lethal, or certified-cycle end turns");
foundationPackage.Validation.EndTurnsWithCertifiedCycle = 1;
Assert(!CombatFoundationModelPackageProtocol.TryValidate(
        foundationPackage,
        out var certifiedCycleGateDiagnostic)
       && certifiedCycleGateDiagnostic.Contains(
           "验证",
           StringComparison.Ordinal),
    "foundation import rejects validation that abandoned a certified cycle");
foundationPackage.Validation.EndTurnsWithCertifiedCycle = 0;
var noEffectActionGateRejected = false;
foundationTraining.Validation.NoEffectActionAttempts = 1;
try
{
    CombatFoundationModelPackageProtocol.Create(
        packageJob,
        packageResult,
        "ABCDEF");
}
catch (InvalidOperationException)
{
    noEffectActionGateRejected = true;
}
finally
{
    foundationTraining.Validation.NoEffectActionAttempts = 0;
}
Assert(noEffectActionGateRejected,
    "foundation export rejects validation containing no-effect action attempts");
var currentActionContractVersion =
    foundationPackage.Compatibility.ActionContractVersion;
foundationPackage.Compatibility.ActionContractVersion =
    "action-contract-legacy";
Assert(!CombatFoundationModelPackageProtocol.TryValidate(
        foundationPackage,
        out var actionContractCompatibilityDiagnostic)
       && actionContractCompatibilityDiagnostic.Contains(
           "兼容",
           StringComparison.Ordinal),
    "foundation import rejects models trained under an incompatible action contract");
foundationPackage.Compatibility.ActionContractVersion =
    currentActionContractVersion;
var supersetCoverage = CombatFoundationModelCoverageProtocol.Assess(
    foundationPackage.TrainingSubject!,
    foundationPackage.DeclaredCoverage!,
    new CombatModelRuntimeContext
    {
        RoleId = "career_1",
        PartnerId = "Partner_10001",
        EnabledRewardCardPackIds =
            new List<string> { "cardpack_1", "cardpack_2" },
        PreferredDeckSizeMinimum = 1,
        PreferredDeckSizeMaximum = 20
    });
Assert(supersetCoverage.Level == "full"
       && supersetCoverage.RuntimeExtraCardPackIds.Count == 0
       && supersetCoverage.TrainingOnlyCardPackIds.SequenceEqual(
           new[] { "cardpack_3" }),
    "a model trained with more card packs fully covers a runtime with fewer packs");
var partialCoverage = CombatFoundationModelCoverageProtocol.Assess(
    foundationPackage.TrainingSubject!,
    foundationPackage.DeclaredCoverage!,
    new CombatModelRuntimeContext
    {
        RoleId = "career_other",
        PartnerId = "Partner_10001",
        EnabledRewardCardPackIds =
            new List<string>
            {
                "cardpack_1",
                "cardpack_2",
                "cardpack_4"
            },
        PreferredDeckSizeMinimum = 1,
        PreferredDeckSizeMaximum = 24
    });
Assert(partialCoverage.Level == "partial"
       && partialCoverage.RoleSkillFallbackRequired
       && partialCoverage.RuntimeExtraCardPackIds.SequenceEqual(
           new[] { "cardpack_4" }),
    "role changes and runtime-only card packs are assessed as partial coverage instead of incompatibility");
var recordingCoverageModel = new RecordingPolicyValueModel();
var coverageAwareModel = new CoverageAwareCombatPolicyValueModel(
    recordingCoverageModel,
    new CombatFoundationTrainingSubject
    {
        RoleId = "career_trained",
        PartnerId = "partner_trained",
        EnabledRewardCardPackIds =
            new List<string> { "cardpack_1", "cardpack_2" },
        PreferredDeckSizeMinimum = 1,
        PreferredDeckSizeMaximum = 24
    },
    new CombatFoundationDeclaredCoverage
    {
        EntityCoverageKnown = true,
        CardIds = new List<string> { "known-card" },
        StatusIds = new List<string> { "known-status" }
    },
    new CombatModelRuntimeContext
    {
        RoleId = "career_other",
        PartnerId = "partner_trained"
    });
var coveragePrediction = coverageAwareModel.Evaluate(
    new CombatPolicyValueInput
    {
        StateFeatures = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["playerHp"] = 20d,
            ["playerStatus:known-status"] = 1d,
            ["playerStatus:unknown-status"] = 2d
        },
        Candidates =
        {
            new CombatPolicyValueCandidate
            {
                CandidateId = "known",
                SourceId = "known-card",
                ActionKind = CombatActionKind.PlayCard.ToString()
            },
            new CombatPolicyValueCandidate
            {
                CandidateId = "unknown",
                SourceId = "unknown-card",
                ActionKind = CombatActionKind.PlayCard.ToString()
            },
            new CombatPolicyValueCandidate
            {
                CandidateId = "role-skill",
                SourceId = "skill-other",
                ActionKind = CombatActionKind.UseSkill.ToString()
            }
        }
    });
Assert(recordingCoverageModel.LastInput != null
       && recordingCoverageModel.LastInput.StateFeatures.ContainsKey(
           "playerStatus:known-status")
       && !recordingCoverageModel.LastInput.StateFeatures.ContainsKey(
           "playerStatus:unknown-status")
       && coveragePrediction.PolicyLogits["known"] == 2d
       && coveragePrediction.PolicyLogits["unknown"] == 0d
       && coveragePrediction.PolicyLogits["role-skill"] == 0d,
    "coverage-aware inference keeps learned card decisions while unknown cards and foreign role skills fall back");
var packageTrainingSubject = foundationPackage.TrainingSubject;
var packageDeclaredCoverage = foundationPackage.DeclaredCoverage;
foundationPackage.TrainingSubject = null;
foundationPackage.DeclaredCoverage = null;
Assert(CombatFoundationModelPackageProtocol.TryValidate(
        foundationPackage,
        out var legacyPackageDiagnostic)
       && string.IsNullOrEmpty(legacyPackageDiagnostic),
    "legacy v2 foundation packages without coverage extensions remain importable");
foundationPackage.TrainingSubject = packageTrainingSubject;
foundationPackage.DeclaredCoverage = packageDeclaredCoverage;
foundationPackage.TrainingSubject!.RoleId = "tampered-role";
Assert(!CombatFoundationModelPackageProtocol.TryValidate(
        foundationPackage,
        out var inconsistentSubjectDiagnostic)
       && inconsistentSubjectDiagnostic.Contains(
           "训练主体元数据",
           StringComparison.Ordinal),
    "extended foundation packages reject internally inconsistent training subject metadata");
foundationPackage.TrainingSubject.RoleId = foundationPackage.RoleId;
foundationPackage.CompletionKind = "training-rejected";
Assert(!CombatFoundationModelPackageProtocol.TryValidate(
           foundationPackage,
           out var rejectedFoundationPackageDiagnostic)
       && rejectedFoundationPackageDiagnostic.Contains(
           "已验收",
           StringComparison.Ordinal),
    "external foundation package validation rejects non-accepted training results");
foundationPackage.CompletionKind = "training-accepted";
packageJob.Request.TrainingCampaign.Player.RoleId = packageOriginalRoleId;
packageJob.Request.TrainingCampaign.Player.PartnerId = packageOriginalPartnerId;
packageJob.Request.TrainingCampaign.Player.GameParameterPresetId =
    packageOriginalPresetId;
packageJob.Request.TrainingCampaign.Player.GameParameterHash =
    packageOriginalParameterHash;
packageJob.Request.TrainingCampaign.EnabledRewardCardPackIds =
    packageOriginalPacks;
packageJob.Request.TrainingCampaign.TargetDeckSizeMinimum =
    packageOriginalDeckMinimum;
packageJob.Request.TrainingCampaign.TargetDeckSizeMaximum =
    packageOriginalDeckMaximum;
var sharedParameters = new CombatFoundationTrainingParameters
{
    Iterations = 0,
    TrainingCampaignsPerIteration = 1,
    MaximumDegreeOfParallelism = int.MaxValue,
    ModelEpochs = 1
}.Normalized();
Assert(sharedParameters.Iterations == 1
       && sharedParameters.AdditionalIterationsOnResume == 3
       && sharedParameters.TrainingCampaignsPerIteration == 2
       && sharedParameters.ModelEpochs == 5
       && sharedParameters.EnablePrioritizedReplay
       && sharedParameters.EnableEndTurnSpecialization
       && sharedParameters.ModelEndTurnFrameWeight == 1d
       && sharedParameters.ModelMaximumUnsafeEndTurnFrameShare == 0.35d
       && sharedParameters.ModelMinimumValidationRunGroups == 16
       && sharedParameters.ModelMinimumTestRunGroups == 16
       && sharedParameters.ModelPolicyTargetTemperature == 1.25d
       && sharedParameters.ModelMaximumPolicyTargetProbability == 0.90d
       && sharedParameters.ModelGradientShardCount == 12
       && sharedParameters.AutoTuneObjective
          == CombatFoundationAutoTuneObjectiveNames.MaximumThroughput
       && sharedParameters.ValidationEarlyStopBatchSize == 32
       && sharedParameters.MaximumDegreeOfParallelism
          <= Math.Max(1, Environment.ProcessorCount)
       && sharedParameters.EstimatedCampaigns() > 0,
    "shared foundation job parameters normalize identically for game and control-center adapters");
var cpu16Execution = CombatFoundationExecutionProfiles.Resolve(
    CombatFoundationExecutionProfileNames.Cpu16,
    1,
    CombatFoundationExecutionProfileNames.DirectInference,
    0,
    0,
    0,
    availableProcessorCount: 32);
var cpu32Execution = CombatFoundationExecutionProfiles.Resolve(
    CombatFoundationExecutionProfileNames.Cpu32,
    1,
    CombatFoundationExecutionProfileNames.DirectInference,
    0,
    0,
    0,
    availableProcessorCount: 32);
Assert(cpu16Execution.CampaignParallelism == 16
       && cpu16Execution.InferenceParallelism == 16
       && cpu16Execution.InferenceBatchSize == 1
       && cpu16Execution.ThreadPoolMinimumWorkerThreads == 24
       && cpu16Execution.CheckpointSerializationParallelism == 1
       && cpu32Execution.CampaignParallelism == 32
       && cpu32Execution.InferenceParallelism == 32
       && cpu32Execution.InferenceBatchSize == 1
       && cpu32Execution.ThreadPoolMinimumWorkerThreads == 40
       && cpu32Execution.CheckpointSerializationParallelism == 2,
    "CPU-16 and CPU-32 profiles expose direct per-campaign inference and bounded background work");
var autoTuneSelection = CombatFoundationAutoTuneSelector.Select(
    new[]
    {
        new CombatFoundationAutoTuneMeasurement
        {
            Parallelism = 16,
            EfficiencyScore = 980d
        },
        new CombatFoundationAutoTuneMeasurement
        {
            Parallelism = 32,
            EfficiencyScore = 1000d
        }
    },
    0.02d);
Assert(autoTuneSelection == 16
       && CombatFoundationAutoTuneSelector.Score(
           1000d,
           gen2CollectionsPerSecond: 0d,
           allocationMegabytesPerSecond: 1024d) >
          CombatFoundationAutoTuneSelector.Score(
              1000d,
              gen2CollectionsPerSecond: 8d,
              allocationMegabytesPerSecond: 8192d),
    "auto-tune selects the lowest near-maximum throughput profile and penalizes GC/allocation pressure");
var maximumThroughputSelection = CombatFoundationAutoTuneSelector.Select(
    new[]
    {
        new CombatFoundationAutoTuneMeasurement
        {
            Parallelism = 12,
            UsefulWorkPerSecond = 1000d,
            EfficiencyScore = 1000d
        },
        new CombatFoundationAutoTuneMeasurement
        {
            Parallelism = 20,
            UsefulWorkPerSecond = 1010d,
            EfficiencyScore = 1010d
        }
    },
    0.02d,
    CombatFoundationAutoTuneObjectiveNames.MaximumThroughput);
Assert(maximumThroughputSelection == 20,
    "maximum-throughput auto-tune chooses the fastest wall-clock candidate even inside the efficiency tolerance");
var inferenceSelection = CombatFoundationAutoTuneSelector.SelectInference(
    new[]
    {
        new CombatFoundationAutoTuneMeasurement
        {
            MeasurementKind = "inference",
            InferenceMode = CombatFoundationExecutionProfileNames.DirectInference,
            InferenceLaneCount = 16,
            InferenceBatchSize = 1,
            EfficiencyScore = 990d,
            P95LatencyMicroseconds = 20d
        },
        new CombatFoundationAutoTuneMeasurement
        {
            MeasurementKind = "inference",
            InferenceMode = CombatFoundationExecutionProfileNames.ShardedBatchInference,
            InferenceLaneCount = 4,
            InferenceBatchSize = 4,
            EfficiencyScore = 1000d,
            P95LatencyMicroseconds = 40d
        }
    },
    0.02d);
Assert(inferenceSelection?.InferenceMode
       == CombatFoundationExecutionProfileNames.DirectInference,
    "inference auto-tune prefers lower latency when throughput is within tolerance");
Assert(CombatFoundationExecutionProfiles.EffectiveLaneCount(12) == 1
       && CombatFoundationExecutionProfiles.EffectiveLaneCount(20) == 2
       && CombatFoundationExecutionProfiles.EffectiveBatchSize(12) == 4
       && CombatFoundationExecutionProfiles.EffectiveBatchSize(20) == 4,
    "automatic inference plans keep enough campaign callers on each batch queue");
Assert(CombatCampaignFoundationTrainer.BuildAutoTuneParallelismCandidates(20)
        .SequenceEqual(new[] { 4, 8, 12, 16, 20 }),
    "auto-tune benchmarks multiple bounded CPU parallelism candidates");
var developmentGovernance = CombatFoundationGovernanceProfiles.Resolve(
    CombatFoundationGovernanceProfileNames.Development,
    tuningInterval: 1,
    tuningNormalCampaigns: 32,
    tuningAdvancedCampaigns: 64,
    tuningScreeningNormalCampaigns: 8,
    tuningScreeningAdvancedCampaigns: 16,
    tuningFinalistCount: 2,
    capabilityProbeTeacherCampaignsPerDifficulty: 128,
    autoTuneSampleCampaigns: 32);
Assert(developmentGovernance.TuningInterval == 2
       && developmentGovernance.TuningNormalCampaigns == 16
       && developmentGovernance.TuningAdvancedCampaigns == 32
       && developmentGovernance.TuningScreeningNormalCampaigns == 4
       && developmentGovernance.TuningScreeningAdvancedCampaigns == 8
       && developmentGovernance.TuningFinalistCount == 1
       && developmentGovernance.CapabilityProbeTeacherCampaignsPerDifficulty
          == 16
       && developmentGovernance.AutoTuneSampleCampaigns == 16
       && developmentGovernance.ScheduledTuningIterations(8) == 5,
    "development governance reduces iterative evaluation without weakening formal validation");
var efficientCampaignEstimate = new CombatFoundationTrainingParameters
{
    GovernanceProfile = CombatFoundationGovernanceProfileNames.Development,
    Iterations = 8,
    TrainingCampaignsPerIteration = 64,
    ArenaCampaignsPerDifficulty = 32,
    ArenaConfirmationCampaignsPerDifficulty = 64,
    NormalValidationCampaigns = 200,
    AdvancedValidationCampaigns = 500,
    CapabilityProbeCampaignsPerDifficulty = 128,
    CapabilityProbeTeacherCampaignsPerDifficulty = 128,
    ModelRetainedCandidates = 3,
    EnableTuningArena = true,
    EnableProgressiveTuning = true,
    TuningNormalCampaigns = 32,
    TuningAdvancedCampaigns = 64,
    TuningScreeningNormalCampaigns = 8,
    TuningScreeningAdvancedCampaigns = 16,
    TuningFinalistCount = 2
}.EstimatedCampaigns();
Assert(efficientCampaignEstimate == 5188,
    "development governance campaign estimate reflects scheduled tuning and diagnostic teacher caps");
var arenaChampionRuns = new List<CombatCampaignResult>
{
    new() { DifficultyId = "normal", FinalBossVictory = true },
    new() { DifficultyId = "advanced", FinalBossVictory = true }
};
var arenaCandidateRuns = new List<CombatCampaignResult>
{
    new() { DifficultyId = "normal", FinalBossVictory = false },
    new() { DifficultyId = "advanced", FinalBossVictory = false }
};
Assert(!CombatCampaignFoundationTrainer.ArenaNoRegressionStillPossible(
           arenaChampionRuns,
           arenaCandidateRuns,
           remainingPairsPerDifficulty: 0,
           requireAdvancedStrictGain: false)
       && CombatCampaignFoundationTrainer.ArenaNoRegressionStillPossible(
           arenaChampionRuns,
           arenaCandidateRuns,
           remainingPairsPerDifficulty: 1,
           requireAdvancedStrictGain: false),
    "sequential arena stopping rejects only when remaining pairs cannot recover a no-regression result");
Assert(CombatCampaignFoundationTrainer.ShouldAcceptWorkingModel(
           workingCheckpoint: true,
           bootstrapPromotion: false,
           meaningfulWinGain: true,
           meaningfulProgressGain: false)
       && !CombatCampaignFoundationTrainer.ShouldAcceptWorkingModel(
           workingCheckpoint: true,
           bootstrapPromotion: false,
           meaningfulWinGain: false,
           meaningfulProgressGain: false)
       && !CombatCampaignFoundationTrainer.ShouldAcceptWorkingModel(
           workingCheckpoint: false,
           bootstrapPromotion: false,
           meaningfulWinGain: true,
           meaningfulProgressGain: true),
    "working models advance on current-window paired gains rather than incomparable historical arena scores");
var capabilityBaselineRuns = new CombatCampaignResult?[]
{
    new() { DifficultyId = "normal", FinalBossVictory = true },
    new() { DifficultyId = "normal", FinalBossVictory = true },
    new() { DifficultyId = "advanced", FinalBossVictory = true },
    new() { DifficultyId = "advanced", FinalBossVictory = true }
};
var capabilityChampionRuns = new CombatCampaignResult?[]
{
    new() { DifficultyId = "normal", FinalBossVictory = false },
    new() { DifficultyId = "normal", FinalBossVictory = false },
    new() { DifficultyId = "advanced", FinalBossVictory = false },
    new() { DifficultyId = "advanced", FinalBossVictory = false }
};
Assert(CombatCampaignFoundationTrainer.CapabilityNoRegressionStillPossible(
           capabilityBaselineRuns,
           capabilityChampionRuns,
           campaignsPerDifficulty: 2,
           completedPerDifficulty: 1)
       && !CombatCampaignFoundationTrainer.CapabilityNoRegressionStillPossible(
           capabilityBaselineRuns,
           capabilityChampionRuns,
           campaignsPerDifficulty: 2,
           completedPerDifficulty: 2),
    "capability probe stops only after the remaining paired samples cannot recover baseline parity");
var reusableRiskStatistics = new CombatSearchRiskStatistics();
reusableRiskStatistics.Record(-2d, 0.8d);
reusableRiskStatistics.Record(2d, 0.2d);
var firstRiskEstimate = reusableRiskStatistics.Estimate(0.5d);
reusableRiskStatistics.Reset();
reusableRiskStatistics.Record(4d, 0.1d);
var resetRiskEstimate = reusableRiskStatistics.Estimate(0.5d);
Assert(firstRiskEstimate.SampleCount == 2
       && resetRiskEstimate.SampleCount == 1
       && Math.Abs(resetRiskEstimate.Mean - 4d) < 0.000000001d,
    "search risk statistics reset reuses storage without retaining prior evidence");
for (var index = 0; index < 2048; index++)
{
    reusableRiskStatistics.Record(index, 0.5d);
}
_ = reusableRiskStatistics.Estimate(0.1d);
var riskAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
for (var index = 0; index < 128; index++)
{
    reusableRiskStatistics.Record(index, 0.5d);
    _ = reusableRiskStatistics.Estimate(0.1d);
}
var riskAllocationBytes =
    GC.GetAllocatedBytesForCurrentThread() - riskAllocationBefore;
Assert(riskAllocationBytes < 64 * 1024,
    "risk estimation reuses its ordered-sample buffer in the search hot path");
var batchDiagnostics = CombatPolicyValueBatchDiagnostics.Capture();
Assert(batchDiagnostics.Requests >= 12
       && batchDiagnostics.BatchEvaluations > 0
       && batchDiagnostics.AverageBatchSize >= 1d
       && batchDiagnostics.AverageWaitMicroseconds >= 0d,
    "batched inference exposes fill, flush, and wait diagnostics");
var appendRequest = new CombatCampaignFoundationTrainingRequest
{
    Iterations = 3,
    AdditionalIterationsOnResume = 3,
    Resume = new CombatCampaignFoundationResumeState
    {
        Stage = "validation",
        NextIteration = 2
    }
};
Assert(
    CombatCampaignFoundationTrainer.ResolveIterationLimit(appendRequest) == 5,
    "terminal rejected checkpoints append configured iterations instead of rerunning validation at the old limit");
appendRequest.Resume.Stage = "iteration-complete";
Assert(
    CombatCampaignFoundationTrainer.ResolveIterationLimit(appendRequest) == 5,
    "iteration-complete checkpoints append configured iterations from their next iteration boundary");
var continuationManifest = new CombatFoundationCompatibilityManifest
{
    RulesetHash = "rules",
    NativeProgramPackageHash = "new-worker",
    CampaignId = "campaign",
    CampaignVersion = "1",
    TrainingCampaignHash = "training",
    ValidationCampaignHash = "validation",
    FeatureSchemaVersion = CombatPolicyValueProtocol.FeatureSchemaVersion,
    FeatureEncodingMode = "partitioned-v3",
    TrainingPolicyVersion =
        CombatFoundationTrainingProtocol.TrainingPolicyVersion,
    StateDimensions = 128,
    ActionDimensions = 96,
    HiddenDimensions = 64
};
var priorWorkerManifest = new CombatFoundationCompatibilityManifest
{
    RulesetHash = continuationManifest.RulesetHash,
    NativeProgramPackageHash = "old-worker",
    CampaignId = continuationManifest.CampaignId,
    CampaignVersion = continuationManifest.CampaignVersion,
    TrainingCampaignHash = continuationManifest.TrainingCampaignHash,
    ValidationCampaignHash = continuationManifest.ValidationCampaignHash,
    FeatureSchemaVersion = continuationManifest.FeatureSchemaVersion,
    FeatureEncodingMode = continuationManifest.FeatureEncodingMode,
    TrainingPolicyVersion = continuationManifest.TrainingPolicyVersion,
    StateDimensions = continuationManifest.StateDimensions,
    ActionDimensions = continuationManifest.ActionDimensions,
    HiddenDimensions = continuationManifest.HiddenDimensions
};
Assert(
    CombatCampaignFoundationTrainer.ManifestCompatible(
        priorWorkerManifest,
        continuationManifest),
    "iteration-boundary continuation tolerates a rebuilt worker while retaining ruleset, campaign, feature, and model compatibility gates");
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
       && capturedFoundationCheckpoint.RunSeed
          == foundationRequest.RunSeed
       && capturedFoundationCheckpoint.TrainingSeedStart
          == foundationRequest.TrainingSeedStart
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
       && capturedFoundationCheckpoint.Compatibility.StateDimensions
          == foundationRequest.Training.StateDimensions
       && capturedFoundationCheckpoint.Compatibility.HiddenDimensions == 8
       && capturedFoundationCheckpoint.CompletedCampaigns == 2
       && capturedFoundationCheckpoint.Replay.Count > 0
       && capturedFoundationCheckpoint.Replay.Count < 74
       && resumedFoundationTraining.Success
       && resumedFoundationTraining.Champion != null
       && foundationTraining.Champion != null
       && resumedFoundationTraining.Champion.StateWeights.SequenceEqual(
           foundationTraining.Champion.StateWeights)
       && resumedFoundationTraining.Champion.PolicyWeights.SequenceEqual(
           foundationTraining.Champion.PolicyWeights),
    "foundation checkpoints persist the sampled replay window and resume model training without replaying campaigns"
    + $" (interrupted={interruptedFoundationObserved}, captured={capturedFoundationCheckpoint != null},"
    + $" completed={capturedFoundationCheckpoint?.CompletedCampaigns}, replay={capturedFoundationCheckpoint?.Replay.Count},"
    + $" resumedSuccess={resumedFoundationTraining.Success}, resumedChampion={resumedFoundationTraining.Champion != null},"
    + $" baselineChampion={foundationTraining.Champion != null},"
    + $" stateEqual={resumedFoundationTraining.Champion?.StateWeights.SequenceEqual(foundationTraining.Champion?.StateWeights ?? Array.Empty<double>())},"
    + $" policyEqual={resumedFoundationTraining.Champion?.PolicyWeights.SequenceEqual(foundationTraining.Champion?.PolicyWeights ?? Array.Empty<double>())})");
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
    + ", allocatedMB="
    + (foundationTraining.AllocatedBytes / 1048576d).ToString("F1")
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
foundationRequest.RetainValidationRunDetails = false;
var earlyStoppedFoundationTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset,
    foundationTraining.Champion);
Assert(earlyStoppedFoundationTraining.Success
       && !earlyStoppedFoundationTraining.AcceptancePassed
       && earlyStoppedFoundationTraining.Validation.EarlyStopped
       && earlyStoppedFoundationTraining.Validation.NormalCampaigns == 5
       && earlyStoppedFoundationTraining.Validation.AdvancedCampaigns == 0
       && earlyStoppedFoundationTraining.CompletedCampaigns
          < earlyStoppedFoundationTraining.RequestedCampaigns
       && earlyStoppedFoundationTraining.ValidationRuns.Count == 5
       && earlyStoppedFoundationTraining.ValidationRuns.All(item =>
           item.Battles.Count == 0
           && item.Rewards.Count == 0
           && !item.FinalBossVictory),
    "foundation validation analyzes one deterministic configured batch and releases full battle graphs when the external worker retention policy is active");
foundationRequest.RetainValidationRunDetails = true;
projectedStrike.Fidelity = CombatRuleFidelity.Approximate;
var invalidPreflightTraining = new CombatCampaignFoundationTrainer().Run(
    foundationRequest,
    campaignRules.Ruleset,
    foundationTraining.Champion);
projectedStrike.Fidelity = CombatRuleFidelity.Authoritative;
Assert(!invalidPreflightTraining.Success
       && !invalidPreflightTraining.Preflight.Passed
       && invalidPreflightTraining.Preflight.InvalidCampaigns
          == 2 + CombatFoundationIntegritySeedCorpus.KnownFailures.Count
       && invalidPreflightTraining.Preflight.Failures.Any(item =>
           item.DifficultyId == "normal" && item.WorldSeed == 19000UL)
       && invalidPreflightTraining.Preflight.Failures.Any(item =>
           item.DifficultyId == "advanced" && item.WorldSeed == 19001UL)
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
var reusableDrawPile = new List<string>();
var reusableUnknownCards = new List<string>();
CombatRootDeterminizer.SampleDrawPileInto(
    hiddenBeliefA,
    hiddenSampleSeed,
    reusableDrawPile,
    reusableUnknownCards);
var reusableDrawPileCapacity = reusableDrawPile.Capacity;
CombatRootDeterminizer.SampleDrawPileInto(
    hiddenBeliefA,
    hiddenSampleSeed,
    reusableDrawPile,
    reusableUnknownCards);
Assert(reusableDrawPile.SequenceEqual(
           CombatRootDeterminizer.SampleDrawPile(
               hiddenBeliefA,
               hiddenSampleSeed))
       && reusableDrawPile.Capacity == reusableDrawPileCapacity,
    "root determinization reuses draw-pile storage without changing seeded order");
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
var cachedDecision = new CombatDecision
{
    HasAction = true,
    Action = currentTokenAction,
    Candidates =
    {
        new CombatCandidateEvaluation
        {
            Action = currentTokenAction,
            Legal = true,
            SearchPrior = 0.75d
        }
    }
};
var currentObservationAction = new CombatActionObservation
{
    ObservationId = "battle:10",
    ActionToken = "a7",
    CandidateId = "attack",
    Legal = true
};
var currentObservation = new CombatStateObservation
{
    ObservationId = "battle:10",
    Actions = { currentObservationAction }
};
Assert(
    CombatDecisionExecutionBindingProtocol.TryBindToObservation(
        cachedDecision,
        currentObservation,
        out var reboundDecision,
        out _)
    && ReferenceEquals(reboundDecision.Action, currentObservationAction)
    && reboundDecision.Action.ActionToken == "a7"
    && reboundDecision.Candidates[0].SearchPrior == 0.75d,
    "cached semantic decisions rebind to the current observation action token");
currentObservationAction.Legal = false;
Assert(
    !CombatDecisionExecutionBindingProtocol.TryBindToObservation(
        cachedDecision,
        currentObservation,
        out _,
        out var illegalRebindReason)
    && illegalRebindReason.Contains(
        "no longer legal",
        StringComparison.Ordinal),
    "cached decisions never bypass current-observation legality");
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
           "visibleModCounter",
           "number",
           0d,
           3d,
           0d))
{
    var registeredFeatureState = BuildPlayerEquivalentFixture(false);
    registeredFeatureState.Features["visibleModCounter"] = 4d;
    Assert(CombatPlayerObservationBoundary.Normalize(registeredFeatureState)
               .Features["visibleModCounter"] == 3d,
        "registered public MOD features are admitted and clamped to their declared range");
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
       && orderedCageForward.PlayerDefend == 0
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

var gameValidationRequest = new CombatGameValidationRequest
{
    RequestId = "validation-1",
    Profile = "balanced",
    ModelId = "policy-v1",
    ModelArtifactHash = "artifact-a",
    GameBuild = "1.2.3",
    CampaignId = "campaign",
    CampaignVersion = "2",
    RulesetHash = "rules-a",
    NativePackageHash = "native-a",
    CreatedUtc = "2026-07-28T00:00:00.0000000Z",
    Cases =
    {
        new CombatGameValidationCase
        {
            CaseId = "final-boss.hje",
            LevelId = "level_10048",
            EncounterId = "enemy_10055",
            Repetitions = 2,
            MinimumWins = 1
        }
    }
};
Assert(CombatGameValidationProtocol.ValidateRequest(
        gameValidationRequest,
        out _),
    "game-host validation request requires immutable model and semantic identities");
var gameValidationReport = new CombatGameValidationReport
{
    RequestId = gameValidationRequest.RequestId,
    ModelId = gameValidationRequest.ModelId,
    CompatibilityKey = CombatGameValidationProtocol.BuildCompatibilityKey(
        gameValidationRequest.Profile,
        gameValidationRequest.ModelId,
        gameValidationRequest.ModelArtifactHash,
        gameValidationRequest.GameBuild,
        gameValidationRequest.CampaignId,
        gameValidationRequest.CampaignVersion,
        gameValidationRequest.RulesetHash,
        gameValidationRequest.NativePackageHash),
    Completed = true,
    Passed = true,
    StartedUtc = "2026-07-28T00:00:01.0000000Z",
    CompletedUtc = "2026-07-28T00:03:01.0000000Z",
    Cases =
    {
        new CombatGameValidationCaseResult
        {
            CaseId = "final-boss.hje",
            LevelId = "level_10048",
            Attempts = 2,
            Wins = 1,
            Losses = 1,
            Decisions = 18
        }
    }
};
gameValidationReport.ReceiptHash =
    CombatGameValidationProtocol.BuildReceiptHash(gameValidationReport);
Assert(CombatGameValidationProtocol.ValidateReport(
        gameValidationRequest,
        gameValidationReport,
        out _),
    "complete game-host receipt passes when coverage, outcome and identity match");
gameValidationRequest.RulesetHash = "rules-b";
Assert(!CombatGameValidationProtocol.ValidateReport(
        gameValidationRequest,
        gameValidationReport,
        out var staleGameValidationReason)
       && staleGameValidationReason.Contains("不匹配", StringComparison.Ordinal),
    "game-host receipt is invalidated by an authoritative ruleset change");

var contentAudit = new CombatTransitionAuditCorpus
{
    Cases =
    {
        new CombatTransitionAuditCase
        {
            CaseId = "alias-a",
            CompactStateFingerprint = "compact",
            FullStateHash = "full-a",
            ActionFingerprint = "play:test",
            NextCompactStateFingerprint = "next-a",
            NextFullStateHash = "next-full-a",
            Outcome = "continue",
            RuntimeSettlementHash = "settlement-a",
            SimulationSettlementHash = "settlement-a"
        },
        new CombatTransitionAuditCase
        {
            CaseId = "alias-b",
            CompactStateFingerprint = "compact",
            FullStateHash = "full-b",
            ActionFingerprint = "play:test",
            NextCompactStateFingerprint = "next-b",
            NextFullStateHash = "next-full-b",
            Outcome = "continue",
            RuntimeSettlementHash = "settlement-b",
            SimulationSettlementHash = "settlement-c"
        }
    }
};
var contentAuditReport = CombatTransitionAuditAnalyzer.Analyze(contentAudit);
Assert(contentAuditReport.AliasedStateCount == 1
       && contentAuditReport.DivergentTransitionCount == 1
       && contentAuditReport.RuntimeMismatchCount == 1
       && !contentAuditReport.Passed,
    "content package transition audit detects state alias divergence and settlement mismatch");
var hiddenStateAuditReport = CombatTransitionAuditAnalyzer.Analyze(
    new CombatTransitionAuditCorpus
    {
        Cases =
        {
            new CombatTransitionAuditCase
            {
                CaseId = "hidden-a",
                CompactStateFingerprint = "same-compact",
                FullStateHash = "full-a",
                ActionFingerprint = "same-action",
                NextCompactStateFingerprint = "same-next-compact",
                NextFullStateHash = "next-full-a",
                Outcome = "continue",
                RuntimeSettlementHash = "same-settlement",
                SimulationSettlementHash = "same-settlement"
            },
            new CombatTransitionAuditCase
            {
                CaseId = "hidden-b",
                CompactStateFingerprint = "same-compact",
                FullStateHash = "full-b",
                ActionFingerprint = "same-action",
                NextCompactStateFingerprint = "same-next-compact",
                NextFullStateHash = "next-full-b",
                Outcome = "continue",
                RuntimeSettlementHash = "same-settlement",
                SimulationSettlementHash = "same-settlement"
            }
        }
    });
Assert(hiddenStateAuditReport.DivergentTransitionCount == 1
       && !hiddenStateAuditReport.Passed,
    "transition audit rejects hidden-state divergence even when compact outcomes match");
var contentTrainingEpisode = new CombatEpisode
{
    EpisodeId = "registered-content-episode",
    Authoritative = true,
    RulesetHash = "registered-ruleset",
    ContentSetHash = CombatContentSetProtocol.EmptyContentSetHash,
    OwnerModSetHash = CombatContentSetProtocol.EmptyOwnerModSetHash,
    Frames =
    {
        new CombatEpisodeFrame
        {
            StateFingerprint = "content-state",
            ExecutedCandidateId = "content-action",
            Candidates =
            {
                new CombatEpisodeCandidate
                {
                    CandidateId = "content-action",
                    SourceId = "content-card",
                    OwnerModId = "Tests.Content",
                    Legal = true
                }
            }
        }
    }
};
Assert(CombatContentTrainingEpisodeProtocol.TryValidate(
        contentTrainingEpisode,
        CombatContentSetProtocol.EmptyContentSetHash,
        CombatContentSetProtocol.EmptyOwnerModSetHash,
        "registered-ruleset",
        out _),
    "registered content episodes require authoritative finite policy-integrity frames");
var contentEpisodeJob = new CombatFoundationWorkerJob
{
    ExpectedRulesetHash = "registered-ruleset",
    Request = new CombatCampaignFoundationTrainingRequest
    {
        AuthoritativeContentEpisodes = { contentTrainingEpisode }
    }
};
Assert(CombatFoundationWorkerProtocol.TryValidateJob(
        contentEpisodeJob,
        out _),
    "worker schema carries validated content episodes into foundation replay");
contentTrainingEpisode.Frames[0].Candidates[0].OwnerModId = "unregistered";
Assert(!CombatContentTrainingEpisodeProtocol.TryValidate(
        contentTrainingEpisode,
        CombatContentSetProtocol.EmptyContentSetHash,
        CombatContentSetProtocol.EmptyOwnerModSetHash,
        "registered-ruleset",
        out _),
    "content episodes reject candidates omitted from authoritative owner registration");
contentTrainingEpisode.Frames[0].Candidates[0].OwnerModId = "Tests.Content";
var pinnedContentReplay = new CombatFoundationReplaySelection();
CombatFoundationReplaySampler.PinEpisodes(
    pinnedContentReplay,
    new[] { contentTrainingEpisode },
    episodeLimit: 8,
    requestedShare: 0.20d);
Assert(pinnedContentReplay.PinnedContentEpisodes == 1
       && pinnedContentReplay.Episodes.Count == 1
       && ReferenceEquals(
           pinnedContentReplay.Episodes[0],
           contentTrainingEpisode),
    "registered content replay receives a configurable guaranteed training quota");

var contentPackageRoot = Path.Combine(
    Path.GetTempPath(),
    "aura-combat-content-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(contentPackageRoot);
try
{
    var auditPath = Path.Combine(contentPackageRoot, "transition-audit.json");
    var passingAudit = new CombatTransitionAuditCorpus
    {
        Cases =
        {
            new CombatTransitionAuditCase
            {
                CaseId = "stable",
                CompactStateFingerprint = "compact-stable",
                FullStateHash = "full-stable",
                ActionFingerprint = "end-turn",
                NextCompactStateFingerprint = "next-stable",
                NextFullStateHash = "next-full-stable",
                Outcome = "continue",
                RuntimeSettlementHash = "same",
                SimulationSettlementHash = "same"
            }
        }
    };
    File.WriteAllText(auditPath, JsonSerializer.Serialize(passingAudit));
    var auditHash = Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(auditPath))).ToLowerInvariant();
    var contentManifest = new CombatContentPackage
    {
        OwnerModId = "Tests.Content",
        PackageId = "tests-content",
        PackageVersion = "1.0.0",
        GameBuild = "2026.08",
        Artifacts = new CombatContentPackageArtifacts
        {
            TransitionAudit = new CombatContentArtifactReference
            {
                Path = "transition-audit.json",
                Sha256 = auditHash
            }
        },
        PublicFeatures =
        {
            new CombatContentPublicFeatureDeclaration
            {
                Name = "tests.charge",
                Scope = "state",
                Minimum = 0d,
                Maximum = 10d,
                DefaultValue = 0d
            }
        }
    };
    var manifestPath = Path.Combine(contentPackageRoot, "package.json");
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(contentManifest));
    var loadedContent = CombatContentPackageLoader.Load(
        contentPackageRoot,
        "Tests.Content",
        "tests-content");
    Assert(loadedContent.Success
           && loadedContent.Loaded?.TransitionAuditReport.Passed == true,
        "content package loader accepts exact owner id hash and passing audit");
    var firstFingerprint = loadedContent.Loaded!.PackageFingerprint;
    File.WriteAllText(
        manifestPath,
        JsonSerializer.Serialize(
            contentManifest,
            new JsonSerializerOptions { WriteIndented = true }));
    var reformattedContent = CombatContentPackageLoader.Load(
        contentPackageRoot,
        "Tests.Content",
        "tests-content");
    Assert(reformattedContent.Success
           && reformattedContent.Loaded!.PackageFingerprint == firstFingerprint,
        "content package identity ignores JSON whitespace and property formatting");
    contentManifest.PackageVersion = "1.0.1";
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(contentManifest));
    var changedContent = CombatContentPackageLoader.Load(
        contentPackageRoot,
        "Tests.Content",
        "tests-content");
    Assert(changedContent.Success
           && changedContent.Loaded!.PackageFingerprint != firstFingerprint,
        "content package fingerprint binds the complete manifest");
    contentManifest.FoundationTrainingEnabled = true;
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(contentManifest));
    var unknownCoverageContent = CombatContentPackageLoader.Load(
        contentPackageRoot,
        "Tests.Content",
        "tests-content");
    Assert(!unknownCoverageContent.Success
           && unknownCoverageContent.Errors.Any(error => error.Contains(
               "authoritative entity coverage", StringComparison.Ordinal)),
        "foundation content requires authoritative declared entity coverage");
    contentManifest.FoundationTrainingEnabled = false;
    contentManifest.Artifacts.TransitionAudit!.Sha256 = auditHash.ToUpperInvariant();
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(contentManifest));
    var uppercaseDigestContent = CombatContentPackageLoader.Load(
        contentPackageRoot,
        "Tests.Content",
        "tests-content");
    Assert(!uppercaseDigestContent.Success
           && uppercaseDigestContent.Errors.Any(error =>
               error.Contains("lowercase SHA-256", StringComparison.Ordinal)),
        "content package loader rejects non-canonical artifact digests");
    contentManifest.Artifacts.TransitionAudit = new CombatContentArtifactReference
    {
        Path = "../outside.json",
        Sha256 = auditHash
    };
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(contentManifest));
    var escapingContent = CombatContentPackageLoader.Load(
        contentPackageRoot,
        "Tests.Content",
        "tests-content");
    Assert(!escapingContent.Success
           && escapingContent.Errors.Any(error => error.Contains(
               "escapes package root", StringComparison.Ordinal)),
        "content package loader rejects artifacts outside the canonical directory");

    var packageA = loadedContent.Loaded!;
    var packageB = new CombatContentLoadedPackage
    {
        Package = new CombatContentPackage
        {
            OwnerModId = "Tests.Second",
            PackageId = "second",
            PackageVersion = "2.0.0",
            GameBuild = "2026.08"
        },
        PackageFingerprint = "bbbb"
    };
    var orderedContentSet = CombatContentSetProtocol.Create(
        new[] { packageA, packageB }, "2026.08");
    var reversedContentSet = CombatContentSetProtocol.Create(
        new[] { packageB, packageA }, "2026.08");
    Assert(orderedContentSet.ContentSetHash == reversedContentSet.ContentSetHash
           && orderedContentSet.OwnerModSetHash == reversedContentSet.OwnerModSetHash,
        "content set identity is deterministic across registration order");

    var conflictingPackage = new CombatContentLoadedPackage
    {
        Package = new CombatContentPackage
        {
            OwnerModId = "Tests.Content",
            FoundationTrainingEnabled = true
        },
        Ruleset = new CombatRulesetDocument
        {
            Cards =
            {
                new CombatCardDefinition
                {
                    CardId = "base-card",
                    OwnerModId = "Tests.Content"
                }
            }
        },
        FoundationOverlay = new CombatContentFoundationOverlay(),
        TransitionAuditReport = new CombatTransitionAuditReport { CaseCount = 1 }
    };
    var mergeRejected = false;
    try
    {
        CombatContentFoundationMerger.MergeRulesets(
            new CombatRulesetDocument
            {
                Cards = { new CombatCardDefinition { CardId = "base-card" } }
            },
            new[] { conflictingPackage });
    }
    catch (InvalidDataException)
    {
        mergeRejected = true;
    }
    Assert(mergeRejected,
        "content foundation merge rejects identity collisions with the base ruleset");
}
finally
{
    Directory.Delete(contentPackageRoot, recursive: true);
}

var lowRankAdapter = new CombatLowRankPolicyAdapterDefinition
{
    Manifest = new CombatDecisionAdapterManifest
    {
        AdapterId = "tests-content-adapter",
        AdapterKind = CombatModelAdapterProtocol.ContentKind,
        OwnerModId = "Tests.Content",
        PackageId = "tests-content",
        BaseModelId = "recording-policy-value",
        MaximumPolicyDelta = 0.5d
    },
    StateDimensions = 16,
    ActionDimensions = 16,
    Rank = 1,
    StateFactors = new double[16],
    ActionFactors = new double[16],
    RankWeights = new[] { 1d },
    Bias = 0.5d
};
Assert(CombatModelAdapterValidator.TryValidate(
        lowRankAdapter,
        "recording-policy-value",
        CombatContentSetProtocol.EmptyContentSetHash,
        out _),
    "content low-rank adapter validates explicit package and base-model binding");
var adaptedPolicyModel = new AdaptedCombatPolicyValueModel(
    new RecordingPolicyValueModel(),
    new[] { lowRankAdapter });
var adaptedPrediction = adaptedPolicyModel.Evaluate(new CombatPolicyValueInput
{
    Candidates =
    {
        new CombatPolicyValueCandidate { CandidateId = "adapted-action" }
    }
});
Assert(Math.Abs(adaptedPrediction.PolicyLogits["adapted-action"] - 2.5d)
       < 0.000000001d
       && adaptedPolicyModel.AdapterIds.SequenceEqual(
           new[] { "tests-content-adapter" }),
    "content low-rank adapter adds a bounded residual without replacing the base model");
var personalAdapterBinding = new CombatDecisionAdapterManifest
{
    AdapterId = "tests-personal",
    AdapterKind = CombatModelAdapterProtocol.PersonalKind,
    OwnerModId = "AuraToolsExp",
    BaseModelId = "recording-policy-value",
    ContentSetHash = CombatContentSetProtocol.EmptyContentSetHash,
    AdjustsActionValue = true,
    MaximumActionValueDelta = 0.1d
};
Assert(!CombatModelAdapterValidator.TryValidate(
        personalAdapterBinding,
        "recording-policy-value",
        CombatContentSetProtocol.EmptyContentSetHash,
        out _),
    "personal preference adapter cannot alter authoritative action Q outputs");
personalAdapterBinding.AdapterKind = "unrecognized-adapter";
personalAdapterBinding.AdjustsActionValue = false;
personalAdapterBinding.MaximumActionValueDelta = 0d;
Assert(!CombatModelAdapterValidator.TryValidate(
        personalAdapterBinding,
        "recording-policy-value",
        CombatContentSetProtocol.EmptyContentSetHash,
        out _),
    "adapter protocol rejects unknown adapter kinds");

var worldState = new CombatStateObservation
{
    ObservationId = "world-observation",
    BattleSessionId = 77,
    Sequence = 9,
    Fingerprint = "public-fingerprint",
    CurrentPower = 2,
    MaxPower = 3,
    HandCount = 1,
    Player = new CombatUnitObservation
    {
        RuntimeId = 1,
        DefinitionId = "career_world",
        CurrentHp = 18,
        MaxHp = 24,
        Defend = 3,
        Statuses =
        {
            new CombatStatusObservation { StatusId = "buff_world", Level = 2 }
        }
    },
    Friendlies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 2,
            DefinitionId = "familiar_world",
            CurrentHp = 10,
            MaxHp = 10
        }
    },
    Enemies =
    {
        new CombatUnitObservation
        {
            RuntimeId = 3,
            DefinitionId = "enemy_world",
            CurrentHp = 12,
            MaxHp = 20,
            Defend = 1
        }
    },
    HandCards =
    {
        new CombatCardInstanceObservation
        {
            RuntimeId = 101,
            CardId = "card_world",
            EffectiveCost = 1,
            EnhancementCount = 1
        }
    },
    HandCardIds = { "card_world" },
    DiscardPileCardIds = { "card_discard", "card_discard" },
    ExhaustPileCardIds = { "card_exhaust" },
    DeckKnowledge = new CombatDeckKnowledge
    {
        DrawPileCount = 5,
        KnownTopCardIds = { "card_top" },
        KnownBottomCardIds = { "card_bottom" },
        ShuffleEpoch = 2
    },
    Actions =
    {
        new CombatActionObservation
        {
            CandidateId = "play-world",
            SourceId = "card_world",
            RuntimeId = 101,
            Kind = CombatActionKind.PlayCard,
            TargetKind = CombatTargetKind.Enemy,
            TargetRuntimeId = 3,
            Cost = 1,
            Legal = true,
            SemanticFidelity = CombatKnowledgeFidelity.Authoritative,
            Semantics = new CombatActionSemantics { Damage = 6d }
        },
        new CombatActionObservation
        {
            CandidateId = "skill-world",
            SourceId = "skill_world",
            Kind = CombatActionKind.UseSkill,
            TargetKind = CombatTargetKind.Self,
            TargetRuntimeId = 1,
            Legal = true,
            Semantics = new CombatActionSemantics { Buff = 1d }
        }
    }
};
var worldEnvelope = CombatWorldModelTokenizer.Build(worldState);
Assert(worldEnvelope.Protocol == CombatWorldModelProtocol.ObservationProtocol
       && worldEnvelope.Tokens.Any(item => item.Kind == CombatObjectTokenKind.Role)
       && worldEnvelope.Tokens.Any(item => item.Kind == CombatObjectTokenKind.Familiar)
       && worldEnvelope.Tokens.Any(item => item.Kind == CombatObjectTokenKind.Enemy)
       && worldEnvelope.Tokens.Any(item => item.Kind == CombatObjectTokenKind.Status)
       && worldEnvelope.Tokens.Any(item => item.Kind == CombatObjectTokenKind.HandCard)
       && worldEnvelope.Tokens.Any(item => item.Kind == CombatObjectTokenKind.DrawBelief)
       && worldEnvelope.Tokens.Any(item => item.Kind == CombatObjectTokenKind.Resource)
       && worldEnvelope.Coverage.Stage("actions") == CombatCoverageStage.Encoded,
    "world-model tokenizer emits typed public object tokens and coverage");
var worldCardAction = worldEnvelope.LegalActions.Single(item =>
    item.CandidateId == "play-world");
var worldSkillAction = worldEnvelope.LegalActions.Single(item =>
    item.CandidateId == "skill-world");
Assert(worldCardAction.CardInstanceBound
       && !worldCardAction.SkillLifecycleBound
       && worldCardAction.SourceZone == "hand"
       && worldSkillAction.SkillLifecycleBound
       && !worldSkillAction.CardInstanceBound
       && worldSkillAction.SourceZone == "skill",
    "typed action envelope preserves separate card and skill lifecycles");
var requiredWorldTokens = worldEnvelope.Tokens.Count(item => item.Kind is
    CombatObjectTokenKind.Global
    or CombatObjectTokenKind.Role
    or CombatObjectTokenKind.Familiar
    or CombatObjectTokenKind.Friendly
    or CombatObjectTokenKind.Enemy
    or CombatObjectTokenKind.EnemyIntent
    or CombatObjectTokenKind.HandCard
    or CombatObjectTokenKind.Resource
    or CombatObjectTokenKind.DeferredEffect
    or CombatObjectTokenKind.ActionCandidate);
var encodedWorldTokens = CombatWorldModelTokenEncoding.Encode(
    worldEnvelope,
    48,
    maximumTokens: 1);
Assert(encodedWorldTokens.Length >= requiredWorldTokens
       && encodedWorldTokens.All(item => item.Length == 48),
    "object-token encoding never truncates decision-critical public objects");

var campaignEnvelope = CombatCampaignWorldModelTokenizer.Build(
    new CombatCampaignState
    {
        WorldSeed = 123,
        CurrentLayer = 4,
        CurrentGameLevel = 2,
        CurrentHp = 31,
        MaxHp = 40,
        Money = 80,
        Attributes = { ["Strength"] = 3 },
        Deck = { "card_world", "card_world", "card_guard" },
        ReserveCards = { "card_reserve" },
        Relics = { "relic_world" },
        Blessings = { "blessing_world" },
        BuildPlan = new CombatCampaignBuildPlan
        {
            LayerNumber = 4,
            FocusStrategyId = "doom-control",
            FeatureWeights = { ["debuff"] = 1.5d }
        }
    });
Assert(campaignEnvelope.Tokens.Any(item =>
           item.Kind == CombatObjectTokenKind.CampaignDeckCard
           && item.DefinitionId == "card_world"
           && item.Count == 2)
       && campaignEnvelope.Tokens.Any(item =>
           item.Kind == CombatObjectTokenKind.CampaignRelic)
       && campaignEnvelope.Tokens.Any(item =>
           item.Kind == CombatObjectTokenKind.BuildGoal),
    "campaign tokenizer preserves deck composition, relics and build goal");

var governanceCandidate = new CombatCandidateEvaluation
{
    Action = worldState.Actions[0],
    Legal = true,
    RuleScore = 1d,
    SearchDeathRisk = 0.01d
};
var governanceVerdict = CombatDecisionGovernance.ReviewSearch(
    worldState,
    new[] { governanceCandidate },
    new CombatEndTurnAssessment { Prohibited = true },
    new CombatSearchResult
    {
        StoppedByTime = true,
        Confidence = 0.1d
    },
    new CombatDecisionProfile { MinimumSearchConfidence = 0.5d });
Assert(governanceVerdict.Decision == CombatGovernanceDecision.UseSafeFallback
       && ReferenceEquals(governanceVerdict.Candidate, governanceCandidate),
    "governance returns a legal non-end-turn fallback on a low-confidence deadline");

var transformerOptions = new CombatTransformerTeacherOptions().Normalized();
Assert(transformerOptions.Layers == 6
       && transformerOptions.HiddenDimensions == 384
       && transformerOptions.AttentionHeads == 8
       && transformerOptions.FeedForwardDimensions == 1536
       && transformerOptions.EstimatedEncoderParameters() >= 10_000_000
       && transformerOptions.EstimatedEncoderParameters() <= 100_000_000,
    "six-layer Transformer defaults stay inside the approved parameter range");

var transformerAdapter = new CombatTransformerLoRAAdapterDefinition
{
    Manifest = new CombatTransformerAdapterManifest
    {
        AdapterId = "tests-transformer-content",
        AdapterKind = CombatModelAdapterProtocol.TransformerContentKind,
        OwnerModId = "Tests.Content",
        PackageId = "tests-content",
        BaseModelId = "tests-world-model",
        BaseModelHash = new string('a', 64),
        ContentSetHash = CombatContentSetProtocol.EmptyContentSetHash,
        OwnerModSetHash = CombatContentSetProtocol.EmptyOwnerModSetHash,
        TrainingDataHash = new string('b', 64),
        AdapterWeightHash = new string('c', 64),
        SupportedContentIds = { "Tests.Content:card_world" },
        ValidationMetrics = { ["base-regression"] = 0d }
    },
    Matrices =
    {
        new CombatTransformerLoRAMatrix
        {
            TargetModule = "battle.encoder.3.attention.q_proj",
            InputDimensions = 4,
            OutputDimensions = 4,
            Rank = 2,
            Alpha = 4d,
            A = new[] { 1d, 0d, 0d, 0d, 0d, 0d, 0d, 0d },
            B = new[] { 1d, 0d, 0d, 0d, 0d, 0d, 0d, 0d }
        }
    }
};
Assert(CombatTransformerAdapterValidator.TryValidate(
        transformerAdapter,
        "tests-world-model",
        new string('a', 64),
        CombatContentSetProtocol.EmptyContentSetHash,
        out _),
    "Transformer LoRA v2 validates base, content, schema, target and tensor binding");
var transformerCacheKeyA = CombatTransformerAdapterValidator.BuildMergeCacheKey(
    new string('a', 64),
    new[] { transformerAdapter },
    "cpu",
    "int8");
var transformerCacheKeyB = CombatTransformerAdapterValidator.BuildMergeCacheKey(
    new string('a', 64),
    new[] { transformerAdapter },
    "CPU",
    "INT8");
Assert(transformerCacheKeyA == transformerCacheKeyB
       && transformerCacheKeyA.Length == 64,
    "Transformer LoRA merge cache identity is deterministic across backend casing");
var transformerComposition = CombatTransformerAdapterComposition.Compose(
    new[] { transformerAdapter },
    "tests-world-model",
    new string('a', 64),
    CombatContentSetProtocol.EmptyContentSetHash,
    CombatContentSetProtocol.EmptyOwnerModSetHash,
    "cpu",
    "int8");
var mergedTransformerWeights = CombatTransformerLoRAMerger.MergeModule(
    new double[16],
    4,
    4,
    "battle.encoder.3.attention.q_proj",
    transformerComposition.ActiveAdapters,
    new[] { "Tests.Content:card_world" });
Assert(transformerComposition.ActiveAdapters.Count == 1
       && transformerComposition.RejectedAdapters.Count == 0
       && transformerComposition.MergeCacheKey == transformerCacheKeyA
       && Math.Abs(mergedTransformerWeights[0] - 2d) < 0.000001d,
    "Transformer LoRA composition validates and premerges active content deterministically");
transformerAdapter.Manifest.AdapterKind =
    CombatModelAdapterProtocol.TransformerPreferenceKind;
Assert(!CombatTransformerAdapterValidator.TryValidate(
        transformerAdapter,
        "tests-world-model",
        new string('a', 64),
        CombatContentSetProtocol.EmptyContentSetHash,
        out _),
    "preference LoRA cannot modify non-actor Transformer modules");

var performanceTelemetry = CombatDecisionPerformanceTelemetry.FromSearch(
    new CombatSearchResult
    {
        ElapsedMilliseconds = 123d,
        Simulations = 64,
        Nodes = 128,
        ModelEvaluations = 32,
        ModelCacheHits = 7,
        OriginalCandidateCount = 18,
        CandidateCount = 10,
        StoppedByModelBudget = true
    });
Assert(performanceTelemetry.TotalMilliseconds == 123d
       && performanceTelemetry.ModelEvaluations == 32
       && performanceTelemetry.ModelCacheHits == 7
       && performanceTelemetry.StopReason == "model-evaluation-budget",
    "decision telemetry preserves model-call budget and cache diagnostics");

using (CombatAiRegistry.RegisterSkillTimingProvider(
           "tests",
           "fixed-skill-timing",
           new FixedSkillTimingProvider(),
           10))
{
    var timingSnapshot = CombatAiRegistry.SnapshotDecisionPreparation();
    var timingState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_test",
            CurrentHp = 20,
            MaxHp = 20
        },
        Actions =
        {
            new CombatActionObservation
            {
                CandidateId = "registered-skill",
                SourceId = "skill_test",
                Kind = CombatActionKind.UseSkill
            }
        }
    };
    Assert(timingSnapshot.SkillTimingProviderCount == 1
           && timingSnapshot.EnrichSkillTimings(timingState)
           && timingState.Actions[0].Features.GetValueOrDefault(
               CombatSkillTimingFeatureNames.PositiveOpportunity) == 1d,
        "isolated preparation snapshot freezes registered skill timing providers");
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

CombatStateObservation BuildHandTransformFixture(bool valuableHand)
{
    var handIds = valuableHand
        ? new[] { "engine", "enhanced-strike", "cycle" }
        : new[] { "curse", "blank", "risky" };
    var state = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            Kind = CombatTargetKind.Self,
            CurrentHp = 35,
            MaxHp = 40
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 200,
                MaxHp = 200
            }
        },
        CurrentPower = 3,
        MaxPower = 3,
        HandCount = handIds.Length,
        HandCardIds = handIds.ToList(),
        DeckCardIds = handIds.ToList(),
        DeckKnowledge = new CombatDeckKnowledge(),
        IsPlayerActionWindow = true
    };
    var transform = new CombatActionObservation
    {
        CandidateId = "transform",
        SourceId = "careercard_4",
        Kind = CombatActionKind.UseSkill,
        Semantics = new CombatActionSemantics
        {
            OpensInteraction = true,
            HandTransform = new CombatHandTransformSemantic
            {
                TargetCardId = "nocard_1",
                TargetCardSemantics = new CombatActionSemantics
                {
                    Damage = 10d,
                    Defend = 10d,
                    AffectedEnemyCount = 1
                },
                TransformAllHandCards = true,
                PreserveInstances = true,
                ClearsEnhancements = true,
                ClearsVariables = true,
                TargetRetained = true,
                TargetExhaustsOnUse = true,
                GrowthStateKey = "playerStatus:buff_Soul",
                GrowthPerExhaust = 1d,
                CurrentGrowthValue = 50d,
                TargetTier = 1,
                NextTierThreshold = 100,
                CooldownProgressRequired = 12d
            }
        }
    };
    state.Actions.Add(transform);
    for (var index = 0; index < handIds.Length; index++)
    {
        var semantics = valuableHand
            ? index == 0
                ? new CombatActionSemantics
                {
                    Draw = 3d,
                    EnergyGain = 2d,
                    CardGeneration = 1d
                }
                : index == 1
                    ? new CombatActionSemantics { Damage = 30d }
                    : new CombatActionSemantics
                    {
                        Draw = 2d,
                        EnergyGain = 1d
                    }
            : index == 0
                ? new CombatActionSemantics
                {
                    Risk = 8d,
                    SelfHpLoss = 2d
                }
                : index == 1
                    ? new CombatActionSemantics()
                    : new CombatActionSemantics
                    {
                        Risk = 5d,
                        EndOfCycleSelfHpLoss = 3d
                    };
        var action = new CombatActionObservation
        {
            CandidateId = "card-" + index,
            SourceId = handIds[index],
            RuntimeId = 100 + index,
            Kind = CombatActionKind.PlayCard,
            Semantics = semantics
        };
        if (valuableHand && index == 0)
        {
            action.Features["strategyInfinite"] = 1d;
            action.Features["strategyExecutable"] = 1d;
        }
        state.Actions.Add(action);
        state.HandCards.Add(new CombatCardInstanceObservation
        {
            RuntimeId = action.RuntimeId,
            CardId = action.SourceId,
            EnhancementCount = valuableHand && index == 1 ? 2 : 0
        });
    }
    if (!valuableHand)
    {
        state.CardTagsById["curse"] = new List<string> { "Curse" };
        state.CardTagsById["blank"] = new List<string> { "Unusable" };
    }
    return state;
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
    foreach (var thresholdReward in new[]
             {
                 ("Strength", 10, "blessing_101"),
                 ("Strength", 20, "blessing_105"),
                 ("Strength", 30, "blessing_109"),
                 ("Strength", 40, "blessing_113"),
                 ("Lucky", 10, "blessing_102"),
                 ("Lucky", 20, "blessing_106"),
                 ("Lucky", 30, "blessing_110"),
                 ("Lucky", 40, "blessing_114"),
                 ("Perceive", 10, "blessing_104"),
                 ("Perceive", 20, "blessing_108"),
                 ("Perceive", 30, "blessing_112"),
                 ("Perceive", 40, "blessing_116"),
                 ("Wisdom", 10, "blessing_103"),
                 ("Wisdom", 20, "blessing_107"),
                 ("Wisdom", 30, "blessing_111"),
                 ("Wisdom", 40, "blessing_115")
             })
    {
        result.AttributeThresholdRewards.Add(
            new CombatCampaignAttributeThresholdRewardDefinition
            {
                AttributeId = thresholdReward.Item1,
                Threshold = thresholdReward.Item2,
                RewardId = thresholdReward.Item3
            });
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = thresholdReward.Item3,
            Kind = CombatCampaignRewardKind.Blessing,
            Tier = thresholdReward.Item2 / 10,
            Fidelity = CombatRuleFidelity.Authoritative
        });
    }
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

sealed class PreserveSurplusThenFinishPolicy : ICombatSimulationPolicy
{
    private bool charged;

    public string PolicyId => "tests:preserve-surplus-then-finish";

    public int SecondTurnEnergy { get; private set; } = -1;

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (context.State.Turn <= 1 && !charged)
        {
            var charge = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    "charge-surplus",
                    StringComparison.OrdinalIgnoreCase));
            if (charge != null)
            {
                charged = true;
                return charge;
            }
        }
        if (context.State.Turn >= 2)
        {
            SecondTurnEnergy = context.State.Player?.Energy ?? -1;
            var finish = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    "finish-after-charge",
                    StringComparison.OrdinalIgnoreCase));
            if (finish != null)
            {
                return finish;
            }
        }
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class PlayCardsInOrderThenEndPolicy : ICombatSimulationPolicy
{
    private readonly Queue<string> cardIds;

    public PlayCardsInOrderThenEndPolicy(params string[] cardIds)
    {
        this.cardIds = new Queue<string>(cardIds);
    }

    public string PolicyId => "tests:play-in-order-then-end";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (cardIds.Count > 0)
        {
            var selected = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    cardIds.Peek(),
                    StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                cardIds.Dequeue();
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

sealed class FrozenPreparationSemanticProvider : ICombatSemanticProvider
{
    private readonly string sourceId;

    public FrozenPreparationSemanticProvider(string sourceId)
    {
        this.sourceId = sourceId;
    }

    public bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionSemantics semantics)
    {
        semantics = new CombatActionSemantics { Buff = 17d };
        return string.Equals(
            action.SourceId,
            sourceId,
            StringComparison.OrdinalIgnoreCase);
    }
}

sealed class CountingSemanticProvider : ICombatSemanticProvider
{
    private readonly string sourceId;
    private int callCount;

    public CountingSemanticProvider(string sourceId)
    {
        this.sourceId = sourceId;
    }

    public int CallCount => Volatile.Read(ref callCount);

    public bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionSemantics semantics)
    {
        Interlocked.Increment(ref callCount);
        semantics = new CombatActionSemantics { Buff = 19d };
        return string.Equals(
            action.SourceId,
            sourceId,
            StringComparison.OrdinalIgnoreCase);
    }
}

sealed class FrozenPreparationRoleStrategyProvider :
    ICombatRoleStrategyProvider
{
    public bool TryEnrich(CombatStateObservation state)
    {
        foreach (var action in state.Actions)
        {
            action.Features[CombatRoleStrategyFeatureNames.Active] = 1d;
        }
        return true;
    }
}

sealed class ProhibitSourceRoleStrategyProvider : ICombatRoleStrategyProvider
{
    private readonly string sourceId;

    public ProhibitSourceRoleStrategyProvider(string sourceId)
    {
        this.sourceId = sourceId;
    }

    public bool TryEnrich(CombatStateObservation state)
    {
        state.Features["roleStrategy:test.prepared-state"] = 1d;
        foreach (var action in state.Actions.Where(action => string.Equals(
                     action.SourceId,
                     sourceId,
                     StringComparison.OrdinalIgnoreCase)))
        {
            action.Features[
                CombatRoleStrategyFeatureNames.StrategicallyProhibited] = 1d;
        }
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

sealed class RecordingPolicyValueModel : ICombatPolicyValueModel
{
    public string ModelId => "recording-policy-value";

    public CombatPolicyValueInput? LastInput { get; private set; }

    public CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input)
    {
        LastInput = input;
        var result = new CombatPolicyValuePrediction
        {
            ExpectedReturn = 0.75d
        };
        foreach (var candidate in input.Candidates)
        {
            result.PolicyLogits[candidate.CandidateId] = 2d;
        }
        return result;
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        return inputs.Select(Evaluate).ToList();
    }
}

sealed class FixedSkillTimingProvider : ICombatSkillTimingProvider
{
    public bool TryEnrich(CombatStateObservation state)
    {
        var action = state.Actions.FirstOrDefault(item =>
            item.Kind == CombatActionKind.UseSkill
            && item.SourceId == "skill_test");
        if (action == null)
        {
            return false;
        }
        action.Features[CombatSkillTimingFeatureNames.Active] = 1d;
        action.Features[CombatSkillTimingFeatureNames.OngoingEffectValue] = 2d;
        CombatSkillTimingPolicy.Enrich(action);
        return true;
    }
}
