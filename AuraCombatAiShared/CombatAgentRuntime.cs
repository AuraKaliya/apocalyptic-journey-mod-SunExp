using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AuraCombatAi.Shared;

public enum CombatAgentControlMode
{
    HumanAssist,
    AutonomousRequired
}

public enum CombatAgentFailureScope
{
    Candidate,
    CardInstance,
    Turn,
    Committed
}

public enum CombatAgentCompletionReason
{
    VoluntaryEnd,
    NoLegalAction,
    ActorDead,
    BattleEnded,
    DecisionTimeout,
    ActionTimeout,
    UnsupportedInteraction,
    RepeatedState,
    MaximumActionsReached,
    ConsecutiveFailures,
    FatalExecutionFailure,
    TurnTimeout
}

public enum CombatAutoTurnStepStatus
{
    Running,
    Waiting,
    Completed
}

public sealed class CombatAgentDescriptor
{
    public string OwnerModId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public int RuntimeId { get; set; }

    public CombatAgentControlMode ControlMode { get; set; } =
        CombatAgentControlMode.AutonomousRequired;
}

public sealed class CombatAutoTurnProfile
{
    public int MaxConsecutiveFailures { get; set; } = 3;

    public int MaxCommittedActions { get; set; } = 32;

    public int MaxRepeatedStateObservations { get; set; } = 3;

    public double DecisionTimeoutSeconds { get; set; } = 2d;

    public double ActionTimeoutSeconds { get; set; } = 8d;

    public double TurnTimeoutSeconds { get; set; } = 45d;

    public bool RequireDeclaredHeadlessActions { get; set; }

    public CombatDecisionProfile DecisionProfile { get; set; } = new();

    internal CombatAutoTurnProfile Normalize()
    {
        MaxConsecutiveFailures = Math.Max(1, MaxConsecutiveFailures);
        MaxCommittedActions = Math.Max(1, MaxCommittedActions);
        MaxRepeatedStateObservations = Math.Max(1, MaxRepeatedStateObservations);
        DecisionTimeoutSeconds = Math.Max(0.01d, DecisionTimeoutSeconds);
        ActionTimeoutSeconds = Math.Max(0.01d, ActionTimeoutSeconds);
        TurnTimeoutSeconds = Math.Max(ActionTimeoutSeconds, TurnTimeoutSeconds);
        DecisionProfile ??= new CombatDecisionProfile();
        return this;
    }
}

public sealed class CombatAgentObservation
{
    public CombatStateObservation State { get; set; } = new();

    public bool ActorAlive { get; set; } = true;

    public bool BattleActive { get; set; } = true;

    public bool ActionWindowOpen { get; set; } = true;
}

public sealed class CombatAgentPreflightResult
{
    public bool Allowed { get; set; }

    public CombatAgentFailureScope FailureScope { get; set; } =
        CombatAgentFailureScope.Candidate;

    public string Reason { get; set; } = "";

    public static CombatAgentPreflightResult Allow()
    {
        return new CombatAgentPreflightResult { Allowed = true };
    }

    public static CombatAgentPreflightResult Reject(
        string reason,
        CombatAgentFailureScope scope = CombatAgentFailureScope.Candidate)
    {
        return new CombatAgentPreflightResult
        {
            Allowed = false,
            FailureScope = scope,
            Reason = reason ?? "preflight rejected"
        };
    }
}

public sealed class CombatAgentExecutionResult
{
    public bool Accepted { get; set; }

    public bool Committed { get; set; }

    public bool Settled { get; set; }

    public bool MeaningfulProgress { get; set; }

    public CombatAgentFailureScope FailureScope { get; set; } =
        CombatAgentFailureScope.Candidate;

    public string Message { get; set; } = "";

    public static CombatAgentExecutionResult Reject(
        string message,
        CombatAgentFailureScope scope = CombatAgentFailureScope.Candidate)
    {
        return new CombatAgentExecutionResult
        {
            FailureScope = scope,
            Message = message ?? "execution rejected"
        };
    }

