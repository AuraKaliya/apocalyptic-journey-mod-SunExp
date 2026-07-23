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
    DiscardCards,
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

public enum CombatIntentKind
{
    Unknown,
    Attack,
    Defend,
    Heal,
    Buff,
    Debuff,
    DamageOverTime,
    Summon
}

public sealed class CombatUnitObservation
{
    public int RuntimeId { get; set; }

    public string Name { get; set; } = "";

    public CombatTargetKind Kind { get; set; }

    public int CurrentHp { get; set; }

    public int MaxHp { get; set; }

    public int Defend { get; set; }

    public double Attack { get; set; }

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Alive => CurrentHp > 0;
}

public sealed class CombatIntentObservation
{
    public string SourceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public CombatIntentKind Kind { get; set; }

    public int SourceRuntimeId { get; set; }

    public double Probability { get; set; } = 1d;

    public double BlockableDamage { get; set; }

    public double UnblockableDamage { get; set; }

    public double DamageOverTime { get; set; }

    public double Confidence { get; set; }

    public bool Current { get; set; }
}

public sealed class CombatThreatForecast
{
    public bool CurrentIntentKnown { get; set; }

    public int IntentPoolSize { get; set; }

    public double AttackProbability { get; set; }

    public double ExpectedBlockableDamage { get; set; }

    public double MaximumBlockableDamage { get; set; }

    public double ExpectedUnblockableDamage { get; set; }

    public double ExpectedDamageOverTime { get; set; }

    public double LethalProbability { get; set; }

    public double Confidence { get; set; }

    public string Summary { get; set; } = "";

    public List<CombatIntentObservation> Intents { get; set; } = new();

    public double RiskAdjustedBlockableDamage(double riskTolerance)
    {
        var normalized = Math.Max(0d, Math.Min(1d, riskTolerance));
        var expected = Math.Max(0d, ExpectedBlockableDamage);
        var maximum = Math.Max(expected, MaximumBlockableDamage);
        return expected + (maximum - expected) * normalized;
    }
}

public sealed class CombatActionSemantics
{
    public double Damage { get; set; }

    public double TrueDamage { get; set; }

    public double DamageOverTime { get; set; }

    public double HitCount { get; set; } = 1d;

    public double Defend { get; set; }

    public double Heal { get; set; }

    public double Draw { get; set; }

    public double EnergyGain { get; set; }

    public double Scaling { get; set; }

    public double DeckValue { get; set; }

    public double Buff { get; set; }

    public double Debuff { get; set; }

    public double Cleanse { get; set; }

    public double CostReduction { get; set; }

    public double CardGeneration { get; set; }

    public double PersistentValue { get; set; }

    public double CooldownTurns { get; set; }

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

    public double ExpectedIncomingDamage { get; set; }

    public CombatThreatForecast Threat { get; set; } = new();

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

    public double EmergencyHpRatio { get; set; } = 0.3d;

    public double FreeActionTieBreaker { get; set; } = 0.25d;

    public double SkillCooldownPenalty { get; set; } = 0.3d;

    public double ThreatRiskTolerance { get; set; } = 0.65d;

    public double SurplusDefendRetention { get; set; } = 0.05d;

    public int BeamWidth { get; set; } = 8;

    public int MaxPlanDepth { get; set; } = 8;
}

public sealed class CombatCandidateEvaluation
{
    public CombatActionObservation Action { get; set; } = new();

    public bool Legal { get; set; }

    public string RejectionReason { get; set; } = "";

    public DecisionUtility Utility { get; set; } = new();

    public double BaseRuleScore { get; set; }

    public double RawResidualScore { get; set; }

    public double ResidualApplicability { get; set; }

    public double AppliedResidualScore { get; set; }

    public double RuleScore { get; set; }

    public double PlanScore { get; set; }
}

public sealed class CombatPlanStep
{
    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public double StepScore { get; set; }

    public double CumulativeScore { get; set; }

    public int RemainingPower { get; set; }
}

public sealed class CombatDecision
{
    public bool HasAction { get; set; }

    public CombatActionObservation? Action { get; set; }

    public double Score { get; set; }

    public string Reason { get; set; } = "";

