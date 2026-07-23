using System;
using System.Collections.Generic;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public enum CombatActionKind
{
    PlayCard,
    UseSkill,
    EndTurn,
    ResolvePrompt
}

public enum CombatTargetKind
{
    None,
    Self,
    Friendly,
    Enemy,
    Object
}

public enum CombatPromptKind
{
    Unknown,
    ChooseCards,
    BurnCards,
    Guidance,
    ChooseHandCards
}

public enum CombatPromptZone
{
    Unknown,
    Hand,
    DrawPile,
    DiscardPile,
    Deck,
    Generated
}

public enum CombatInteractionState
{
    None,
    AwaitingUi,
    AwaitingChoice,
    Resolving,
    Completed,
    HandedToPlayer,
    Failed
}

public sealed class CombatUnitObservation
{
    public int RuntimeId { get; set; }

    public string Name { get; set; } = "";

    public CombatTargetKind Kind { get; set; }

    public int CurrentHp { get; set; }

    public int MaxHp { get; set; }

    public int Defend { get; set; }

    public bool Alive => CurrentHp > 0;
}

public sealed class CombatActionSemantics
{
    public double Damage { get; set; }

    public double Defend { get; set; }

    public double Heal { get; set; }

    public double Draw { get; set; }

    public double EnergyGain { get; set; }

    public double Scaling { get; set; }

    public double DeckValue { get; set; }

    public double Risk { get; set; }

    public double Uncertainty { get; set; }

    public bool OpensInteraction { get; set; }

    public bool RandomOutcome { get; set; }
}

public sealed class CombatActionObservation
{
    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public CombatActionKind Kind { get; set; }

    public int RuntimeId { get; set; }

    public int TargetRuntimeId { get; set; }

    public CombatTargetKind TargetKind { get; set; }

    public int Cost { get; set; }

    public bool Legal { get; set; } = true;

    public string RejectionReason { get; set; } = "";

    public CombatActionSemantics Semantics { get; set; } = new();

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public object? RuntimeHandle { get; set; }

    public object? TargetHandle { get; set; }
}

public sealed class CombatStateObservation
{
    public long BattleSessionId { get; set; }

    public long Sequence { get; set; }

    public CombatUnitObservation Player { get; set; } = new();

    public List<CombatUnitObservation> Friendlies { get; set; } = new();

    public List<CombatUnitObservation> Enemies { get; set; } = new();

    public List<CombatActionObservation> Actions { get; set; } = new();

    public int CurrentPower { get; set; }

    public int MaxPower { get; set; }

    public int HandCount { get; set; }

    public bool IsPlayerActionWindow { get; set; }

    public bool UiBusy { get; set; }

    public string Fingerprint { get; set; } = "";
}

public sealed class CombatDecisionProfile
{
    public string Id { get; set; } = "balanced";

    public DecisionWeights Weights { get; set; } = new();

    public DecisionGraph? Graph { get; set; }

    public double MinimumActionScore { get; set; } = 0.05d;

    public double UnknownActionPenalty { get; set; } = 2d;
}

public sealed class CombatDecision
{
    public bool HasAction { get; set; }

    public CombatActionObservation? Action { get; set; }

    public double Score { get; set; }

    public string Reason { get; set; } = "";
}

public sealed class CombatExecutionResult
{
    public bool Accepted { get; set; }

    public string Message { get; set; } = "";

    public static CombatExecutionResult Success(string message = "accepted")
    {
        return new CombatExecutionResult { Accepted = true, Message = message };
    }

    public static CombatExecutionResult Rejected(string message)
    {
        return new CombatExecutionResult { Accepted = false, Message = message ?? "rejected" };
    }
}

public interface ICombatObservationProvider
{
    bool TryCapture(out CombatStateObservation observation, out string reason);
}

public interface ICombatActionExecutor
{
    CombatExecutionResult Execute(CombatActionObservation action);
}

public interface ICombatSemanticProvider
{
    bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionSemantics semantics);
}

public interface ICombatPreflightRule
{
    bool IsLegal(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason);
}

public interface ICombatTrainingSampleSink
{
    void Record(CombatTrainingSample sample);
}

public sealed class CombatTrainingSample
{
    public string ModelProtocol { get; set; } = "aura.combat-ai.sample.v1";

    public long BattleSessionId { get; set; }

    public long Sequence { get; set; }

    public string StateFingerprint { get; set; } = "";

    public string CandidateId { get; set; } = "";

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double PredictedScore { get; set; }

    public double Reward { get; set; }

    public bool Terminal { get; set; }
}
