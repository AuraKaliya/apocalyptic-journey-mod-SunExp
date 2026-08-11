using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;
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

public enum CombatEffectKind
{
    Damage,
    TrueDamage,
    DamageOverTime,
    GainDefend,
    Heal,
    Draw,
    GainEnergy,
    SetEnergy,
    SetCardCostMultiplier,
    ReduceCost,
    Buff,
    Debuff,
    Cleanse,
    GenerateCard,
    RetrieveCards,
    PersistentValue,
    Scaling,
    DamageMultiplier
}

public sealed class CombatEffectOperation
{
    public CombatEffectKind Kind { get; set; }

    public int TargetRuntimeId { get; set; }

    public double Magnitude { get; set; }

    public double SecondaryMagnitude { get; set; }

    public string SemanticId { get; set; } = "";

    public CombatCardZoneKind SourceCardZone { get; set; }

    public CombatCardZoneKind DestinationCardZone { get; set; }

    public int SelectionRank { get; set; }
}

public enum CombatCardZoneKind
{
    DrawPile,
    Hand,
    DiscardPile,
    ExhaustPile
}

public sealed class CombatCardRetrievalSemantic
{
    public CombatCardZoneKind SourceZone { get; set; }

    public CombatCardZoneKind DestinationZone { get; set; } =
        CombatCardZoneKind.Hand;

    public int Amount { get; set; }

    public string RequiredCardTag { get; set; } = "";

    public int CandidateBranchCount { get; set; } = 3;
}

public sealed class CombatHandTransformSemantic
{
    public string TargetCardId { get; set; } = "";

    public CombatActionSemantics TargetCardSemantics { get; set; } = new();

    public bool TransformAllHandCards { get; set; } = true;

    public bool PreserveInstances { get; set; } = true;

    public bool ClearsEnhancements { get; set; }

    public bool ClearsVariables { get; set; }

    public bool TargetRetained { get; set; }

    public bool TargetExhaustsOnUse { get; set; }

    public string GrowthStateKey { get; set; } = "";

    public double GrowthPerExhaust { get; set; }

    public double CurrentGrowthValue { get; set; }

    public int TargetTier { get; set; }

    public int NextTierThreshold { get; set; }

    public double CooldownProgressRequired { get; set; }

    public string CooldownProgressEvent { get; set; } = "";
}

public sealed class CombatActionOutcome
{
    public string OutcomeId { get; set; } = "";

    public double Probability { get; set; } = 1d;

    public List<CombatEffectOperation> Effects { get; set; } = new();
}

public sealed class CombatActionModel
{
    public string ModelId { get; set; } = "semantic-default";

    public double Confidence { get; set; } = 1d;

    public List<CombatActionOutcome> Outcomes { get; set; } = new();
}

public sealed class CombatUnitObservation
{
    public int RuntimeId { get; set; }

    public string DefinitionId { get; set; } = "";

    public string Name { get; set; } = "";

    public CombatTargetKind Kind { get; set; }

    public int CurrentHp { get; set; }

    public int MaxHp { get; set; }

    public int Defend { get; set; }

    public double Attack { get; set; }

    public List<CombatStatusObservation> Statuses { get; set; } = new();

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Alive => CurrentHp > 0;
}

public sealed class CombatStatusObservation
{
    public string StatusId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Level { get; set; }

    public int Rarity { get; set; } = 1;

    public int UpperBound { get; set; }

    public int ReducePerTurn { get; set; }

    public int ReducePerUse { get; set; }

    public int ReducePerAttacked { get; set; }

    public string Type { get; set; } = "";
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

public enum CombatSemanticEffectPhase
{
    Immediate,
    PostAction,
    Deferred
}

public enum CombatSemanticEffectKind
{
    Damage,
    TrueDamage,
    DirectHpLoss,
    Defend,
    Heal,
    AddStatus,
    RemoveStatus,
    StateChange
}

public enum CombatSemanticEffectAttribution
{
    DirectAction,
    ActionTriggeredContext,
    PhaseTriggered,
    ExternalOrUnknown
}

public sealed class CombatTargetedSemanticEffect
{
    public CombatSemanticEffectPhase Phase { get; set; }

    public CombatSemanticEffectKind Kind { get; set; }