    public static CombatAgentExecutionResult AwaitSettlement(
        string message = "committed")
    {
        return new CombatAgentExecutionResult
        {
            Accepted = true,
            Committed = true,
            Message = message
        };
    }

    public static CombatAgentExecutionResult Complete(
        bool committed = true,
        bool meaningfulProgress = true,
        string message = "settled")
    {
        return new CombatAgentExecutionResult
        {
            Accepted = true,
            Committed = committed,
            Settled = true,
            MeaningfulProgress = meaningfulProgress,
            Message = message
        };
    }
}

public sealed class CombatAgentSettlementResult
{
    public bool Settled { get; set; }

    public bool MeaningfulProgress { get; set; }

    public string Message { get; set; } = "";

    public static CombatAgentSettlementResult Pending(string message = "pending")
    {
        return new CombatAgentSettlementResult { Message = message };
    }

    public static CombatAgentSettlementResult Complete(
        bool meaningfulProgress = true,
        string message = "settled")
    {
        return new CombatAgentSettlementResult
        {
            Settled = true,
            MeaningfulProgress = meaningfulProgress,
            Message = message
        };
    }
}

public sealed class CombatAutoTurnResult
{
    public CombatAgentCompletionReason Reason { get; set; }

    public bool Forced { get; set; }

    public int CommittedActions { get; set; }

    public int ConsecutiveFailures { get; set; }

    public string Message { get; set; } = "";
}

public interface ICombatAgentDecisionSource
{
    CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile profile);
}

public sealed class CombatDecisionEngineSource : ICombatAgentDecisionSource
{
    private readonly CombatDecisionEngine engine;

    public CombatDecisionEngineSource(CombatDecisionEngine engine)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile profile)
    {
        return engine.Choose(state, profile);
    }
}

public interface ICombatAgentRuntimePort
{
    bool TryObserve(out CombatAgentObservation observation, out string reason);

    CombatAgentPreflightResult Preflight(
        CombatAgentObservation observation,
        CombatActionObservation action);

    CombatAgentExecutionResult Execute(
        CombatAgentObservation observation,
        CombatActionObservation action);

    CombatAgentSettlementResult PollSettlement(
        CombatActionObservation action);

    void CompleteTurn(CombatAutoTurnResult result);
}

public sealed class CombatAutoTurnRunner
{
    private readonly CombatAgentDescriptor descriptor;
    private readonly CombatAutoTurnProfile profile;
    private readonly ICombatAgentDecisionSource decisionSource;
    private readonly ICombatAgentRuntimePort runtime;
    private readonly CombatCandidateSuppressionSet suppressions = new();
    private CombatActionObservation? pendingAction;
    private bool pendingEndsTurn;
    private double startedAt = double.NaN;
    private double actionDeadline;
    private string lastFingerprint = "";
    private int repeatedStateCount;
    private int consecutiveFailures;
    private int committedActions;

    public CombatAutoTurnRunner(
        CombatAgentDescriptor descriptor,
        CombatAutoTurnProfile profile,
        ICombatAgentDecisionSource decisionSource,
        ICombatAgentRuntimePort runtime)
    {
        this.descriptor = descriptor
                          ?? throw new ArgumentNullException(nameof(descriptor));
        this.profile = (profile
                        ?? throw new ArgumentNullException(nameof(profile)))
            .Normalize();
        this.decisionSource = decisionSource
                              ?? throw new ArgumentNullException(nameof(decisionSource));
        this.runtime = runtime
                       ?? throw new ArgumentNullException(nameof(runtime));
    }

    public CombatAutoTurnResult? Result { get; private set; }

