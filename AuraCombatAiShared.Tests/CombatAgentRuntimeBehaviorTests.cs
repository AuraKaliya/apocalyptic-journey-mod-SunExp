using AuraCombatAi.Shared;

internal static class CombatAgentRuntimeBehaviorTests
{
    public static void Run()
    {
        VoluntaryEndDoesNotUseForcedCompletion();
        FailedTargetIsSuppressedBeforeRetry();
        ConsecutiveDecisionFailuresCannotStallTurn();
        CommittedActionTimeoutIsNeverReplayed();
        HeadlessDeclarationRejectsUnsupportedAction();
        NormalizedDecisionKeepsStableHeadlessRoute();
        ActorCardSnapshotIsDeepAndValidated();
    }

    private static void VoluntaryEndDoesNotUseForcedCompletion()
    {
        var port = new FakeAgentPort(BuildState("voluntary"));
        var runner = CreateRunner(
            port,
            new FixedDecisionSource(state => state.Actions.Single(action =>
                action.Kind == CombatActionKind.EndTurn)));

        var status = runner.Step(0d);

        CombatAiTestFixtures.Assert(
            status == CombatAutoTurnStepStatus.Completed,
            "agent voluntary end completes immediately");
        CombatAiTestFixtures.Assert(
            runner.Result?.Reason == CombatAgentCompletionReason.VoluntaryEnd,
            "agent voluntary end reason");
        CombatAiTestFixtures.Assert(
            runner.Result?.Forced == false,
            "agent voluntary end remains voluntary");
        CombatAiTestFixtures.Assert(
            port.Completed.Count == 1,
            "agent voluntary end completes runtime exactly once");
        CombatAiTestFixtures.Assert(
            port.Executed.Count == 0,
            "agent voluntary end never executes a card");
    }

    private static void FailedTargetIsSuppressedBeforeRetry()
    {
        var port = new FakeAgentPort(BuildState("target-failure"));
        port.PreflightRule = action => action.TargetRuntimeId == 10
            ? CombatAgentPreflightResult.Reject("target unavailable")
            : CombatAgentPreflightResult.Allow();
        var runner = CreateRunner(port, new FirstLegalDecisionSource());

        var first = runner.Step(0d);
        var second = runner.Step(0.1d);

        CombatAiTestFixtures.Assert(
            first == CombatAutoTurnStepStatus.Running,
            "agent target failure retries instead of ending");
        CombatAiTestFixtures.Assert(
            second == CombatAutoTurnStepStatus.Running,
            "agent alternate target settles without ending");
        CombatAiTestFixtures.Assert(
            port.Executed.Count == 1
            && port.Executed[0].TargetRuntimeId == 11,
            "agent suppresses only failed card target pair");
        CombatAiTestFixtures.Assert(
            runner.Result == null,
            "agent remains active after alternate action succeeds");
    }

    private static void ConsecutiveDecisionFailuresCannotStallTurn()
    {
        var port = new FakeAgentPort(BuildState("decision-failure"));
        var runner = CreateRunner(
            port,
            new EmptyDecisionSource(),
            new CombatAutoTurnProfile
            {
                MaxConsecutiveFailures = 3,
                MaxRepeatedStateObservations = 10
            });

        runner.Step(0d);
        runner.Step(0.1d);
        var status = runner.Step(0.2d);

        CombatAiTestFixtures.Assert(
            status == CombatAutoTurnStepStatus.Completed,
            "agent consecutive failures force completion");
        CombatAiTestFixtures.Assert(
            runner.Result?.Reason == CombatAgentCompletionReason.ConsecutiveFailures,
            "agent reports consecutive failure completion");
        CombatAiTestFixtures.Assert(
            runner.Result?.ConsecutiveFailures == 3,
            "agent uses configured three-failure limit");
        CombatAiTestFixtures.Assert(
            runner.Result?.Forced == true,
            "agent failure completion is forced");
    }

