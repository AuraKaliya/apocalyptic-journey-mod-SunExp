using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using static CombatAiTestFixtures;

internal static class RuntimeDecisionSafetyBehaviorTests
{
    public static void Run()
    {
        DynamicDamageProjectionMatchesRuntimeVariables();
        ForwardModelUsesObservedDamageVariables();
        StrategicTargetsAreWorthRemovingFirst();
        CompletedLowConfidenceSearchUsesSafeFallback();
        ModelAuthorityDoesNotUseQualityFallback();
        ModelAuthorityKeepsHeuristicallyRejectedAction();
        ModelAuthorityKeepsEquivalentCandidateInstances();
        MinimumSearchTimeRequiresMoreEvidence();
        SearchReportsRawNetworkPrediction();
        FatalAndRepeatableSelfHarmCannotBeOverridden();
        NegativeFallbackRequiresMinimumLossProof();
        SaturatedRiskFallbackRequiresSearchEvidence();
        DynamicInstanceCostAndSuppressionRevisionTrackEligibility();
        RealizedTransitionCarriesHpMaximumHpAndStatusDeltas();
        InteractiveSettlementUsesTransactionDeadline();
    }

    private static void DynamicDamageProjectionMatchesRuntimeVariables()
    {
        Assert(CombatDynamicDamageProjection.ResolveNormal(
                   3d,
                   outgoingMultiplier: 8d,
                   outgoingFlat: 0d,
                   strength: 0d,
                   incomingMultiplier: 1d,
                   incomingFlat: 0d,
                   applyStrength: false) == 24,
            "dynamic enemy PercentDamage is reflected in threat damage");
        Assert(CombatDynamicDamageProjection.ResolveNormal(
                   10d,
                   outgoingMultiplier: 1d,
                   outgoingFlat: 0d,
                   strength: 10d,
                   incomingMultiplier: 1d,
                   incomingFlat: 0d,
                   applyStrength: true) == 13,
            "player Strength uses the game's three-percent damage scaling");
        Assert(CombatDynamicDamageProjection.ResolveNormal(
                   10d,
                   outgoingMultiplier: 1d,
                   outgoingFlat: 0d,
                   strength: 0d,
                   incomingMultiplier: 1.5d,
                   incomingFlat: 2d,
                   applyStrength: false) == 18,
            "target incoming flat and percent damage modifiers preserve game order");
        Assert(CombatDynamicDamageProjection.ResolveTrue(5d, 2d) == 10,
            "true damage uses the public TruePercentDamage multiplier");
    }