    public CombatAutoTurnStepStatus Step(double nowSeconds)
    {
        if (Result != null)
        {
            return CombatAutoTurnStepStatus.Completed;
        }

        if (double.IsNaN(startedAt))
        {
            startedAt = nowSeconds;
        }
        if (nowSeconds - startedAt >= profile.TurnTimeoutSeconds)
        {
            return Finish(CombatAgentCompletionReason.TurnTimeout, true,
                "autonomous turn timed out");
        }
        if (pendingAction != null)
        {
            return PollPendingAction(nowSeconds);
        }

        if (!TryObserve(out var observation, out var observeFailure))
        {
            return RecordFailure(observeFailure);
        }
        if (!observation.BattleActive)
        {
            return Finish(CombatAgentCompletionReason.BattleEnded, true,
                "battle ended while actor was taking its turn");
        }
        if (!observation.ActorAlive)
        {
            return Finish(CombatAgentCompletionReason.ActorDead, true,
                "actor is no longer alive");
        }
        if (!observation.ActionWindowOpen)
        {
            return CombatAutoTurnStepStatus.Waiting;
        }

        suppressions.Apply(observation.State);
        TrackRepeatedState(observation.State);
        if (repeatedStateCount >= profile.MaxRepeatedStateObservations)
        {
            return Finish(CombatAgentCompletionReason.RepeatedState, true,
                "actor state stopped making progress");
        }

        var legalActions = observation.State.Actions
            .Where(action => action != null && action.Legal)
            .ToArray();
        var actionable = legalActions
            .Where(action => action.Kind != CombatActionKind.EndTurn)
            .ToArray();
        if (actionable.Length == 0)
        {
            return Finish(CombatAgentCompletionReason.NoLegalAction, true,
                "no legal actor actions remain");
        }

        CombatDecision decision;
        var decisionStart = Stopwatch.GetTimestamp();
        try
        {
            decision = decisionSource.Choose(
                observation.State,
                profile.DecisionProfile);
        }
        catch (Exception ex)
        {
            return RecordFailure("decision failed: " + ex.Message);
        }
        var decisionSeconds =
            (Stopwatch.GetTimestamp() - decisionStart) / (double)Stopwatch.Frequency;
        if (decisionSeconds >= profile.DecisionTimeoutSeconds)
        {
            return Finish(CombatAgentCompletionReason.DecisionTimeout, true,
                "decision exceeded its deadline");
        }
        if (decision?.HasAction != true || decision.Action == null)
        {
            return RecordFailure(decision?.Reason ?? "decision returned no action");
        }

        var action = decision.Action;
        if (!IsCurrentLegalAction(action, legalActions))
        {
            suppressions.Add(action, CombatAgentFailureScope.Candidate,
                "decision selected a stale or suppressed candidate");
            return RecordFailure("decision selected an unavailable action");
        }
        if (action.Kind == CombatActionKind.EndTurn)
        {
            return Finish(CombatAgentCompletionReason.VoluntaryEnd, false,
                "decision selected end turn");
        }

        if (!TryPreflight(observation, action, out var preflight))
        {
            suppressions.Add(action, preflight.FailureScope, preflight.Reason);
            return RecordFailure(preflight.Reason);
        }

        CombatAgentExecutionResult execution;
        try
        {
            execution = runtime.Execute(observation, action)
                        ?? CombatAgentExecutionResult.Reject(
                            "actor runtime returned no execution result");
        }
        catch (Exception ex)
        {
            execution = CombatAgentExecutionResult.Reject(
                "execution failed: " + ex.Message,
                CombatAgentFailureScope.CardInstance);
        }
        if (!execution.Accepted)
        {
            suppressions.Add(action, execution.FailureScope, execution.Message);
            if (execution.FailureScope == CombatAgentFailureScope.Committed)
            {
                return Finish(CombatAgentCompletionReason.FatalExecutionFailure, true,
                    execution.Message);
            }
            return RecordFailure(execution.Message);
        }

        if (execution.Committed)
        {
            committedActions++;
        }
        pendingEndsTurn = action.Semantics?.EndsTurn == true;
        if (!execution.Settled)
        {
            pendingAction = action;
            actionDeadline = nowSeconds + profile.ActionTimeoutSeconds;
            return CombatAutoTurnStepStatus.Waiting;
        }
        return SettleAction(execution.MeaningfulProgress);
    }