    private static void CommittedActionTimeoutIsNeverReplayed()
    {
        var port = new FakeAgentPort(BuildState("commit-timeout"))
        {
            ExecutionRule = _ => CombatAgentExecutionResult.AwaitSettlement(),
            SettlementRule = _ => CombatAgentSettlementResult.Pending("still resolving")
        };
        var runner = CreateRunner(
            port,
            new FirstLegalDecisionSource(),
            new CombatAutoTurnProfile { ActionTimeoutSeconds = 1d });

        var first = runner.Step(0d);
        var second = runner.Step(1.1d);

        CombatAiTestFixtures.Assert(
            first == CombatAutoTurnStepStatus.Waiting,
            "agent waits for committed action settlement");
        CombatAiTestFixtures.Assert(
            second == CombatAutoTurnStepStatus.Completed,
            "agent committed action timeout completes turn");
        CombatAiTestFixtures.Assert(
            runner.Result?.Reason == CombatAgentCompletionReason.ActionTimeout,
            "agent committed action timeout reason");
        CombatAiTestFixtures.Assert(
            port.Executed.Count == 1,
            "agent never replays a committed timed out action");
    }

    private static void HeadlessDeclarationRejectsUnsupportedAction()
    {
        using var registration = CombatActionAutomationRegistry.Register(
            "tests",
            "unsupported-prompt",
            new UnsupportedActionProvider());
        var port = new FakeAgentPort(BuildState("headless"));
        var runner = CreateRunner(
            port,
            new FirstLegalDecisionSource(),
            new CombatAutoTurnProfile { RequireDeclaredHeadlessActions = true });

        var status = runner.Step(0d);

        CombatAiTestFixtures.Assert(
            status == CombatAutoTurnStepStatus.Running,
            "agent unsupported headless action is suppressed before ending");
        status = runner.Step(0.1d);
        CombatAiTestFixtures.Assert(
            status == CombatAutoTurnStepStatus.Completed,
            "agent unsupported headless action completes after no legal action remains");
        CombatAiTestFixtures.Assert(
            runner.Result?.Reason
            == CombatAgentCompletionReason.NoLegalAction,
            "agent unsupported headless action ends only after suppression exhausts candidates");
        CombatAiTestFixtures.Assert(
            port.Executed.Count == 0,
            "agent unsupported headless action is not committed");
    }

    private static void ActorCardSnapshotIsDeepAndValidated()
    {
        var snapshot = new CombatActorCardStateSnapshot
        {
            OwnerModId = "Terrias",
            ActorId = "projection-1",
            CurrentPower = 2,
            MaxPower = 3,
            Cards =
            {
                new CombatCardInstanceSnapshot
                {
                    InstanceId = "projection-card-1",
                    SourceInstanceId = "player-card-9",
                    CardId = "Card_Attack",
                    Zone = CombatActorCardZone.Hand,
                    Variables = { ["damage"] = 8d },
                    RuntimeData = { ["cost-source"] = "runtime" },
                    Tags = { "attack" },
                    Attachments = { "upgrade-1" },
                    AttachmentStates =
                    {
                        new CombatCardAttachmentSnapshot
                        {
                            AttachmentId = "upgrade-1",
                            Variables = { ["level"] = "2" }
                        }
                    }
                }
            },
            RuntimeVariables = { ["turn"] = 1d }
        };

        var clone = snapshot.DeepClone();
        clone.Cards[0].Variables["damage"] = 99d;
        clone.Cards[0].Tags.Add("changed");
        clone.Cards[0].AttachmentStates[0].Variables["level"] = "3";
        clone.RuntimeVariables["turn"] = 2d;

        CombatAiTestFixtures.Assert(
            snapshot.Validate(out _),
            "agent actor snapshot validates");
        CombatAiTestFixtures.Assert(
            snapshot.Cards[0].Variables["damage"] == 8d
            && snapshot.Cards[0].Tags.Count == 1
            && snapshot.Cards[0].AttachmentStates[0].Variables["level"] == "2"
            && snapshot.RuntimeVariables["turn"] == 1d,
            "agent actor snapshot deep clone has no aliases");

        clone.Cards.Add(clone.Cards[0].DeepClone());
        CombatAiTestFixtures.Assert(
            !clone.Validate(out var reason)
            && reason.Contains("unique", StringComparison.Ordinal),
            "agent actor snapshot rejects duplicate instance ids");
    }