    private static void ForwardModelUsesObservedDamageVariables()
    {
        var action = new CombatActionObservation
        {
            CandidateId = "dynamic-strike",
            SourceId = "dynamic-strike",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 7,
            Semantics = new CombatActionSemantics
            {
                Damage = 4d,
                HitCount = 2d
            }
        };
        var observation = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                CurrentHp = 30,
                MaxHp = 30,
                Features =
                {
                    [CombatDynamicDamageProjection.PercentDamage] = 2d,
                    [CombatDynamicDamageProjection.DefaultDamage] = 1d
                }
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = 1,
            Actions = { action },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 7,
                    CurrentHp = 50,
                    MaxHp = 50,
                    Features =
                    {
                        [CombatDynamicDamageProjection.AttackedPercentDamage] = 1.5d,
                        [CombatDynamicDamageProjection.AttackedDefaultDamage] = 2d
                    }
                }
            }
        };
        var state = CombatForwardModel.Create(observation, 1);
        var model = CombatForwardModel.Resolve(
            observation,
            action,
            useRegisteredResolvers: false);
        var result = CombatForwardModel.Apply(
            state,
            action,
            0,
            model.Outcomes[0],
            new CombatDecisionProfile());
        Assert(result.Enemies[0].Hp == 18,
            "forward search applies observed damage variables to every hit in a multi-hit action");
    }

    private static void StrategicTargetsAreWorthRemovingFirst()
    {
        var strategicHit = BuildTargetValueState(
            strategicHp: 90,
            ordinaryHp: 100);
        var ordinaryHit = BuildTargetValueState(
            strategicHp: 100,
            ordinaryHp: 90);
        Assert(strategicHit.EvaluateLeaf(new CombatDecisionProfile()).Value
               > ordinaryHit.EvaluateLeaf(new CombatDecisionProfile()).Value,
            "leaf evaluation rewards progress against summoners and support enemies");
    }

    private static CombatSimulationState BuildTargetValueState(
        int strategicHp,
        int ordinaryHp)
    {
        return new CombatSimulationState
        {
            PlayerHp = 30,
            PlayerMaxHp = 30,
            Enemies =
            [
                new CombatSimulationUnit
                {
                    RuntimeId = 10,
                    Hp = strategicHp,
                    MaxHp = 100,
                    Features =
                    {
                        [CombatEnemyPriorityPolicy.SummonPotentialFeature] = 1d,
                        [CombatEnemyPriorityPolicy.StrategicPriorityFeature] = 2.5d
                    }
                },
                new CombatSimulationUnit
                {
                    RuntimeId = 11,
                    Hp = ordinaryHp,
                    MaxHp = 100
                }
            ]
        };
    }

    private static void CompletedLowConfidenceSearchUsesSafeFallback()
    {
        var action = new CombatActionObservation
        {
            CandidateId = "safe-hit",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { Damage = 5d }
        };
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                CurrentHp = 20,
                MaxHp = 20
            },
            Actions = { action }
        };
        var candidate = new CombatCandidateEvaluation
        {
            Action = action,
            Legal = true,
            RuleScore = 1d,
            SearchDeathRisk = 0d
        };
        var search = new CombatSearchResult
        {
            HasAction = true,
            Action = action,
            Confidence = 0.053d,
            StoppedByTime = false
        };
        var profile = new CombatDecisionProfile
        {
            UseLowConfidenceFallback = true,
            MinimumSearchConfidence = 0.35d
        };
        var verdict = CombatDecisionGovernance.ReviewSearch(
            state,
            new[] { candidate },
            new CombatEndTurnAssessment(),
            search,
            profile);
        Assert(verdict.Decision == CombatGovernanceDecision.Accept
               && verdict.Reason.Contains("safest"),
            "low confidence does not relabel the same proposal as an independent fallback");

        candidate.SearchDeathRisk = 0.25d;
        var saferAction = new CombatActionObservation
        {
            CandidateId = "safer-guard",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { Defend = 5d }
        };
        var safer = new CombatCandidateEvaluation
        {
            Action = saferAction,
            Legal = true,
            RuleScore = 0.8d,
            SearchDeathRisk = 0d
        };
        var provenFallback = CombatDecisionGovernance.ReviewSearch(
            state,
            new[] { candidate, safer },
            new CombatEndTurnAssessment(),
            search,
            profile);
        Assert(provenFallback.Decision
               == CombatGovernanceDecision.UseSafeFallback
               && ReferenceEquals(provenFallback.Candidate, safer),
            "low-confidence governance switches actions only when the alternative has a measurable safety proof");

        profile.UseLowConfidenceFallback = false;
        var optOut = CombatDecisionGovernance.ReviewSearch(
            state,
            new[] { candidate },
            new CombatEndTurnAssessment(),
            search,
            profile);
        Assert(optOut.Decision == CombatGovernanceDecision.Accept,
            "profiles may explicitly opt out of low-confidence fallback");
    }

    private static void ModelAuthorityDoesNotUseQualityFallback()
    {
        var proposedAction = new CombatActionObservation
        {
            CandidateId = "model-choice",
            Kind = CombatActionKind.PlayCard,
            Legal = true
        };
        var saferAction = new CombatActionObservation
        {
            CandidateId = "heuristic-choice",
            Kind = CombatActionKind.PlayCard,
            Legal = true
        };
        var proposed = new CombatCandidateEvaluation
        {
            Action = proposedAction,
            Legal = true,
            RuleScore = -10d,
            SearchDeathRisk = 1d
        };
        var safer = new CombatCandidateEvaluation
        {
            Action = saferAction,
            Legal = true,
            RuleScore = 10d,
            SearchDeathRisk = 0d
        };
        var verdict = CombatDecisionGovernance.ReviewSearch(
            new CombatStateObservation(),
            new[] { proposed, safer },
            new CombatEndTurnAssessment { Prohibited = true },
            new CombatSearchResult
            {
                HasAction = true,
                Action = proposedAction,
                Confidence = 0d,
                StoppedByTime = true,
                StoppedByModelBudget = true
            },
            new CombatDecisionProfile
            {
                ModelOwnsActionSelection = true,
                UseLowConfidenceFallback = true,
                MinimumSearchConfidence = 0.8d
            });
        Assert(verdict.Decision == CombatGovernanceDecision.Accept
               && ReferenceEquals(verdict.Candidate, proposed),
            "model authority accepts its legal proposal despite low confidence, negative rule score and safer heuristic alternatives");
    }

    private static void ModelAuthorityKeepsHeuristicallyRejectedAction()
    {
        var attack = new CombatActionObservation
        {
            CandidateId = "safe-attack",
            SourceId = "safe-attack",
            Kind = CombatActionKind.PlayCard,
            Legal = true,
            Cost = 1,
            TargetRuntimeId = 2,
            Semantics = new CombatActionSemantics { Damage = 5d }
        };
        var modelChoice = new CombatActionObservation
        {
            CandidateId = "model-visible-fake",
            SourceId = "model-visible-fake",
            Kind = CombatActionKind.PlayCard,
            Legal = true,
            Cost = 1,
            TargetRuntimeId = 2,
            Semantics = new CombatActionSemantics { Damage = 1d },
            Features = { ["visibleFake"] = 1d }
        };
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                CurrentHp = 30,
                MaxHp = 30
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    CurrentHp = 30,
                    MaxHp = 30
                }
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = 2,
            HandCardIds = { "safe-attack", "model-visible-fake" },
            Actions = { attack, modelChoice },
            IsPlayerActionWindow = true,
            Fingerprint = "model-authority-end-turn"
        };
        var profile = new CombatDecisionProfile
        {
            ModelOwnsActionSelection = true,
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 1,
            SearchNodeBudget = 16,
            SearchMaxPly = 1,
            SearchMinimumSimulations = 1,
            SearchStabilityWindow = 1,
            SearchStableChecks = 1,
            SearchTimeBudgetMilliseconds = 0,
            SearchMinimumTimeMilliseconds = 0,
            UseLowConfidenceFallback = true,
            MinimumSearchConfidence = 0.8d
        };
        var decision = new CombatDecisionEngine(
                useRuntimeRegistries: false,
                policyValueModel: new PreferredPolicyValueModel(
                    modelChoice.CandidateId))
            .Choose(state, profile);
        Assert(decision.HasAction
               && decision.Action?.CandidateId == modelChoice.CandidateId
               && decision.DecisionPath == "model-authority-accepted"
               && !decision.GovernanceFallbackApplied,
            "model authority keeps and may select an engine-legal action that heuristic visibility policy would reject");
    }

    private static void ModelAuthorityKeepsEquivalentCandidateInstances()
    {
        CombatActionObservation Candidate(string id, int handIndex)
        {
            var action = new CombatActionObservation
            {
                CandidateId = id,
                SourceId = "duplicate-card",
                Kind = CombatActionKind.PlayCard,
                Legal = true,
                Cost = 1,
                TargetRuntimeId = 2,
                Semantics = new CombatActionSemantics { Damage = 5d }
            };
            action.Features["handIndex"] = handIndex;
            return action;
        }

        var first = Candidate("a-card-instance", 0);
        var preferred = Candidate("z-card-instance", 1);
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                CurrentHp = 30,
                MaxHp = 30
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    CurrentHp = 30,
                    MaxHp = 30
                }
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = 2,
            HandCardIds = { "duplicate-card", "duplicate-card" },
            Actions = { first, preferred },
            IsPlayerActionWindow = true,
            Fingerprint = "model-authority-equivalent-instances"
        };
        var decision = new CombatDecisionEngine(
                useRuntimeRegistries: false,
                policyValueModel: new PreferredPolicyValueModel(
                    preferred.CandidateId))
            .Choose(
                state,
                new CombatDecisionProfile
                {
                    ModelOwnsActionSelection = true,
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 1,
                    SearchNodeBudget = 16,
                    SearchMaxPly = 1,
                    SearchMinimumSimulations = 1,
                    SearchStabilityWindow = 1,
                    SearchStableChecks = 1,
                    SearchTimeBudgetMilliseconds = 0,
                    SearchMinimumTimeMilliseconds = 0
                });
        Assert(decision.Action?.CandidateId == preferred.CandidateId
               && decision.SearchOriginalCandidateCount == 2
               && decision.SearchCandidateCount == 2,
            "model authority preserves mechanically equivalent card instances so the network can choose between them");
    }

    private static void MinimumSearchTimeRequiresMoreEvidence()
    {
        var proposedAction = new CombatActionObservation
        {
            CandidateId = "premature",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { Damage = 2d }
        };
        var alternativeAction = new CombatActionObservation
        {
            CandidateId = "alternative",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { Defend = 2d }
        };
        var proposed = new CombatCandidateEvaluation
        {
            Action = proposedAction,
            Legal = true,
            RuleScore = 1d
        };
        var alternative = new CombatCandidateEvaluation
        {
            Action = alternativeAction,
            Legal = true,
            RuleScore = 0.9d
        };
        var search = new CombatSearchResult
        {
            Action = proposedAction,
            HasAction = true,
            Confidence = 0.9d,
            CandidateCount = 2,
            MinimumTimeMilliseconds = 100,
            MinimumTimeSatisfied = false
        };
        var verdict = CombatDecisionGovernance.ReviewSearch(
            new CombatStateObservation
            {
                Player = new CombatUnitObservation
                {
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            new[] { proposed, alternative },
            new CombatEndTurnAssessment(),
            search,
            new CombatDecisionProfile());
        Assert(verdict.Decision == CombatGovernanceDecision.RequireMoreSearch,
            "a multi-candidate search cannot publish a local optimum before its minimum evidence time");

        search.CandidateCount = 1;
        var forced = CombatDecisionGovernance.ReviewSearch(
            new CombatStateObservation
            {
                Player = new CombatUnitObservation
                {
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            new[] { proposed },
            new CombatEndTurnAssessment(),
            search,
            new CombatDecisionProfile());
        Assert(forced.Decision == CombatGovernanceDecision.Accept,
            "a genuinely forced single legal action does not burn an artificial minimum search duration");
    }

    private static void InteractiveSettlementUsesTransactionDeadline()
    {
        var action = new CombatActionObservation
        {
            CandidateId = "interactive",
            Kind = CombatActionKind.UseSkill,
            Semantics = new CombatActionSemantics
            {
                OpensInteraction = true,
                Interaction = new CombatInteractionDefinition
                {
                    MinSelections = 1,
                    MaxSelections = 1
                }
            }
        };
        Assert(double.IsPositiveInfinity(
                   CombatActionExecutionPolicy.NoEffectGraceSeconds(
                       action,
                       0.35d))
               && CombatActionExecutionPolicy.OpensFollowUpInteraction(action),
            "interactive actions remain pending until the bounded transaction deadline instead of racing prompt creation");
    }

    private static void SearchReportsRawNetworkPrediction()
    {
        var strike = new CombatActionObservation
        {
            CandidateId = "telemetry-strike",
            SourceId = "telemetry-strike",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 90,
            Semantics = new CombatActionSemantics { Damage = 4d }
        };
        var guard = new CombatActionObservation
        {
            CandidateId = "telemetry-guard",
            SourceId = "telemetry-guard",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { Defend = 3d }
        };
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                CurrentHp = 20,
                MaxHp = 20
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = 2,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 90,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            Actions = { strike, guard }
        };
        var candidates = new[]
        {
            new CombatCandidateEvaluation
            {
                Action = strike,
                Legal = true,
                RuleScore = 2d
            },
            new CombatCandidateEvaluation
            {
                Action = guard,
                Legal = true,
                RuleScore = 1d
            }
        };
        var result = new CombatRiskAwareRootSamplingPuctPlanner(
                useRuntimeRegistries: false,
                policyValueModel: new RecordingPolicyValueModel())
            .Choose(
                state,
                candidates,
                new CombatDecisionProfile
                {
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 8,
                    SearchNodeBudget = 128,
                    SearchModelEvaluationBudget = 64,
                    SearchTimeBudgetMilliseconds = 1000
                });
        Assert(result.HasNetworkPrediction
               && Math.Abs(result.NetworkExpectedReturn - 0.75d) < 0.000001d
               && Math.Abs(result.NetworkDeathProbability - 0.10d) < 0.000001d
               && Math.Abs(result.NetworkUncertainty - 0.20d) < 0.000001d
               && Math.Abs(result.PolicyAmbiguity - 0.20d) < 0.000001d
               && result.SearchEvidence == result.Confidence
               && result.SemanticCoverageRisk > 0d
               && result.Summary.Contains("network=return:"),
            "search telemetry separates the raw network forecast from rollout evidence");
    }

    private static void FatalAndRepeatableSelfHarmCannotBeOverridden()
    {
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                CurrentHp = 8,
                MaxHp = 50
            }
        };
        var fatal = new CombatActionObservation
        {
            CandidateId = "fatal-conversion",
            SourceId = "fatal-conversion",
            Kind = CombatActionKind.PlayCard,
            Cost = 0,
            Semantics = new CombatActionSemantics { SelfHpLoss = 8d },
            Features = { ["recycle"] = 1d }
        };
        state.Enemies.Add(new CombatUnitObservation
        {
            RuntimeId = 10,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 4,
            MaxHp = 4
        });
        fatal.TargetRuntimeId = 10;
        Assert(!CombatActionSafetyPolicy.IsAdmissible(
                   state,
                   fatal,
                   new AuraDecision.Shared.DecisionUtility(),
                   out var fatalReason)
               && fatalReason.Contains("fatal", StringComparison.Ordinal),
            "learned value cannot override an uncertified fatal action");

        state.Enemies.Add(new CombatUnitObservation
        {
            RuntimeId = 11,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 4,
            MaxHp = 4
        });
        fatal.Semantics.Damage = 4d;
        Assert(!CombatActionSafetyPolicy.IsAdmissible(
                   state,
                   fatal,
                   new AuraDecision.Shared.DecisionUtility { Lethal = 24d },
                   out _),
            "a target-lethal score cannot certify a fatal action while another enemy survives");
        state.Enemies.RemoveAt(1);
        Assert(CombatActionSafetyPolicy.IsAdmissible(
                state,
                fatal,
                new AuraDecision.Shared.DecisionUtility { Lethal = 24d },
                out _),
            "a verified immediate battle win may spend the player's remaining health");
        state.Enemies.Clear();

        fatal.Semantics.SelfHpLoss = 2d;
        fatal.Semantics.Damage = 0d;
        fatal.TargetRuntimeId = 0;
        Assert(!CombatActionSafetyPolicy.IsAdmissible(
                   state,
                   fatal,
                   new AuraDecision.Shared.DecisionUtility(),
                   out var loopReason)
               && loopReason.Contains("repeatable self-harm", StringComparison.Ordinal),
            "repeatable self-harm without enemy or system progress is hard-gated");
        fatal.Features["systemProgressValue"] = 10d;
        Assert(CombatActionSafetyPolicy.IsAdmissible(
                state,
                fatal,
                new AuraDecision.Shared.DecisionUtility(),
                out _),
            "structured system progress permits a bounded self-harm conversion");
    }

    private static void NegativeFallbackRequiresMinimumLossProof()
    {
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                CurrentHp = 20,
                MaxHp = 20
            }
        };
        var costly = new CombatCandidateEvaluation
        {
            Legal = true,
            RuleScore = -4d,
            Action = new CombatActionObservation
            {
                CandidateId = "costly-loss",
                Semantics = new CombatActionSemantics { SelfHpLoss = 3d }
            }
        };
        var smaller = new CombatCandidateEvaluation
        {
            Legal = true,
            RuleScore = -1d,
            Action = new CombatActionObservation
            {
                CandidateId = "minimum-loss",
                Semantics = new CombatActionSemantics()
            }
        };
        var selected = CombatDecisionGovernance.SelectSafeFallback(
            state,
            new[] { costly, smaller },
            new CombatDecisionProfile { MinimumActionScore = 0.05d });
        Assert(ReferenceEquals(selected, smaller)
               && CombatActionSafetyPolicy.HasMinimumLossCertificate(
                   smaller.Action)
               && !CombatActionSafetyPolicy.HasMinimumLossCertificate(
                   costly.Action),
            "negative fallback is legal only for the proven minimum-loss candidate");
    }

    private static void SaturatedRiskFallbackRequiresSearchEvidence()
    {
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                CurrentHp = 20,
                MaxHp = 20
            }
        };
        var noisyMinimum = new CombatCandidateEvaluation
        {
            Legal = true,
            RuleScore = 1d,
            SearchVisits = 4,
            SearchDeathRisk = 0.30d,
            SearchMeanReturn = -1d,
            SearchLowerTailMean = -2d,
            Action = new CombatActionObservation
            {
                CandidateId = "noisy-minimum-risk",
                Semantics = new CombatActionSemantics { Damage = 1d },
                Features = { ["effectiveDamage"] = 1d }
            }
        };
        var evidenced = new CombatCandidateEvaluation
        {
            Legal = true,
            RuleScore = 2d,
            SearchVisits = 128,
            SearchDeathRisk = 0.33d,
            SearchMeanReturn = 1d,
            SearchLowerTailMean = -0.5d,
            Action = new CombatActionObservation
            {
                CandidateId = "evidence-backed-branch",
                Semantics = new CombatActionSemantics { Damage = 4d },
                Features = { ["effectiveDamage"] = 4d }
            }
        };
        var selected = CombatDecisionGovernance.SelectSafeFallback(
            state,
            new[] { noisyMinimum, evidenced },
            new CombatDecisionProfile
            {
                DeathRiskLimit = 0.20d,
                MinimumActionScore = 0.05d,
                SearchMinimumChallengerVisits = 4
            });
        Assert(ReferenceEquals(selected, evidenced),
            "saturated-risk fallback does not let a four-visit minimum erase a deeply searched close-risk branch");

        evidenced.SearchDeathRisk = 0.37d;
        selected = CombatDecisionGovernance.SelectSafeFallback(
            state,
            new[] { noisyMinimum, evidenced },
            new CombatDecisionProfile
            {
                DeathRiskLimit = 0.20d,
                MinimumActionScore = 0.05d,
                SearchMinimumChallengerVisits = 4
            });
        Assert(ReferenceEquals(selected, noisyMinimum),
            "evidence fallback preserves the minimum-risk branch when the risk gap is material");
    }

    private static void DynamicInstanceCostAndSuppressionRevisionTrackEligibility()
    {
        var action = new CombatActionObservation
        {
            CandidateId = "card:dynamic:2000:0",
            SourceId = "dynamic",
            RuntimeId = 2000,
            Kind = CombatActionKind.PlayCard,
            Cost = 4,
            Features =
            {
                ["cardBaseCost"] = 20d,
                ["cardCostCap"] = 4d,
                ["cardCostMultiplier"] = 1d,
                ["cardExCost"] = 0d,
                ["runtimeUsable"] = 1d
            }
        };
        var simulation = new CombatSimulationState
        {
            CardCostMultiplier = 1d,
            ActionCostAdjustments = new[] { -3 }
        };
        Assert(CombatForwardModel.EffectiveCost(simulation, action, 0) == 1,
            "forward search applies per-instance held-action cost changes");

        var first = new CombatStateObservation
        {
            BattleSessionId = 9,
            Fingerprint = "unrelated-a"
        };
        var second = new CombatStateObservation
        {
            BattleSessionId = 9,
            Fingerprint = "unrelated-b"
        };
        var firstKey = CombatActionExecutionPolicy.BuildFailureSuppressionKey(
            first,
            action);
        var secondKey = CombatActionExecutionPolicy.BuildFailureSuppressionKey(
            second,
            action);
        var changedCost = new CombatActionObservation
        {
            CandidateId = action.CandidateId,
            SourceId = action.SourceId,
            RuntimeId = action.RuntimeId,
            Kind = action.Kind,
            Cost = 1,
            Features = new Dictionary<string, double>(
                action.Features,
                StringComparer.OrdinalIgnoreCase)
            {
                ["cardExCost"] = -3d
            }
        };
        Assert(firstKey == secondKey
               && firstKey != CombatActionExecutionPolicy.BuildFailureSuppressionKey(
                   second,
                   changedCost),
            "no-effect quarantine survives unrelated fingerprints and releases on a real cost revision");
    }

    private static void RealizedTransitionCarriesHpMaximumHpAndStatusDeltas()
    {
        var before = new AuraCombatSimulation.Shared.CombatBattleState
        {
            PlayerActorId = 1,
            Actors =
            {
                new AuraCombatSimulation.Shared.CombatActorState
                {
                    ActorId = 1,
                    Kind = AuraCombatSimulation.Shared.CombatSimulationActorKind.Player,
                    Hp = 30,
                    MaxHp = 50,
                    Statuses =
                    {
                        new AuraCombatSimulation.Shared.CombatStatusState
                        {
                            StatusId = "buff_system",
                            Stacks = 20
                        }
                    }
                }
            }
        };
        var after = before.Clone();
        after.Player!.Hp = 24;
        after.Player.MaxHp = 45;
        after.Player.Statuses[0].Stacks = 10;
        var realized = CombatSemanticAuditor.ProjectRealized(
            before,
            after,
            Array.Empty<AuraCombatSimulation.Shared.CombatSimulationEvent>(),
            new AuraCombatSimulation.Shared.CombatSimulationAction
            {
                ActorId = 1
            },
            null);
        Assert(realized.SelfHpLoss == 6d
               && realized.StateChanges["player.hp"] == -6d
               && realized.StateChanges["playerMaxHp"] == -5d
               && realized.StateChanges["status:buff_system"] == -10d,
            "realized transition preserves hp, maximum-hp and direct status changes structurally");
        var effective = CombatSemanticAuditor.ProjectEffective(
            before,
            new AuraCombatSimulation.Shared.CombatSimulationAction
            {
                ActorId = 1
            },
            realized);
        Assert(effective.Damage == 0d,
            "structured player hp loss cannot be reclassified as enemy damage");
    }
}