    private CombatAutoTurnStepStatus PollPendingAction(double nowSeconds)
    {
        var action = pendingAction!;
        CombatAgentSettlementResult settlement;
        try
        {
            settlement = runtime.PollSettlement(action)
                         ?? CombatAgentSettlementResult.Pending(
                             "actor runtime returned no settlement result");
        }
        catch (Exception ex)
        {
            return Finish(CombatAgentCompletionReason.FatalExecutionFailure, true,
                "settlement failed after commit: " + ex.Message);
        }
        if (settlement.Settled)
        {
            pendingAction = null;
            return SettleAction(settlement.MeaningfulProgress);
        }
        if (nowSeconds >= actionDeadline)
        {
            pendingAction = null;
            return Finish(CombatAgentCompletionReason.ActionTimeout, true,
                settlement.Message.Length > 0
                    ? settlement.Message
                    : "committed action did not settle");
        }
        return CombatAutoTurnStepStatus.Waiting;
    }

    private CombatAutoTurnStepStatus SettleAction(bool meaningfulProgress)
    {
        if (meaningfulProgress)
        {
            consecutiveFailures = 0;
            repeatedStateCount = 0;
            lastFingerprint = "";
            suppressions.OnMeaningfulProgress();
        }
        if (pendingEndsTurn)
        {
            pendingEndsTurn = false;
            return Finish(CombatAgentCompletionReason.VoluntaryEnd, false,
                "actor action ended the turn");
        }
        if (committedActions >= profile.MaxCommittedActions)
        {
            return Finish(CombatAgentCompletionReason.MaximumActionsReached, true,
                "actor reached its action limit");
        }
        return CombatAutoTurnStepStatus.Running;
    }

    private bool TryObserve(
        out CombatAgentObservation observation,
        out string reason)
    {
        try
        {
            if (runtime.TryObserve(out observation, out reason)
                && observation?.State != null)
            {
                observation.State.Actions ??= new List<CombatActionObservation>();
                reason = "";
                return true;
            }
        }
        catch (Exception ex)
        {
            observation = new CombatAgentObservation();
            reason = "observation failed: " + ex.Message;
            return false;
        }
        observation ??= new CombatAgentObservation();
        reason = string.IsNullOrWhiteSpace(reason)
            ? "actor observation unavailable"
            : reason;
        return false;
    }

    private bool TryPreflight(
        CombatAgentObservation observation,
        CombatActionObservation action,
        out CombatAgentPreflightResult result)
    {
        CombatActionAutomationDescriptor? automation = null;
        if (profile.RequireDeclaredHeadlessActions
            && !CombatActionAutomationRegistry.TryDescribe(
                observation.State,
                action,
                out automation))
        {
            result = CombatAgentPreflightResult.Reject(
                "no headless automation provider declared this action",
                CombatAgentFailureScope.Turn);
            return false;
        }
        if (profile.RequireDeclaredHeadlessActions
            && automation != null
            && !automation.HeadlessSupported)
        {
            result = CombatAgentPreflightResult.Reject(
                automation.Reason.Length > 0
                    ? automation.Reason
                    : "action is not supported without player interaction",
                automation.FailureScope);
            return false;
        }

        try
        {
            result = runtime.Preflight(observation, action)
                     ?? CombatAgentPreflightResult.Reject(
                         "actor runtime returned no preflight result");
        }
        catch (Exception ex)
        {
            result = CombatAgentPreflightResult.Reject(
                "preflight failed: " + ex.Message,
                CombatAgentFailureScope.CardInstance);
        }
        return result.Allowed;
    }

    private CombatAutoTurnStepStatus RecordFailure(string message)
    {
        consecutiveFailures++;
        if (consecutiveFailures >= profile.MaxConsecutiveFailures)
        {
            return Finish(CombatAgentCompletionReason.ConsecutiveFailures, true,
                string.IsNullOrWhiteSpace(message)
                    ? "actor reached its consecutive failure limit"
                    : message);
        }
        return CombatAutoTurnStepStatus.Running;
    }