    public CombatSemanticEffectAttribution Attribution { get; set; } =
        CombatSemanticEffectAttribution.DirectAction;

    public int TargetRuntimeId { get; set; }

    public string DefinitionId { get; set; } = "";

    public string Trigger { get; set; } = "";

    public string SourceDefinitionId { get; set; } = "";

    public long SourceActionId { get; set; }

    public long Sequence { get; set; }

    public long ParentSequence { get; set; }

    public long CausalChainId { get; set; }

    public int TriggerWave { get; set; }

    public double RawAmount { get; set; }

    public double EffectiveAmount { get; set; }

    public double EffectiveDurabilityAmount { get; set; }

    public double BlockedAmount { get; set; }

    public double Probability { get; set; } = 1d;

    public bool BypassesBlock { get; set; }

    public bool Contextual { get; set; }

    public CombatTargetedSemanticEffect Clone()
    {
        return (CombatTargetedSemanticEffect)MemberwiseClone();
    }
}

public sealed class CombatActionSemantics
{
    public double Damage { get; set; }

    public double TrueDamage { get; set; }

    public double DamageOverTime { get; set; }

    public double SelfHpLoss { get; set; }

    public double DirectDamage { get; set; }

    public double ContextDamage { get; set; }

    public double DirectSelfHpLoss { get; set; }

    public double ContextSelfHpLoss { get; set; }

    public double DirectHeal { get; set; }

    public double ContextHeal { get; set; }

    public double ObservedNetHpDelta { get; set; }

    public double MinimumHpDuringAction { get; set; }

    public bool LethalBeforeRecovery { get; set; }

    public double EndOfCycleSelfHpLoss { get; set; }

    public double HitCount { get; set; } = 1d;

    public double Defend { get; set; }

    public double Heal { get; set; }

    public double Draw { get; set; }

    public double EnergyGain { get; set; }

    public double? EnergySetAmount { get; set; }

    public double? EnergyMinimum { get; set; }

    public bool RestoreEnergyToMaximum { get; set; }

    public List<CombatCardRetrievalSemantic> CardRetrievals { get; set; } =
        new();

    public double Scaling { get; set; }

    public double DeckValue { get; set; }

    public double Buff { get; set; }

    public double Debuff { get; set; }

    public double Cleanse { get; set; }

    public double CostReduction { get; set; }

    public double CardGeneration { get; set; }

    public double PersistentValue { get; set; }

    public double DamageMultiplierGain { get; set; }

    public double ImmediateHpDamage { get; set; }

    public double ImmediateDurabilityDamage { get; set; }

    public double DeferredHpDamage { get; set; }

    public int AffectedEnemyCount { get; set; }

    public List<CombatTargetedSemanticEffect> TargetEffects { get; set; } =
        new();

    public Dictionary<string, double> StateChanges { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public double CooldownTurns { get; set; }

    public double Risk { get; set; }

    public double Uncertainty { get; set; }

    public bool OpensInteraction { get; set; }

    public CombatInteractionDefinition? Interaction { get; set; }

    public bool RandomOutcome { get; set; }

    /// <summary>
    /// Resolving this action immediately hands control to the turn lifecycle.
    /// It is deliberately separate from <see cref="CombatActionKind.EndTurn"/>
    /// because ordinary cards and skills may call the native ChangeRound API.
    /// </summary>
    public bool EndsTurn { get; set; }

    /// <summary>
    /// Damage dealt after this setup is recorded and converted into block by
    /// the turn lifecycle. This makes setup-before-damage strictly dominate
    /// the reverse order when both actions fit in the current energy budget.
    /// </summary>
    public bool DamageToBlockSetup { get; set; }

    public CombatHandTransformSemantic? HandTransform { get; set; }
}

public static class CombatActionSemanticMetrics
{
    public static double ImmediateHpDamage(CombatActionSemantics? semantics)
    {
        if (semantics == null)
        {
            return 0d;
        }
        if (semantics.TargetEffects.Count > 0)
        {
            return semantics.TargetEffects
                .Where(item =>
                    item.Phase == CombatSemanticEffectPhase.Immediate
                    && item.Kind is CombatSemanticEffectKind.Damage
                        or CombatSemanticEffectKind.TrueDamage
                        or CombatSemanticEffectKind.DirectHpLoss)
                .Sum(item =>
                    Math.Max(0d, item.EffectiveAmount)
                    * Math.Max(0d, Math.Min(1d, item.Probability)));
        }
        if (semantics.ImmediateHpDamage > 0d)
        {
            return semantics.ImmediateHpDamage;
        }
        return Math.Max(0d, semantics.Damage)
               * Math.Max(1d, semantics.HitCount)
               + Math.Max(0d, semantics.TrueDamage);
    }