    public string ProfileId { get; set; } = "";

    public List<CombatCandidateEvaluation> Candidates { get; set; } = new();

    public List<CombatPlanStep> Plan { get; set; } = new();

    public string PlanSummary { get; set; } = "";
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

public interface ICombatThreatProvider
{
    bool TryForecast(
        CombatStateObservation state,
        out CombatThreatForecast forecast);
}

public interface ICombatTrainingSampleSink
{
    void Record(CombatTrainingSample sample);
}

public sealed class CombatTrainingSample
{
    public string ModelProtocol { get; set; } = "aura.combat-ai.sample.v4";

    public int FeatureSchemaVersion { get; set; } = 4;

    public string GameBuild { get; set; } = "";

    public string SharedBuild { get; set; } = "";

    public string OwnerModSetHash { get; set; } = "";

    public long BattleSessionId { get; set; }

    public long DecisionIndex { get; set; }

    public long Sequence { get; set; }

    public long TransactionId { get; set; }

    public string StateFingerprint { get; set; } = "";

    public string NextStateFingerprint { get; set; } = "";

    public string DecisionProfile { get; set; } = "";

    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string Demonstrator { get; set; } = "policy";

    public string RecommendedCandidateId { get; set; } = "";

    public CombatTrainingSelectionTrace Selection { get; set; } = new();

    public string PlanSummary { get; set; } = "";

    public List<CombatPlanStep> Plan { get; set; } = new();

    public List<CombatTrainingCandidate> Candidates { get; set; } = new();

    public Dictionary<string, double> StateFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double PredictedScore { get; set; }

    public CombatTrainingReward RewardComponents { get; set; } = new();

    public double Reward { get; set; }

    public bool Terminal { get; set; }

    public string BattleOutcome { get; set; } = "unknown";

    public string CompletionState { get; set; } = "";

    public string TerminalReason { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CombatTrainingSelectionTrace
{
    public string Protocol { get; set; } = "aura.combat-ai.selection.v1";

    public string ExecutedBy { get; set; } = "policy";

    public string LabelKind { get; set; } = "policy-trajectory";

    public string ExecutedCandidateId { get; set; } = "";

    public string ExecutedDisplayName { get; set; } = "";

    public string PolicyPreselectedCandidateId { get; set; } = "";

    public string PolicyPreselectedDisplayName { get; set; } = "";

    public bool PolicyWasExecuted { get; set; }

    public bool HumanPolicyAgreement { get; set; }

    public bool PolicyVisibleToHuman { get; set; }
}

public sealed class CombatTrainingCandidate
{
    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string ActionKind { get; set; } = "";

    public string TargetKind { get; set; } = "";

    public int Cost { get; set; }

    public bool Legal { get; set; }

    public string RejectionReason { get; set; } = "";

    public bool IsExecutedAction { get; set; }

    public bool IsHumanSelection { get; set; }

    public bool IsPolicyPreselection { get; set; }

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CombatActionSemantics Semantics { get; set; } = new();

    public CombatTrainingUtility Utility { get; set; } = new();

    public double BaseRuleScore { get; set; }

    public double RawResidualScore { get; set; }

    public double ResidualApplicability { get; set; }

    public double AppliedResidualScore { get; set; }

    public double RuleScore { get; set; }
}

public sealed class CombatTrainingUtility
{
    public double Survival { get; set; }

    public double Lethal { get; set; }

    public double Tempo { get; set; }

    public double Resource { get; set; }

    public double DeckEconomy { get; set; }

    public double Scaling { get; set; }

    public double Synergy { get; set; }

    public double Continuation { get; set; }

    public double Risk { get; set; }

    public double Uncertainty { get; set; }

    public double Coordination { get; set; }
}

public sealed class CombatTrainingReward
{
    public double EffectiveDamage { get; set; }

    public double PlayerHpChange { get; set; }

    public double ShieldGain { get; set; }

    public double UsefulDefend { get; set; }

    public double WastedDefend { get; set; }

    public double UnblockableThreat { get; set; }

    public double PowerChange { get; set; }

    public double HandChange { get; set; }

    public double TurnCost { get; set; }

    public double TerminalBonus { get; set; }
}