    private void TrackRepeatedState(CombatStateObservation state)
    {
        var fingerprint = state.Fingerprint ?? "";
        if (fingerprint.Length == 0)
        {
            fingerprint = state.ObservationId ?? "";
        }
        if (fingerprint.Length == 0)
        {
            repeatedStateCount = 0;
            return;
        }
        if (string.Equals(lastFingerprint, fingerprint, StringComparison.Ordinal))
        {
            repeatedStateCount++;
            return;
        }
        lastFingerprint = fingerprint;
        repeatedStateCount = 1;
    }

    private static bool IsCurrentLegalAction(
        CombatActionObservation selected,
        IReadOnlyCollection<CombatActionObservation> legalActions)
    {
        return legalActions.Any(action =>
            ReferenceEquals(action, selected)
            || (!string.IsNullOrWhiteSpace(action.CandidateId)
                && string.Equals(
                    action.CandidateId,
                    selected.CandidateId,
                    StringComparison.Ordinal)
                && action.TargetRuntimeId == selected.TargetRuntimeId));
    }

    private CombatAutoTurnStepStatus Finish(
        CombatAgentCompletionReason reason,
        bool forced,
        string message)
    {
        Result = new CombatAutoTurnResult
        {
            Reason = reason,
            Forced = forced,
            CommittedActions = committedActions,
            ConsecutiveFailures = consecutiveFailures,
            Message = message ?? ""
        };
        try
        {
            runtime.CompleteTurn(Result);
        }
        catch (Exception ex)
        {
            Result.Message = Result.Message.Length == 0
                ? "completion failed: " + ex.Message
                : Result.Message + "; completion failed: " + ex.Message;
        }
        return CombatAutoTurnStepStatus.Completed;
    }
}

internal sealed class CombatCandidateSuppressionSet
{
    private readonly List<CombatCandidateSuppression> entries = new();

    public void Add(
        CombatActionObservation action,
        CombatAgentFailureScope scope,
        string reason)
    {
        if (action == null)
        {
            return;
        }
        var entry = CombatCandidateSuppression.Create(action, scope, reason);
        if (!entries.Any(existing => existing.SameIdentity(entry)))
        {
            entries.Add(entry);
        }
    }

    public void Apply(CombatStateObservation state)
    {
        foreach (var action in state.Actions.Where(action => action != null))
        {
            var match = entries.LastOrDefault(entry => entry.Matches(action));
            if (match == null)
            {
                continue;
            }
            action.Legal = false;
            action.RejectionReason = match.Reason;
        }
    }

    public void OnMeaningfulProgress()
    {
        entries.RemoveAll(entry =>
            entry.Scope == CombatAgentFailureScope.Candidate
            || entry.Scope == CombatAgentFailureScope.CardInstance);
    }

    private sealed class CombatCandidateSuppression
    {
        public CombatAgentFailureScope Scope { get; set; }

        public string CandidateId { get; set; } = "";

        public string SourceId { get; set; } = "";

        public int RuntimeId { get; set; }

        public int TargetRuntimeId { get; set; }

        public string Reason { get; set; } = "";

        public static CombatCandidateSuppression Create(
            CombatActionObservation action,
            CombatAgentFailureScope scope,
            string reason)
        {
            return new CombatCandidateSuppression
            {
                Scope = scope,
                CandidateId = action.CandidateId ?? "",
                SourceId = action.SourceId ?? "",
                RuntimeId = action.RuntimeId,
                TargetRuntimeId = action.TargetRuntimeId,
                Reason = string.IsNullOrWhiteSpace(reason)
                    ? "candidate suppressed for this actor turn"
                    : reason
            };
        }

