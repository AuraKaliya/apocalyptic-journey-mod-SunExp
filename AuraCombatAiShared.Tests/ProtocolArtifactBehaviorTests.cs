using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

internal static class CombatAiProtocolArtifactBehaviorTests
{
    public static void Run(CombatAiTrainingTestContext context)
    {
        var concurrentFeatureName = "checkpoint-concurrent-feature-"
                                    + Guid.NewGuid().ToString("N");
        System.Threading.Tasks.Parallel.For(
            0,
            512,
            _ => CombatFeatureTokenRegistry.GetToken(concurrentFeatureName));
        Assert(CombatFeatureTokenRegistry.CaptureCatalog().Values.Count(name =>
                   string.Equals(
                       name,
                       concurrentFeatureName,
                       StringComparison.OrdinalIgnoreCase)) == 1,
            "concurrent compact feature allocation publishes exactly one token");

        var existingFeatureTokens =
            CombatFeatureTokenRegistry.CaptureCatalog();
        var aliasToken = existingFeatureTokens.Count == 0
            ? 1
            : existingFeatureTokens.Keys.Max() + 1;
        var aliasFeatureName = "checkpoint-alias-feature-"
                               + Guid.NewGuid().ToString("N");
        CombatFeatureTokenRegistry.RegisterCatalog(
            new Dictionary<int, string>
            {
                [aliasToken] = aliasFeatureName,
                [aliasToken + 1] = aliasFeatureName
            });
        var aliasVector = new CombatCompactFeatureVector(
            new[] { aliasToken + 1 },
            new[] { 7.5f });
        Assert(CombatFeatureTokenRegistry.TryGetToken(
                   aliasFeatureName,
                   out var canonicalAliasToken)
               && canonicalAliasToken == aliasToken
               && CombatFeatureTokenRegistry.TryResolve(
                   aliasToken + 1,
                   out var resolvedAliasName)
               && string.Equals(
                   resolvedAliasName,
                   aliasFeatureName,
                   StringComparison.OrdinalIgnoreCase)
               && aliasVector.TryGetValue(aliasFeatureName, out var aliasValue)
               && Math.Abs(aliasValue - 7.5d) < 1e-6d,
            "checkpoint feature catalogs retain duplicate-name token aliases without losing compact values");

        var simulationEngine = context.Simulation.Engine;
        var simulationRules = context.Simulation.Rules;
        var bundledRulesV2 = context.Simulation.BundledRules;
        var episodes = context.Episodes;
        var policyValueTraining = context.PolicyValueTraining;
        var policyValueModel = context.PolicyValueModel;
        var reusableState = context.ReusableState;
        var reusableCandidates = context.ReusableCandidates;
        var campaign = context.Campaign;
        var campaignRules = context.CampaignRules;
        var foundationTraining = context.FoundationTraining;
        var foundationPackage = context.FoundationPackage;
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
        var hiddenSeedBasis =
            CombatPublicObservationHasher.CreateSeedBasis(hiddenOrderA);
        Assert(hiddenSampleSeed
               == CombatPublicObservationHasher.Seed(hiddenSeedBasis, 7),
            "cached public-observation seed basis preserves determinization seeds");
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
        var migratableEpisode = new CombatEpisode
        {
            ModelProtocol = CombatPolicyValueProtocol.PreviousEpisodeProtocol,
            FeatureSchemaVersion = CombatPolicyValueProtocol.FeatureSchemaVersion,
            EpisodeId = "v6-decision-sequence-migration",
            Frames =
            {
                new CombatEpisodeFrame
                {
                    Turn = 1,
                    ActionSequence = 5,
                    BattleSessionId = 9001,
                    StateFingerprint = "migration-before",
                    ExecutedCandidateId = "attack",
                    StateFeatures = { ["expectedIncomingDamage"] = 9d },
                    Candidates =
                    {
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "attack",
                            Legal = true
                        },
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "defend",
                            Legal = true,
                            Features = { ["effectiveDefend"] = 8d }
                        }
                    }
                },
                new CombatEpisodeFrame
                {
                    Turn = 2,
                    ActionSequence = 5,
                    BattleSessionId = 9001,
                    StateFingerprint = "migration-after",
                    ExecutedCandidateId = "attack",
                    StateFeatures = { ["playerHp"] = 11d },
                    Candidates =
                    {
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "attack",
                            Legal = true
                        }
                    }
                }
            }
        };
        Assert(CombatPolicyValueEpisodeMigration.UpgradeInPlace(
                   migratableEpisode)
               && migratableEpisode.ModelProtocol
                  == CombatPolicyValueProtocol.EpisodeProtocol
               && migratableEpisode.Frames[0].DecisionSequence == 1
               && migratableEpisode.Frames[1].DecisionSequence == 2
               && migratableEpisode.Frames[0].TransitionValid
               && migratableEpisode.Frames[0].TransitionActionSequenceDelta == 0
               && migratableEpisode.Frames[0].TransitionKind
                  == CombatEpisodeTransitionProtocol.CrossTurn
               && migratableEpisode.Frames[0].StrategyApplicabilityKnown
               && migratableEpisode.Frames[0].StrategyApplicableLabels
                   .SequenceEqual(new[] { "survival" })
               && migratableEpisode.Frames[0].StrategyLabels.Count == 0
               && migratableEpisode.Frames[1].Terminal
               && !migratableEpisode.Frames[1].TransitionKnown,
            "v6 replay migration reconstructs the decision clock, cross-turn transitions, terminal contract, and applicable negative strategy supervision without discarding the checkpoint");
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
        contentEpisodeJob.RequiredCheckpointFingerprint = "invalid";
        Assert(!CombatFoundationWorkerProtocol.TryValidateJob(
                contentEpisodeJob,
                out _),
            "iteration-boundary checkpoint handoff rejects malformed fingerprints");
        contentEpisodeJob.RequiredCheckpointFingerprint = new string('A', 64);
        Assert(CombatFoundationWorkerProtocol.TryValidateJob(
                contentEpisodeJob,
                out _),
            "iteration-boundary checkpoint handoff accepts a complete hexadecimal fingerprint");
        contentEpisodeJob.RequiredCheckpointFingerprint = "";
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
        var processBoundaryReplay = Enumerable.Range(0, 600)
            .Select(index => new CombatEpisode
            {
                EpisodeId = "process-boundary-" + index.ToString("D4"),
                Seed = (ulong)(1000 + index),
                ScenarioId = "process-boundary",
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    DifficultyId = index % 2 == 0 ? "normal" : "advanced"
                },
                Frames = Enumerable.Repeat(
                        new CombatEpisodeFrame(),
                        100)
                    .ToList()
            })
            .ToList();
        var boundedProcessReplay = CombatFoundationReplaySampler
            .SelectProcessBoundary(
                processBoundaryReplay,
                required: Array.Empty<CombatEpisode>(),
                configuredEpisodeLimit: 2048,
                configuredFrameLimit: 96_000,
                configuredEstimatedBytesLimit: 768L * 1024L * 1024L,
                minimumEpisodes: 64,
                stratified: true);
        Assert(boundedProcessReplay.Episodes.Count
                   <= CombatFoundationReplaySampler.ProcessBoundaryEpisodeLimit
               && boundedProcessReplay.Episodes.Sum(episode =>
                   episode.Frames.Count)
               <= CombatFoundationReplaySampler.ProcessBoundaryFrameLimit
               && boundedProcessReplay.Episodes.Sum(
                   CombatFoundationReplaySampler.EstimateResidentBytes)
               <= CombatFoundationReplaySampler
                   .ProcessBoundaryEstimatedBytesLimit,
            "cross-process replay snapshots enforce independent episode, frame and resident-memory caps");

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
        var minimumTransformerReserve =
            new CombatTransformerTeacherOptions
            {
                MemoryReserveBytes = 1L
            }.Normalized();
        Assert(transformerOptions.Layers == 6
               && transformerOptions.HiddenDimensions == 384
               && transformerOptions.AttentionHeads == 8
               && transformerOptions.FeedForwardDimensions == 1536
               && transformerOptions.MemoryReserveBytes
               == 128L * 1024L * 1024L
               && minimumTransformerReserve.MemoryReserveBytes
               == 128L * 1024L * 1024L
               && transformerOptions.EstimatedEncoderParameters() >= 10_000_000
               && transformerOptions.EstimatedEncoderParameters() <= 100_000_000,
            "Transformer defaults retain the approved model size and an independent 128 MiB stage reserve");

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

        var longPathRoot = Path.Combine(
            Path.GetTempPath(),
            "aura-foundation-long-path-tests",
            Guid.NewGuid().ToString("N"));
        var longPathDirectory = longPathRoot;
        while (longPathDirectory.Length < 285)
        {
            longPathDirectory = Path.Combine(
                longPathDirectory,
                "checkpoint-segment-0123456789abcdef");
        }
        var longPathFile = Path.Combine(longPathDirectory, "checkpoint.json");
        try
        {
            CombatFoundationCheckpointStorage.WriteAtomicText(
                longPathFile,
                "{\"ok\":true}",
                retainBackup: false);
            Assert(longPathFile.Length > 260
                   && CombatFoundationPathRuntime.FileExists(longPathFile)
                   && CombatFoundationCheckpointStorage.ReadAllTextShared(longPathFile)
                      == "{\"ok\":true}",
                "generic path runtime supports atomic read/write beyond MAX_PATH");
            Assert(!OperatingSystem.IsWindows()
                   || CombatFoundationPathRuntime.ForExternalProcess(longPathFile)
                       .StartsWith(
                           CombatFoundationPathRuntime.WindowsExtendedPathPrefix,
                           StringComparison.Ordinal),
                "external process paths use the Windows extended-path namespace");
        }
        finally
        {
            if (CombatFoundationPathRuntime.DirectoryExists(longPathRoot))
            {
                Directory.Delete(
                    CombatFoundationPathRuntime.ForFileSystem(longPathRoot),
                    recursive: true);
            }
        }

        var checkpointCandidates = new[]
        {
            new CombatFoundationCheckpointCatalogEntry
            {
                Id = "early",
                CompletedEpochs = 4,
                ValidationLoss = 0.20d,
                Risk = "balanced",
                QualityGatesPassed = true,
                SupportsModelBranch = true,
                SelectionAnchorMetrics = new CombatPolicyValueMetricSnapshot
                {
                    FrameCount = 100,
                    CompositeLoss = 0.205d,
                    CompositeLossStandardError = 0.01d
                }
            },
            new CombatFoundationCheckpointCatalogEntry
            {
                Id = "late",
                CompletedEpochs = 12,
                ValidationLoss = 0.18d,
                Risk = "balanced",
                QualityGatesPassed = true,
                SupportsModelBranch = true,
                SelectionAnchorMetrics = new CombatPolicyValueMetricSnapshot
                {
                    FrameCount = 100,
                    CompositeLoss = 0.20d,
                    CompositeLossStandardError = 0.01d
                }
            }
        };
        Assert(CombatFoundationCheckpointCatalogProtocol.Recommend(
                   checkpointCandidates)?.Id == "early",
            "checkpoint recommendation uses fixed-anchor one-standard-error selection");
        Assert(CombatFoundationCheckpointCatalogProtocol.Risk(
                   0.10d,
                   0.21d,
                   Array.Empty<CombatPolicyValueEpochMetrics>(),
                   out _) == "overfit"
               && CombatFoundationCheckpointResumeModes.Normalize("model-branch")
                  == CombatFoundationCheckpointResumeModes.ModelBranch,
            "checkpoint catalog classifies fit risk and normalizes resume modes");
        var moderateGeneralization = CombatGeneralizationAssessmentProtocol.Assess(
            new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 3186,
                CompositeLoss = 0.09738d,
                CompositeLossCiLower = 0.09085d,
                CompositeLossCiUpper = 0.10391d
            },
            new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 501,
                CompositeLoss = 0.15951d,
                CompositeLossCiLower = 0.13118d,
                CompositeLossCiUpper = 0.18784d
            },
            new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 447,
                CompositeLoss = 0.20211d,
                CompositeLossCiLower = 0.16595d,
                CompositeLossCiUpper = 0.23827d
            });
        Assert(moderateGeneralization.Level == CombatGeneralizationRiskLevels.Watch,
            "generalization assessment classifies the prior run as watch rather than a relative-gap overfit false positive");

        var compactState = CombatPolicyValueEncoding.BuildCompactStateFeatures(
            reusableState);
        var eagerState = CombatPolicyValueEncoding.BuildStateFeatures(reusableState);
        var compactCandidate = CombatPolicyValueEncoding
            .BuildCompactCandidateFeatures(reusableCandidates[0]);
        var eagerCandidate = CombatPolicyValueEncoding.BuildCandidateFeatures(
            reusableCandidates[0]);
        Assert(compactState.Materialize().OrderBy(pair => pair.Key)
                   .SequenceEqual(eagerState.OrderBy(pair => pair.Key))
               && compactCandidate.Materialize().OrderBy(pair => pair.Key)
                   .SequenceEqual(eagerCandidate.OrderBy(pair => pair.Key)),
            "compact numeric state and candidate columns preserve the public feature dictionary exactly");
        var compactStateEncoding = new double[257];
        var eagerStateEncoding = new double[257];
        var compactCandidateEncoding = new double[263];
        var eagerCandidateEncoding = new double[263];
        CombatPolicyValueEncoding.EncodeStateInto(
            compactState,
            compactStateEncoding,
            compactStateEncoding.Length,
            "partitioned-v4");
        CombatPolicyValueEncoding.EncodeStateInto(
            eagerState,
            eagerStateEncoding,
            eagerStateEncoding.Length,
            "partitioned-v4");
        CombatPolicyValueEncoding.EncodeCandidateInto(
            compactCandidate,
            reusableCandidates[0].Action.SourceId,
            compactCandidateEncoding,
            compactCandidateEncoding.Length,
            "partitioned-v4");
        CombatPolicyValueEncoding.EncodeCandidateInto(
            new CombatPolicyValueCandidate
            {
                CandidateId = reusableCandidates[0].Action.CandidateId,
                SourceId = reusableCandidates[0].Action.SourceId,
                Features = eagerCandidate
            },
            eagerCandidateEncoding,
            eagerCandidateEncoding.Length,
            "partitioned-v4");
        Assert(compactStateEncoding.SequenceEqual(eagerStateEncoding)
               && compactCandidateEncoding.SequenceEqual(eagerCandidateEncoding),
            "compact numeric feature columns hash to the same model inputs as compatibility dictionaries");
        var lazyFrame = new CombatEpisodeFrame();
        lazyFrame.SetCompactStateFeatures(compactState);
        lazyFrame.SetCompactTransitionNextStateFeatures(compactState);
        lazyFrame.TransitionKnown = true;
        lazyFrame.TransitionValid = true;
        lazyFrame.TransitionSpan = 1;
        var lazyCandidate = new CombatEpisodeCandidate();
        lazyCandidate.SetCompactFeatures(compactCandidate);
        var storageBeforeMaterialization = CombatEpisodeStorageDiagnostics.Capture();
        Assert(!lazyFrame.HasMaterializedStateFeatures
               && !lazyCandidate.HasMaterializedFeatures
               && !lazyFrame.HasMaterializedTransitionNextStateFeatures
               && lazyFrame.TryGetStateFeature("power", out var lazyPower)
               && lazyPower == reusableState.CurrentPower
               && lazyCandidate.TryGetFeature("cost", out var lazyCost)
               && lazyCost == reusableCandidates[0].Action.Cost,
            "compact episode columns support hot-path feature lookup without materializing dictionaries");
        _ = lazyFrame.StateFeatures;
        _ = lazyFrame.TransitionNextStateFeatures;
        _ = lazyCandidate.Features;
        var storageAfterMaterialization = CombatEpisodeStorageDiagnostics.Capture();
        Assert(storageAfterMaterialization.StateDictionaryMaterializations
                   == storageBeforeMaterialization.StateDictionaryMaterializations + 1
               && storageAfterMaterialization.CandidateDictionaryMaterializations
                  == storageBeforeMaterialization.CandidateDictionaryMaterializations + 1
               && !lazyFrame.HasMaterializedStateFeatures
               && lazyFrame.HasMaterializedTransitionNextStateFeatures
               && !lazyCandidate.HasMaterializedFeatures,
            "compatibility dictionaries are temporary and are not retained beside compact columns");
        lazyFrame.Observation = new CombatObservationEnvelope();
        lazyFrame.Candidates.Add(lazyCandidate);
        lazyFrame.ReleaseTransientStorage();
        Assert(!lazyFrame.HasObservation
               && !lazyFrame.HasMaterializedStateFeatures
               && !lazyFrame.HasMaterializedTransitionNextStateFeatures
               && !lazyCandidate.HasMaterializedFeatures
               && lazyFrame.TryGetStateFeature("power", out _)
               && lazyFrame.CompactTransitionNextStateFeatures?.Count
                  == compactState.Count
               && lazyCandidate.TryGetFeature("cost", out _),
            "cross-round cleanup drops observation graphs while preserving compact training columns");
        var normalizedReusableState = CombatPlayerObservationBoundary.Normalize(
            reusableState);
        Assert(JsonSerializer.Serialize(
                   CombatWorldModelTokenizer.Build(reusableState))
               == JsonSerializer.Serialize(
                   CombatWorldModelTokenizer.BuildNormalizedOwned(
                       normalizedReusableState)),
            "owned normalized world-model tokenization is protocol-equivalent to the public normalization boundary");
        var actionArena = new CombatActionModelArena();
        var arenaAction = reusableCandidates[0].Action;
        var eagerActionModel = CombatForwardModel.Resolve(
            reusableState,
            arenaAction,
            useRegisteredResolvers: false);
        actionArena.BeginSearch();
        var pooledActionModel = CombatForwardModel.Resolve(
            reusableState,
            arenaAction,
            useRegisteredResolvers: false,
            arena: actionArena);
        var pooledActionJson = JsonSerializer.Serialize(pooledActionModel);
        Assert(pooledActionJson == JsonSerializer.Serialize(eagerActionModel)
               && actionArena.ModelCapacity == 1
               && actionArena.OutcomeCapacity >= 1
               && actionArena.EffectCapacity >= pooledActionModel.Outcomes
                   .Sum(outcome => outcome.Effects.Count),
            "reusable action semantic slots preserve forward-model outcomes and effects");
        actionArena.BeginSearch();
        var reusedActionModel = CombatForwardModel.Resolve(
            reusableState,
            arenaAction,
            useRegisteredResolvers: false,
            arena: actionArena);
        Assert(ReferenceEquals(pooledActionModel, reusedActionModel)
               && pooledActionJson == JsonSerializer.Serialize(reusedActionModel),
            "action semantic arena reuses its compiled model graph on the next search");

        const long gib = 1024L * 1024L * 1024L;
        var capacity32 = CombatFoundationParallelismPlanner.Select(
            1,
            32,
            new CombatFoundationResourceSnapshot
            {
                TotalPhysicalMemoryBytes = 64L * gib,
                AvailablePhysicalMemoryBytes = 48L * gib,
                ProcessPrivateMemoryBytes = 10L * gib
            },
            configuredPerLaneBytes: gib,
            configuredReserveBytes: 4L * gib);
        var capacity16 = CombatFoundationParallelismPlanner.Select(
            2,
            32,
            new CombatFoundationResourceSnapshot
            {
                TotalPhysicalMemoryBytes = 64L * gib,
                AvailablePhysicalMemoryBytes = 22L * gib,
                ProcessPrivateMemoryBytes = 12L * gib
            },
            configuredPerLaneBytes: gib,
            configuredReserveBytes: 4L * gib);
        var capacity8 = CombatFoundationParallelismPlanner.Select(
            3,
            32,
            new CombatFoundationResourceSnapshot
            {
                TotalPhysicalMemoryBytes = 64L * gib,
                AvailablePhysicalMemoryBytes = 11L * gib,
                ProcessPrivateMemoryBytes = 14L * gib
            },
            configuredPerLaneBytes: gib,
            configuredReserveBytes: 4L * gib);
        Assert(capacity32.SelectedParallelism == 32
               && capacity16.SelectedParallelism == 18
               && capacity8.SelectedParallelism == 7,
            "memory-capacity planner uses every fitting lane instead of rounding down to fixed tiers");
        var defaultReserveFirstRound =
            CombatFoundationParallelismPlanner.Select(
                1,
                32,
                new CombatFoundationResourceSnapshot
                {
                    TotalPhysicalMemoryBytes = 32L * gib,
                    AvailablePhysicalMemoryBytes = 15L * gib,
                    ProcessPrivateMemoryBytes = 4L * gib
                },
                configuredPerLaneBytes: gib);
        var defaultReserveLaterRound =
            CombatFoundationParallelismPlanner.Select(
                7,
                32,
                new CombatFoundationResourceSnapshot
                {
                    TotalPhysicalMemoryBytes = 32L * gib,
                    AvailablePhysicalMemoryBytes = 15L * gib,
                    ProcessPrivateMemoryBytes = 9L * gib
                },
                configuredPerLaneBytes: gib);
        Assert(defaultReserveFirstRound.MemoryReserveBytes
                   == 128L * 1024L * 1024L
               && defaultReserveFirstRound.SelectedParallelism == 14
               && defaultReserveLaterRound.SelectedParallelism == 14,
            "the default 128 MiB reserve is fixed and prior-round private memory is diagnostic, not deducted twice");
        var arenaBoundCapacity = CombatFoundationParallelismPlanner.Select(
            4,
            32,
            new CombatFoundationResourceSnapshot
            {
                TotalPhysicalMemoryBytes = 64L * gib,
                AvailablePhysicalMemoryBytes = 25L * gib
            },
            new CombatSearchMemoryTrimReport
            {
                PlannerCount = 8,
                ReleasedEstimatedBytes = 8L * gib
            },
            configuredPerLaneBytes: 384L * 1024L * 1024L,
            configuredReserveBytes: 4L * gib);
        Assert(arenaBoundCapacity.SelectedParallelism == 15
               && arenaBoundCapacity.PredictedPerLaneBytes > gib,
            "released search-arena high-water cost participates in the next iteration capacity prediction");
        var searchTrim = CombatRiskAwareRootSamplingPuctPlanner
            .TrimRetainedSearchMemory();
        Assert(searchTrim.PlannerCount >= 0
               && searchTrim.ReleasedEstimatedBytes >= 0L,
            "search memory trim reports releasable arena capacity safely");

    }
}