    private static void NormalizedDecisionKeepsStableHeadlessRoute()
    {
        var provider = new StableProjectionRouteProvider();
        using var registration = CombatActionAutomationRegistry.Register(
            "tests",
            "stable-projection-route",
            provider,
            priority: 100);
        var state = new CombatStateObservation
        {
            Fingerprint = "normalized-headless-route",
            CurrentPower = 1,
            MaxPower = 1,
            HandCount = 1,
            IsPlayerActionWindow = true,
            Player = new CombatUnitObservation
            {
                RuntimeId = 42,
                Kind = CombatTargetKind.Friendly,
                CurrentHp = 10,
                MaxHp = 10
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 10,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 10,
                    MaxHp = 10
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "projection:card:100:10",
                    SourceId = "projection-card:safe-strike",
                    DisplayName = "Safe strike",
                    RuntimeId = 100,
                    TargetRuntimeId = 10,
                    TargetKind = CombatTargetKind.Enemy,
                    Kind = CombatActionKind.PlayCard,
                    Cost = 1,
                    Legal = true,
                    Semantics = new CombatActionSemantics { Damage = 5d },
                    Features = { ["headlessSupported"] = 1d }
                },
                new CombatActionObservation
                {
                    CandidateId = "projection:end-turn",
                    SourceId = "projection:end-turn",
                    Kind = CombatActionKind.EndTurn,
                    Legal = false,
                    RejectionReason = "a legal actor-safe card remains"
                }
            }
        };
        var port = new FakeAgentPort(state);
        var runner = CreateRunner(
            port,
            new CombatDecisionEngineSource(new CombatDecisionEngine(
                useRuntimeRegistries: false)),
            new CombatAutoTurnProfile
            {
                RequireDeclaredHeadlessActions = true,
                DecisionProfile = new CombatDecisionProfile
                {
                    Id = "normalized-headless-route",
                    SearchQuality = "fast",
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 16,
                    SearchMinimumSimulations = 8,
                    SearchTimeBudgetMilliseconds = 50
                }
            });

        var status = runner.Step(0d);