        public bool Matches(CombatActionObservation action)
        {
            switch (Scope)
            {
                case CombatAgentFailureScope.CardInstance:
                case CombatAgentFailureScope.Committed:
                    return RuntimeId != 0 && action.RuntimeId == RuntimeId;
                case CombatAgentFailureScope.Turn:
                    return SourceId.Length > 0
                           && string.Equals(
                               SourceId,
                               action.SourceId,
                               StringComparison.Ordinal);
                default:
                    return CandidateId.Length > 0
                           && string.Equals(
                               CandidateId,
                               action.CandidateId,
                               StringComparison.Ordinal)
                           && action.TargetRuntimeId == TargetRuntimeId;
            }
        }

        public bool SameIdentity(CombatCandidateSuppression other)
        {
            return Scope == other.Scope
                   && CandidateId == other.CandidateId
                   && SourceId == other.SourceId
                   && RuntimeId == other.RuntimeId
                   && TargetRuntimeId == other.TargetRuntimeId;
        }
    }
}

public enum CombatActorCardZone
{
    DrawPile,
    Hand,
    DiscardPile,
    ExhaustPile,
    Wait,
    Retained
}

public sealed class CombatCardInstanceSnapshot
{
    public string InstanceId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string DefinitionType { get; set; } = "Card";

    public CombatActorCardZone Zone { get; set; }

    public int ZoneIndex { get; set; }

    public int EffectiveCost { get; set; }

    public bool Retained { get; set; }

    public bool ExhaustsOnUse { get; set; }