    public static double DeferredHpDamage(CombatActionSemantics? semantics)
    {
        if (semantics == null)
        {
            return 0d;
        }
        if (semantics.TargetEffects.Count > 0)
        {
            return semantics.TargetEffects
                .Where(item =>
                    item.Phase == CombatSemanticEffectPhase.Deferred
                    && item.Kind is CombatSemanticEffectKind.Damage
                        or CombatSemanticEffectKind.TrueDamage
                        or CombatSemanticEffectKind.DirectHpLoss)
                .Sum(item =>
                    Math.Max(0d, item.EffectiveAmount)
                    * Math.Max(0d, Math.Min(1d, item.Probability)));
        }
        return Math.Max(
            Math.Max(0d, semantics.DeferredHpDamage),
            Math.Max(0d, semantics.DamageOverTime));
    }
}

public sealed class CombatActionObservation
{
    public string ObservationId { get; set; } = "";

    public string ActionToken { get; set; } = "";

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

    public string SemanticSource { get; set; } = "runtime-heuristic";

    public CombatKnowledgeFidelity SemanticFidelity { get; set; } =
        CombatKnowledgeFidelity.Approximate;

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatCardInstanceObservation
{
    public int RuntimeId { get; set; }

    public string CardId { get; set; } = "";

    public int EffectiveCost { get; set; }

    public bool Retained { get; set; }

    public bool ExhaustsOnUse { get; set; }

    public bool CreatedThisBattle { get; set; }

    public int EnhancementCount { get; set; }

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatDeferredEffectObservation
{
    public int Sequence { get; set; }

    public string StatusId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public int TargetRuntimeId { get; set; }

    public CombatActionSemantics Semantics { get; set; } = new();
}

public sealed class CombatStateObservation
{
    public int InformationBoundaryVersion { get; set; } = 2;

    public string ObservationId { get; set; } = "";

    public long BattleSessionId { get; set; }

    public long Sequence { get; set; }

    public CombatUnitObservation Player { get; set; } = new();

    public List<CombatUnitObservation> Friendlies { get; set; } = new();

    public List<CombatUnitObservation> Enemies { get; set; } = new();

    public List<CombatActionObservation> Actions { get; set; } = new();

    public int CurrentPower { get; set; }

    public int MaxPower { get; set; }

    public int HandCount { get; set; }

    public List<string> HandCardIds { get; set; } = new();

    public List<CombatCardInstanceObservation> HandCards { get; set; } = new();

    public List<string> RetainedHandCardIds { get; set; } = new();

    public List<string> DeckCardIds { get; set; } = new();

    public List<string> DiscardPileCardIds { get; set; } = new();

    public List<string> ExhaustPileCardIds { get; set; } = new();

    public Dictionary<string, List<string>> CardTagsById { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<CombatDeferredEffectObservation> DeferredEffects { get; set; } = new();

    public CombatDeckKnowledge DeckKnowledge { get; set; } = new();

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

    /// <summary>
    /// When enabled, the learned policy/value stack owns action selection.
    /// Runtime legality and mechanical forward simulation still apply, but
    /// heuristic dominance, quality governance and safety fallbacks may not
    /// replace a legal model proposal.
    /// </summary>
    public bool ModelOwnsActionSelection { get; set; }

    public double MinimumActionScore { get; set; } = 0.05d;

    public double UnknownActionPenalty { get; set; } = 2d;

    public double EmergencyHpRatio { get; set; } = 0.3d;

    public double FreeActionTieBreaker { get; set; } = 0.25d;

    public double SkillCooldownPenalty { get; set; } = 0.3d;

    public double ThreatRiskTolerance { get; set; } = 0.65d;

    public double SurplusDefendRetention { get; set; } = 0.65d;

    public int SearchSimulationBudget { get; set; } = 256;

    public int SearchNodeBudget { get; set; } = 8192;

    public int SearchMaxPly { get; set; } = 10;

    public int SearchMinimumSimulations { get; set; } = 128;

    public int SearchStabilityWindow { get; set; } = 64;

    public int SearchStableChecks { get; set; } = 2;

    public string SearchBudgetMode { get; set; } = "dynamic";

    public string SearchQuality { get; set; } = "balanced";

    public string SearchBudgetContext { get; set; } = "deployment";

    public int SearchTimeBudgetMilliseconds { get; set; } = 450;

    /// <summary>
    /// Zero selects the automatic tier ratio. A positive value is clamped to
    /// the configured hard search deadline.
    /// </summary>
    public int SearchMinimumTimeMilliseconds { get; set; }

    public int SearchMinimumRootVisits { get; set; } = 2;

    public int SearchMinimumChallengerVisits { get; set; } = 4;

    public double SearchEarlyStopConfidence { get; set; } = 0.55d;

    public double SearchDominanceStandardErrors { get; set; } = 1d;

    public int SearchModelEvaluationBudget { get; set; } = 512;

    public double SearchExploration { get; set; } = 1.15d;

    public double DeathRiskLimit { get; set; } = 0.05d;

    public int LoopMaximumCertifiedCycles { get; set; } = 32;

    public int LoopLimitDamageMaximumCycles { get; set; } = 8;

    public double LoopMinimumEffectiveProgress { get; set; } = 1d;

    public double LoopMinimumHpReserveRatio { get; set; } = 0.05d;

    public double TailRiskPenalty { get; set; } = 35d;

    public double TailRiskQuantile { get; set; } = 0.1d;

    public double RiskPreference { get; set; } = 0.5d;

    public double UncertaintyPenalty { get; set; } = 0.75d;

    public double NetworkDeathRiskWeight { get; set; } = 1d;

    public double SemanticCoverageRiskWeight { get; set; } = 0.5d;

    public double SetupValueWeight { get; set; } = 0.8d;

    public double PersistentValueWeight { get; set; } = 1d;

    public double NextTurnThreatRetention { get; set; } = 0.80d;

    public double UnknownNextTurnThreatProbabilityFloor { get; set; } = 0.35d;

    public double EndTurnUncertainty { get; set; } = 0.35d;

    public bool PreferDominantFreeSetup { get; set; } = true;

    public bool UseLowConfidenceFallback { get; set; } = true;

    public double MinimumSearchConfidence { get; set; } = 0.35d;

    public bool EnableActorCandidatePruning { get; set; }

    public int ActorCandidateTopK { get; set; } = 12;

    public double ActorCandidateProbabilityMass { get; set; } = 0.995d;

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

    public double SearchPrior { get; set; }

    public int SearchVisits { get; set; }

    public double SearchDeathRisk { get; set; }

    public double SearchMeanReturn { get; set; }

    public double SearchReturnStandardError { get; set; }

    public double SearchLowerTailMean { get; set; }

    public List<double> SearchReturnQuantiles { get; set; } = new();
}

public sealed class CombatPlanStep
{
    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public double StepScore { get; set; }

    public double CumulativeScore { get; set; }

    public int RemainingPower { get; set; }

    public double DeathRisk { get; set; }

    public int Visits { get; set; }
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

    public string EndTurnTrace { get; set; } = "";

    public int SearchSimulations { get; set; }

    public int SearchNodes { get; set; }

    public int SearchTranspositionHits { get; set; }

    public bool SearchStoppedEarly { get; set; }

    public bool SearchStoppedByTime { get; set; }

    public int SearchMinimumTimeMilliseconds { get; set; }

    public bool SearchMinimumTimeSatisfied { get; set; }

    public bool SearchEarlyStopCertified { get; set; }

    public string SearchStopReason { get; set; } = "";

    public double SearchConfidence { get; set; }

    public double SearchEvidence { get; set; }

    public double PolicyAmbiguity { get; set; }

    public double SemanticCoverageRisk { get; set; }

    public double OutcomeUncertainty { get; set; }

    public double SearchValueGap { get; set; }

    public int SearchBestVisits { get; set; }

    public int SearchSecondBestVisits { get; set; }

    public int SearchCandidateCount { get; set; }

    public int SearchOriginalCandidateCount { get; set; }

    public string SearchBudgetTier { get; set; } = "";

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }

    public string SearchAlgorithm { get; set; } = "";

    public int InferenceWorkerCount { get; set; } = 1;

    public double InferenceAgreement { get; set; } = 1d;

    public string SearchProposedCandidateId { get; set; } = "";

    public string SearchProposedDisplayName { get; set; } = "";

    public string GovernanceDecision { get; set; } = "";

    public string GovernanceReason { get; set; } = "";

    public bool GovernanceFallbackApplied { get; set; }

    public string DecisionPath { get; set; } = "";

    public CombatDecisionPerformanceTelemetry Performance { get; set; } = new();
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

public interface ICombatEffectResolver
{
    bool TryResolve(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionModel model);
}

public interface ICombatSimulationRule
{
    bool IsLegal(
        CombatSimulationState state,
        CombatActionObservation action,
        out string reason);
}

public interface ICombatTrainingSampleSink
{
    void Record(CombatTrainingSample sample);
}

public static class CombatTrainingProtocol
{
    public const string SampleProtocol = "aura.combat-ai.sample.v8";

    public const int FeatureSchemaVersion = 11;

    public static bool IsCompatible(CombatTrainingSample? sample)
    {
        return sample != null
               && string.Equals(
                   sample.ModelProtocol,
                   SampleProtocol,
                   StringComparison.Ordinal)
               && sample.FeatureSchemaVersion == FeatureSchemaVersion
               && sample.Selection != null
               && string.Equals(
                   sample.Selection.Protocol,
                   "aura.combat-ai.selection.v1",
                   StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(
                   sample.Selection.ExecutedCandidateId);
    }
}

public sealed class CombatTrainingSample
{
    public string ModelProtocol { get; set; } = CombatTrainingProtocol.SampleProtocol;

    public int FeatureSchemaVersion { get; set; } =
        CombatTrainingProtocol.FeatureSchemaVersion;

    public string GameBuild { get; set; } = "";

    public string SharedBuild { get; set; } = "";

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string BaseModelId { get; set; } = "";

    public List<string> ActiveAdapterIds { get; set; } = new();

    public long BattleSessionId { get; set; }

    public long DecisionIndex { get; set; }

    public long Sequence { get; set; }

    public long TransactionId { get; set; }

    public string StateFingerprint { get; set; } = "";

    public string NextStateFingerprint { get; set; } = "";

    public string DecisionProfile { get; set; } = "";

    public CombatTrainingSelectionTrace Selection { get; set; } = new();

    public CombatTrainingInteractionTrace? Interaction { get; set; }

    public string PlanSummary { get; set; } = "";

    public List<CombatPlanStep> Plan { get; set; } = new();

    public string SearchAlgorithm { get; set; } = "";

    public int SearchSimulations { get; set; }

    public int SearchNodes { get; set; }

    public int SearchTranspositionHits { get; set; }

    public string SearchBudgetTier { get; set; } = "";

    public CombatDecisionPerformanceTelemetry Performance { get; set; } = new();

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

public sealed class CombatTrainingInteractionTrace
{
    public const string CurrentProtocol = "aura.combat-ai.interaction-trace.v2";

    public string Protocol { get; set; } = CurrentProtocol;

    public long RequestId { get; set; }

    public string ParentActionToken { get; set; } = "";

    public string ParentCandidateId { get; set; } = "";

    public CombatInteractionKind Kind { get; set; }

    public CombatInteractionZone Zone { get; set; }

    public int MinSelections { get; set; }

    public int MaxSelections { get; set; }

    public bool CanConfirmEarly { get; set; }

    public bool EffectsComplete { get; set; }

    public List<string> EligibleCandidateIds { get; set; } = new();

    public List<string> SelectedCandidateIds { get; set; } = new();

    public bool Completed { get; set; }

    public string CompletionReason { get; set; } = "";
}

public sealed class CombatTrainingCandidate
{
    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

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

    public double PlanScore { get; set; }

    public double SearchPrior { get; set; }

    public int SearchVisits { get; set; }

    public double SearchDeathRisk { get; set; }

    public double SearchMeanReturn { get; set; }

    public double SearchReturnStandardError { get; set; }

    public double SearchLowerTailMean { get; set; }

    public List<double> SearchReturnQuantiles { get; set; } = new();
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