        CombatAiTestFixtures.Assert(
            status == CombatAutoTurnStepStatus.Running
            && port.Executed.Count == 1
            && port.Executed[0].SourceId == "projection-card:safe-strike",
            "agent commits a declared actor-safe action selected through the normalized decision boundary");
        CombatAiTestFixtures.Assert(
            provider.ObservedSelectedAction
            && !provider.SelectedActionContainedLegacyCapabilityFeature,
            "headless route declaration remains valid after private capability features are sanitized");
        CombatAiTestFixtures.Assert(
            runner.Result == null,
            "a successful normalized actor action does not consume the consecutive-failure path");
    }

    private static CombatAutoTurnRunner CreateRunner(
        FakeAgentPort port,
        ICombatAgentDecisionSource decisionSource,
        CombatAutoTurnProfile? profile = null)
    {
        return new CombatAutoTurnRunner(
            new CombatAgentDescriptor
            {
                OwnerModId = "tests",
                ActorId = "projection",
                RuntimeId = 42
            },
            profile ?? new CombatAutoTurnProfile(),
            decisionSource,
            port);
    }

    private static CombatStateObservation BuildState(string fingerprint)
    {
        return new CombatStateObservation
        {
            Fingerprint = fingerprint,
            Player = new CombatUnitObservation
            {
                RuntimeId = 42,
                Kind = CombatTargetKind.Friendly,
                CurrentHp = 10,
                MaxHp = 10
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 10,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 10,
                    MaxHp = 10
                },
                new CombatUnitObservation
                {
                    RuntimeId = 11,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 10,
                    MaxHp = 10
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "strike:10",
                    SourceId = "strike",
                    RuntimeId = 100,
                    TargetRuntimeId = 10,
                    TargetKind = CombatTargetKind.Enemy,
                    Kind = CombatActionKind.PlayCard
                },
                new CombatActionObservation
                {
                    CandidateId = "strike:11",
                    SourceId = "strike",
                    RuntimeId = 100,
                    TargetRuntimeId = 11,
                    TargetKind = CombatTargetKind.Enemy,
                    Kind = CombatActionKind.PlayCard
                },
                new CombatActionObservation
                {
                    CandidateId = "end-turn",
                    SourceId = "end-turn",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
    }

    private sealed class FixedDecisionSource : ICombatAgentDecisionSource
    {
        private readonly Func<CombatStateObservation, CombatActionObservation> select;

        public FixedDecisionSource(
            Func<CombatStateObservation, CombatActionObservation> select)
        {
            this.select = select;
        }

        public CombatDecision Choose(
            CombatStateObservation state,
            CombatDecisionProfile profile)
        {
            return new CombatDecision
            {
                HasAction = true,
                Action = select(state)
            };
        }
    }

    private sealed class FirstLegalDecisionSource : ICombatAgentDecisionSource
    {
        public CombatDecision Choose(
            CombatStateObservation state,
            CombatDecisionProfile profile)
        {
            var action = state.Actions.FirstOrDefault(candidate =>
                candidate.Legal && candidate.Kind != CombatActionKind.EndTurn);
            return new CombatDecision
            {
                HasAction = action != null,
                Action = action
            };
        }
    }

    private sealed class EmptyDecisionSource : ICombatAgentDecisionSource
    {
        public CombatDecision Choose(
            CombatStateObservation state,
            CombatDecisionProfile profile)
        {
            return new CombatDecision { Reason = "no decision" };
        }
    }

    private sealed class FakeAgentPort : ICombatAgentRuntimePort
    {
        private readonly CombatStateObservation state;

        public FakeAgentPort(CombatStateObservation state)
        {
            this.state = state;
        }

        public List<CombatActionObservation> Executed { get; } = new();

        public List<CombatAutoTurnResult> Completed { get; } = new();

        public Func<CombatActionObservation, CombatAgentPreflightResult>
            PreflightRule { get; set; } = _ => CombatAgentPreflightResult.Allow();

        public Func<CombatActionObservation, CombatAgentExecutionResult>
            ExecutionRule { get; set; } = _ => CombatAgentExecutionResult.Complete();

        public Func<CombatActionObservation, CombatAgentSettlementResult>
            SettlementRule { get; set; } = _ => CombatAgentSettlementResult.Complete();

        public bool TryObserve(
            out CombatAgentObservation observation,
            out string reason)
        {
            observation = new CombatAgentObservation { State = CloneState(state) };
            reason = "";
            return true;
        }

        public CombatAgentPreflightResult Preflight(
            CombatAgentObservation observation,
            CombatActionObservation action)
        {
            return PreflightRule(action);
        }

        public CombatAgentExecutionResult Execute(
            CombatAgentObservation observation,
            CombatActionObservation action)
        {
            Executed.Add(action);
            return ExecutionRule(action);
        }

        public CombatAgentSettlementResult PollSettlement(
            CombatActionObservation action)
        {
            return SettlementRule(action);
        }

        public void CompleteTurn(CombatAutoTurnResult result)
        {
            Completed.Add(result);
        }

        private static CombatStateObservation CloneState(CombatStateObservation source)
        {
            return new CombatStateObservation
            {
                Fingerprint = source.Fingerprint,
                Player = source.Player,
                Enemies = source.Enemies,
                Friendlies = source.Friendlies,
                CurrentPower = source.CurrentPower,
                MaxPower = source.MaxPower,
                Actions = source.Actions.Select(action =>
                    new CombatActionObservation
                    {
                        CandidateId = action.CandidateId,
                        SourceId = action.SourceId,
                        RuntimeId = action.RuntimeId,
                        TargetRuntimeId = action.TargetRuntimeId,
                        TargetKind = action.TargetKind,
                        Kind = action.Kind,
                        Legal = action.Legal,
                        Semantics = action.Semantics,
                        Features = new Dictionary<string, double>(
                            action.Features,
                            StringComparer.OrdinalIgnoreCase)
                    }).ToList()
            };
        }
    }

    private sealed class UnsupportedActionProvider : ICombatActionAutomationProvider
    {
        public bool TryDescribe(
            CombatStateObservation state,
            CombatActionObservation action,
            out CombatActionAutomationDescriptor descriptor)
        {
            descriptor = new CombatActionAutomationDescriptor
            {
                HeadlessSupported = false,
                FailureScope = CombatAgentFailureScope.Turn,
                Reason = "test action requires visible player input"
            };
            return action.Kind == CombatActionKind.PlayCard;
        }
    }

    private sealed class StableProjectionRouteProvider : ICombatActionAutomationProvider
    {
        public bool ObservedSelectedAction { get; private set; }

        public bool SelectedActionContainedLegacyCapabilityFeature { get; private set; }

        public bool TryDescribe(
            CombatStateObservation state,
            CombatActionObservation action,
            out CombatActionAutomationDescriptor descriptor)
        {
            var declared = action.SourceId.StartsWith(
                "projection-card:",
                StringComparison.Ordinal);
            if (declared)
            {
                ObservedSelectedAction = true;
                SelectedActionContainedLegacyCapabilityFeature =
                    action.Features.ContainsKey("headlessSupported");
            }
            descriptor = new CombatActionAutomationDescriptor
            {
                HeadlessSupported = declared,
                FailureScope = CombatAgentFailureScope.Turn,
                Reason = declared ? "" : "not a projection card route"
            };
            return declared;
        }
    }
}