    public Dictionary<string, double> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> RuntimeVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> RuntimeData { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Tags { get; set; } = new();

    public List<string> Attachments { get; set; } = new();

    public List<CombatCardAttachmentSnapshot> AttachmentStates { get; set; } = new();

    public CombatCardInstanceSnapshot DeepClone()
    {
        return new CombatCardInstanceSnapshot
        {
            InstanceId = InstanceId,
            SourceInstanceId = SourceInstanceId,
            CardId = CardId,
            DefinitionType = DefinitionType,
            Zone = Zone,
            ZoneIndex = ZoneIndex,
            EffectiveCost = EffectiveCost,
            Retained = Retained,
            ExhaustsOnUse = ExhaustsOnUse,
            Variables = new Dictionary<string, double>(
                Variables ?? new Dictionary<string, double>(),
                StringComparer.OrdinalIgnoreCase),
            RuntimeVariables = new Dictionary<string, string>(
                RuntimeVariables ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
            RuntimeData = new Dictionary<string, string>(
                RuntimeData ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
            Tags = (Tags ?? new List<string>()).ToList(),
            Attachments = (Attachments ?? new List<string>()).ToList(),
            AttachmentStates = (AttachmentStates
                                ?? new List<CombatCardAttachmentSnapshot>())
                .Where(attachment => attachment != null)
                .Select(attachment => attachment.DeepClone())
                .ToList()
        };
    }
}

public sealed class CombatCardAttachmentSnapshot
{
    public string AttachmentId { get; set; } = "";

    public string DefinitionType { get; set; } = "EnchTag";

    public Dictionary<string, string> RuntimeData { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatCardAttachmentSnapshot DeepClone()
    {
        return new CombatCardAttachmentSnapshot
        {
            AttachmentId = AttachmentId,
            DefinitionType = DefinitionType,
            RuntimeData = new Dictionary<string, string>(
                RuntimeData ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
            Variables = new Dictionary<string, string>(
                Variables ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class CombatActorCardStateSnapshot
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;

    public long BattleSessionId { get; set; }

    public string OwnerModId { get; set; } = "";

    public string ActorId { get; set; } = "";

    public int CurrentPower { get; set; }

    public int MaxPower { get; set; }

    public bool DrawAtNextTurnStart { get; set; }

    public List<CombatCardInstanceSnapshot> Cards { get; set; } = new();

    public Dictionary<string, double> RuntimeVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatActorCardStateSnapshot DeepClone()
    {
        return new CombatActorCardStateSnapshot
        {
            ProtocolVersion = ProtocolVersion,
            BattleSessionId = BattleSessionId,
            OwnerModId = OwnerModId,
            ActorId = ActorId,
            CurrentPower = CurrentPower,
            MaxPower = MaxPower,
            DrawAtNextTurnStart = DrawAtNextTurnStart,
            Cards = (Cards ?? new List<CombatCardInstanceSnapshot>())
                .Where(card => card != null)
                .Select(card => card.DeepClone())
                .ToList(),
            RuntimeVariables = new Dictionary<string, double>(
                RuntimeVariables ?? new Dictionary<string, double>(),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    public bool Validate(out string reason)
    {
        if (ProtocolVersion != CurrentProtocolVersion)
        {
            reason = "unsupported actor card snapshot protocol";
            return false;
        }
        if (string.IsNullOrWhiteSpace(OwnerModId)
            || string.IsNullOrWhiteSpace(ActorId))
        {
            reason = "snapshot owner and actor ids are required";
            return false;
        }
        if (CurrentPower < 0 || MaxPower < 0 || CurrentPower > MaxPower)
        {
            reason = "snapshot power values are invalid";
            return false;
        }
        var cards = Cards ?? new List<CombatCardInstanceSnapshot>();
        if (cards.Any(card =>
                card == null
                || string.IsNullOrWhiteSpace(card.InstanceId)
                || string.IsNullOrWhiteSpace(card.CardId)
                || card.ZoneIndex < 0))
        {
            reason = "snapshot contains an invalid card instance";
            return false;
        }
        if (cards.GroupBy(card => card.InstanceId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            reason = "snapshot card instance ids must be unique";
            return false;
        }
        reason = "";
        return true;
    }
}

public sealed class CombatActionAutomationDescriptor
{
    public string OwnerModId { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public bool HeadlessSupported { get; set; }

    public CombatAgentFailureScope FailureScope { get; set; } =
        CombatAgentFailureScope.Turn;

    public string Reason { get; set; } = "";
}

public interface ICombatActionAutomationProvider
{
    bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionAutomationDescriptor descriptor);
}

public static class CombatActionAutomationRegistry
{
    private static readonly object Sync = new();
    private static readonly List<Registration> Registrations = new();

    public static IDisposable Register(
        string ownerModId,
        string providerId,
        ICombatActionAutomationProvider provider,
        int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(ownerModId))
        {
            throw new ArgumentException("owner mod id is required", nameof(ownerModId));
        }
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("provider id is required", nameof(providerId));
        }
        if (provider == null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        var registration = new Registration(
            ownerModId.Trim(),
            providerId.Trim(),
            provider,
            priority);
        lock (Sync)
        {
            Registrations.RemoveAll(item =>
                string.Equals(item.OwnerModId, registration.OwnerModId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ProviderId, registration.ProviderId,
                    StringComparison.OrdinalIgnoreCase));
            Registrations.Add(registration);
        }
        return registration;
    }

    public static bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionAutomationDescriptor? descriptor)
    {
        Registration[] snapshot;
        lock (Sync)
        {
            snapshot = Registrations
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.OwnerModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        foreach (var registration in snapshot)
        {
            try
            {
                if (!registration.Provider.TryDescribe(
                        state,
                        action,
                        out var candidate)
                    || candidate == null)
                {
                    continue;
                }
                candidate.OwnerModId = registration.OwnerModId;
                candidate.ProviderId = registration.ProviderId;
                descriptor = candidate;
                return true;
            }
            catch
            {
                // A foreign provider cannot prevent the remaining providers.
            }
        }
        descriptor = null;
        return false;
    }

    private sealed class Registration : IDisposable
    {
        private bool disposed;

        public Registration(
            string ownerModId,
            string providerId,
            ICombatActionAutomationProvider provider,
            int priority)
        {
            OwnerModId = ownerModId;
            ProviderId = providerId;
            Provider = provider;
            Priority = priority;
        }

        public string OwnerModId { get; }

        public string ProviderId { get; }

        public ICombatActionAutomationProvider Provider { get; }

        public int Priority { get; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            lock (Sync)
            {
                Registrations.Remove(this);
            }
        }
    }
}
