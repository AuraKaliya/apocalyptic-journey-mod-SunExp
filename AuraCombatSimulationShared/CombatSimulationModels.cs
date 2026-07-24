using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public enum CombatRuleFidelity
{
    Authoritative,
    Approximate,
    Unsupported
}

public enum CombatSimulationPhase
{
    Initialize,
    BattleStart,
    PlayerTurnStart,
    PlayerAction,
    PlayerTurnEnd,
    EnemyIntent,
    EnemyAction,
    RoundEnd,
    Completed
}

public enum CombatSimulationOutcome
{
    None,
    Victory,
    Defeat,
    Draw,
    Invalid
}

public enum CombatTerminationReason
{
    None,
    Victory,
    Defeat,
    MaximumTurns,
    MaximumActions,
    MaximumCommands,
    MaximumSummonedActors,
    TriggerLoop,
    UnsupportedRule,
    InvalidScenario,
    IllegalPolicyAction,
    Cancelled,
    EngineError
}

public enum CombatSimulationTraceLevel
{
    Summary,
    Actions,
    Full
}

public enum CombatSimulationActorKind
{
    Player,
    Friendly,
    Enemy
}

public enum CombatSimulationActionKind
{
    PlayCard,
    EndTurn
}

public enum CombatSimulationTarget
{
    None,
    Self,
    SelectedEnemy,
    AllEnemies,
    RandomEnemy,
    Player,
    EventSource,
    EventTarget
}

public enum CombatSimulationEffectKind
{
    Damage,
    TrueDamage,
    GainBlock,
    Heal,
    Draw,
    DiscardRandom,
    ExhaustRandom,
    GainEnergy,
    AddStatus,
    RemoveStatus,
    CreateCard,
    ChangeCardCost,
    SummonEnemy,
    Despawn
}

public enum CombatSimulationEventKind
{
    BattleStarted,
    TurnStarted,
    TurnEnded,
    IntentSelected,
    CardDrawn,
    CardPlayed,
    CardDiscarded,
    CardExhausted,
    DamageDealt,
    BlockGained,
    Healed,
    EnergyChanged,
    StatusAdded,
    StatusRemoved,
    ActorDefeated,
    ActorSummoned,
    CardCostChanged,
    RandomResolved,
    BattleEnded,
    RuleRejected
}

public enum CombatCardZone
{
    DrawPile,
    Hand,
    DiscardPile,
    ExhaustPile
}

public sealed class CombatSimulationEffectDefinition
{
    public CombatSimulationEffectKind Kind { get; set; }

    public CombatSimulationTarget Target { get; set; }

    public int Amount { get; set; }

    public double Probability { get; set; } = 1d;

    public string DefinitionId { get; set; } = "";

    public int Duration { get; set; }

    public bool ScaleWithStatusStacks { get; set; }

    public CombatSimulationEffectDefinition Clone()
    {
        return (CombatSimulationEffectDefinition)MemberwiseClone();
    }
}

public sealed class CombatCardDefinition
{
    public string OwnerModId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Cost { get; set; }

    public bool Exhaust { get; set; }

    public bool RequiresEnemyTarget { get; set; }

    public CombatRuleFidelity Fidelity { get; set; } = CombatRuleFidelity.Authoritative;

    public List<CombatSimulationEffectDefinition> Effects { get; set; } = new();

    public CombatCardDefinition Clone()
    {
        return new CombatCardDefinition
        {
            OwnerModId = OwnerModId,
            CardId = CardId,
            DisplayName = DisplayName,
            Cost = Cost,
            Exhaust = Exhaust,
            RequiresEnemyTarget = RequiresEnemyTarget,
            Fidelity = Fidelity,
            Effects = Effects.Select(effect => effect.Clone()).ToList()
        };
    }
}

public sealed class CombatEnemyIntentDefinition
{
    public string IntentId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Weight { get; set; } = 1;

    public int MinimumTurn { get; set; } = 1;

    public int MaximumTurn { get; set; } = int.MaxValue;

    public double MinimumHpRatio { get; set; }

    public double MaximumHpRatio { get; set; } = 1d;

    public bool PreventConsecutiveUse { get; set; }

    public List<CombatSimulationEffectDefinition> Effects { get; set; } = new();

    public CombatEnemyIntentDefinition Clone()
    {
        return new CombatEnemyIntentDefinition
        {
            IntentId = IntentId,
            DisplayName = DisplayName,
            Weight = Weight,
            MinimumTurn = MinimumTurn,
            MaximumTurn = MaximumTurn,
            MinimumHpRatio = MinimumHpRatio,
            MaximumHpRatio = MaximumHpRatio,
            PreventConsecutiveUse = PreventConsecutiveUse,
            Effects = Effects.Select(effect => effect.Clone()).ToList()
        };
    }
}

public sealed class CombatEnemyDefinition
{
    public string OwnerModId { get; set; } = "";

    public string EnemyId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int MaxHp { get; set; }

    public int InitialBlock { get; set; }

    public CombatRuleFidelity Fidelity { get; set; } = CombatRuleFidelity.Authoritative;

    public List<CombatEnemyIntentDefinition> Intents { get; set; } = new();

    public CombatEnemyDefinition Clone()
    {
        return new CombatEnemyDefinition
        {
            OwnerModId = OwnerModId,
            EnemyId = EnemyId,
            DisplayName = DisplayName,
            MaxHp = MaxHp,
            InitialBlock = InitialBlock,
            Fidelity = Fidelity,
            Intents = Intents.Select(intent => intent.Clone()).ToList()
        };
    }
}

public sealed class CombatStatusTriggerDefinition
{
    public string TriggerId { get; set; } = "";

    public CombatSimulationEventKind EventKind { get; set; }

    public int Priority { get; set; }

    public List<CombatSimulationEffectDefinition> Effects { get; set; } = new();

    public CombatStatusTriggerDefinition Clone()
    {
        return new CombatStatusTriggerDefinition
        {
            TriggerId = TriggerId,
            EventKind = EventKind,
            Priority = Priority,
            Effects = Effects.Select(effect => effect.Clone()).ToList()
        };
    }
}

public sealed class CombatStatusDefinition
{
    public string OwnerModId { get; set; } = "";

    public string StatusId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public CombatRuleFidelity Fidelity { get; set; } = CombatRuleFidelity.Authoritative;

    public bool DecayAtRoundEnd { get; set; } = true;

    public List<CombatStatusTriggerDefinition> Triggers { get; set; } = new();

    public CombatStatusDefinition Clone()
    {
        return new CombatStatusDefinition
        {
            OwnerModId = OwnerModId,
            StatusId = StatusId,
            DisplayName = DisplayName,
            Fidelity = Fidelity,
            DecayAtRoundEnd = DecayAtRoundEnd,
            Triggers = Triggers.Select(trigger => trigger.Clone()).ToList()
        };
    }
}

public sealed class CombatPlayerSetup
{
    public string RoleId { get; set; } = "";

    public int MaxHp { get; set; } = 30;

    public int CurrentHp { get; set; } = 30;

    public int BaseEnergy { get; set; } = 3;

    public List<string> Deck { get; set; } = new();

    public List<CombatInitialStatus> InitialStatuses { get; set; } = new();
}

public sealed class CombatEnemySetup
{
    public string EnemyId { get; set; } = "";

    public string InstanceKey { get; set; } = "";

    public double HpScale { get; set; } = 1d;

    public List<CombatInitialStatus> InitialStatuses { get; set; } = new();
}

public sealed class CombatInitialStatus
{
    public string StatusId { get; set; } = "";

    public int Stacks { get; set; } = 1;

    public int Duration { get; set; }
}

public sealed class CombatSimulationLimits
{
    public int MaximumTurns { get; set; } = 100;

    public int MaximumActions { get; set; } = 5000;

    public int MaximumCommands { get; set; } = 50000;

    public int MaximumCommandsPerAction { get; set; } = 1000;

    public int MaximumTriggerWavesPerAction { get; set; } = 100;

    public int MaximumSummonedActors { get; set; } = 32;

    public CombatSimulationLimits Normalize()
    {
        return new CombatSimulationLimits
        {
            MaximumTurns = Math.Max(1, Math.Min(10000, MaximumTurns)),
            MaximumActions = Math.Max(1, Math.Min(1000000, MaximumActions)),
            MaximumCommands = Math.Max(1, Math.Min(10000000, MaximumCommands)),
            MaximumCommandsPerAction = Math.Max(1, Math.Min(100000, MaximumCommandsPerAction)),
            MaximumTriggerWavesPerAction = Math.Max(1, Math.Min(10000, MaximumTriggerWavesPerAction)),
            MaximumSummonedActors = Math.Max(1, Math.Min(1024, MaximumSummonedActors))
        };
    }
}

public sealed class CombatScenarioDefinition
{
    public string ScenarioId { get; set; } = "";

    public string RulesetVersion { get; set; } = "1";

    public ulong Seed { get; set; } = 1UL;

    public CombatPlayerSetup Player { get; set; } = new();

    public List<CombatEnemySetup> Enemies { get; set; } = new();

    public int InitialDraw { get; set; } = 5;

    public int DrawPerTurn { get; set; } = 5;

    public int HandLimit { get; set; } = 10;

    public bool RetainBlockBetweenTurns { get; set; }

    public bool RequireAuthoritativeRules { get; set; } = true;

    public CombatSimulationTraceLevel TraceLevel { get; set; } = CombatSimulationTraceLevel.Actions;

    public CombatSimulationLimits Limits { get; set; } = new();
}

public sealed class CombatRulesetDocument
{
    public string Version { get; set; } = "1";

    public List<CombatCardDefinition> Cards { get; set; } = new();

    public List<CombatEnemyDefinition> Enemies { get; set; } = new();

    public List<CombatStatusDefinition> Statuses { get; set; } = new();
}

public sealed class CombatCardInstanceState
{
    public int InstanceId { get; set; }

    public string CardId { get; set; } = "";

    public int CostModifier { get; set; }

    public CombatCardInstanceState Clone()
    {
        return (CombatCardInstanceState)MemberwiseClone();
    }
}

public sealed class CombatStatusState
{
    public string StatusId { get; set; } = "";

    public int Stacks { get; set; }

    public int Duration { get; set; }

    public int SourceActorId { get; set; }

    public CombatStatusState Clone()
    {
        return (CombatStatusState)MemberwiseClone();
    }
}

public sealed class CombatActorState
{
    public int ActorId { get; set; }

    public string InstanceKey { get; set; } = "";

    public string DefinitionId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public CombatSimulationActorKind Kind { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int Block { get; set; }

    public int Energy { get; set; }

    public int BaseEnergy { get; set; }

    public string CurrentIntentId { get; set; } = "";

    public string PreviousIntentId { get; set; } = "";

    public List<CombatStatusState> Statuses { get; set; } = new();

    public bool Alive => Hp > 0;

    public CombatActorState Clone()
    {
        return new CombatActorState
        {
            ActorId = ActorId,
            InstanceKey = InstanceKey,
            DefinitionId = DefinitionId,
            DisplayName = DisplayName,
            Kind = Kind,
            Hp = Hp,
            MaxHp = MaxHp,
            Block = Block,
            Energy = Energy,
            BaseEnergy = BaseEnergy,
            CurrentIntentId = CurrentIntentId,
            PreviousIntentId = PreviousIntentId,
            Statuses = Statuses.Select(status => status.Clone()).ToList()
        };
    }
}

public sealed class CombatRandomCounterState
{
    public Dictionary<string, ulong> Counters { get; set; } =
        new(StringComparer.Ordinal);

    public CombatRandomCounterState Clone()
    {
        return new CombatRandomCounterState
        {
            Counters = new Dictionary<string, ulong>(Counters, StringComparer.Ordinal)
        };
    }
}

public sealed class CombatBattleState
{
    public int Turn { get; set; }

    public CombatSimulationPhase Phase { get; set; }

    public CombatSimulationOutcome Outcome { get; set; }

    public CombatTerminationReason TerminationReason { get; set; }

    public int PlayerActorId { get; set; }

    public List<CombatActorState> Actors { get; set; } = new();

    public List<CombatCardInstanceState> Cards { get; set; } = new();

    public List<int> DrawPile { get; set; } = new();

    public List<int> Hand { get; set; } = new();

    public List<int> DiscardPile { get; set; } = new();

    public List<int> ExhaustPile { get; set; } = new();

    public CombatRandomCounterState Random { get; set; } = new();

    public long ActionSequence { get; set; }

    public long EventSequence { get; set; }

    public long CommandCount { get; set; }

    public int NextCardInstanceId { get; set; } = 1;

    public int NextActorId { get; set; } = 1;

    public CombatBattleState Clone()
    {
        return new CombatBattleState
        {
            Turn = Turn,
            Phase = Phase,
            Outcome = Outcome,
            TerminationReason = TerminationReason,
            PlayerActorId = PlayerActorId,
            Actors = Actors.Select(actor => actor.Clone()).ToList(),
            Cards = Cards.Select(card => card.Clone()).ToList(),
            DrawPile = new List<int>(DrawPile),
            Hand = new List<int>(Hand),
            DiscardPile = new List<int>(DiscardPile),
            ExhaustPile = new List<int>(ExhaustPile),
            Random = Random.Clone(),
            ActionSequence = ActionSequence,
            EventSequence = EventSequence,
            CommandCount = CommandCount,
            NextCardInstanceId = NextCardInstanceId,
            NextActorId = NextActorId
        };
    }

    public CombatActorState? Player =>
        Actors.FirstOrDefault(actor => actor.ActorId == PlayerActorId);

    public IEnumerable<CombatActorState> LivingEnemies =>
        Actors.Where(actor => actor.Kind == CombatSimulationActorKind.Enemy && actor.Alive);

    public CombatActorState? FindActor(int actorId)
    {
        return Actors.FirstOrDefault(actor => actor.ActorId == actorId);
    }

    public CombatCardInstanceState? FindCard(int instanceId)
    {
        return Cards.FirstOrDefault(card => card.InstanceId == instanceId);
    }
}

public sealed class CombatSimulationAction
{
    public string CandidateId { get; set; } = "";

    public CombatSimulationActionKind Kind { get; set; }

    public int ActorId { get; set; }

    public int CardInstanceId { get; set; }

    public int TargetActorId { get; set; }

    public int Cost { get; set; }

    public string DefinitionId { get; set; } = "";
}

public sealed class CombatSimulationPolicyContext
{
    public CombatScenarioDefinition Scenario { get; set; } = new();

    public CombatRuleset Ruleset { get; set; } = CombatRuleset.Empty;

    public CombatBattleState State { get; set; } = new();

    public IReadOnlyList<CombatSimulationAction> LegalActions { get; set; } =
        Array.Empty<CombatSimulationAction>();
}

public interface ICombatSimulationPolicy
{
    string PolicyId { get; }

    CombatSimulationAction? SelectAction(CombatSimulationPolicyContext context);
}

public sealed class CombatSimulationEvent
{
    public long Sequence { get; set; }

    public long ParentSequence { get; set; }

    public int Turn { get; set; }

    public CombatSimulationPhase Phase { get; set; }

    public CombatSimulationEventKind Kind { get; set; }

    public int SourceActorId { get; set; }

    public int TargetActorId { get; set; }

    public int CardInstanceId { get; set; }

    public string DefinitionId { get; set; } = "";

    public int Amount { get; set; }

    public string BeforeHash { get; set; } = "";

    public string AfterHash { get; set; } = "";

    public string RandomStreamId { get; set; } = "";

    public ulong RandomCounter { get; set; }

    public ulong RandomValue { get; set; }

    public string Message { get; set; } = "";
}

public sealed class CombatTurnSummary
{
    public int Turn { get; set; }

    public int PlayerHpAtStart { get; set; }

    public int PlayerHpAtEnd { get; set; }

    public int EnemyHpAtStart { get; set; }

    public int EnemyHpAtEnd { get; set; }

    public int Actions { get; set; }

    public string StartStateHash { get; set; } = "";

    public string EndStateHash { get; set; } = "";
}

public sealed class CombatSimulationMetrics
{
    public int DamageDealt { get; set; }

    public int DamageTaken { get; set; }

    public int BlockGained { get; set; }

    public int Healing { get; set; }

    public int CardsPlayed { get; set; }

    public int CardsDrawn { get; set; }

    public int EnergySpent { get; set; }

    public Dictionary<string, int> CardPlayCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatSimulationResult
{
    public string ScenarioId { get; set; } = "";

    public ulong Seed { get; set; }

    public string RulesetHash { get; set; } = "";

    public string PolicyId { get; set; } = "";

    public CombatSimulationOutcome Outcome { get; set; }

    public CombatTerminationReason TerminationReason { get; set; }

    public int Turns { get; set; }

    public int FinalPlayerHp { get; set; }

    public string FinalStateHash { get; set; } = "";

    public double SemanticCoverage { get; set; }

    public List<string> UnsupportedDefinitions { get; set; } = new();

    public CombatSimulationMetrics Metrics { get; set; } = new();

    public List<CombatTurnSummary> TurnsSummary { get; set; } = new();

    public List<CombatSimulationEvent> Events { get; set; } = new();

    public CombatBattleState FinalState { get; set; } = new();
}

public sealed class CombatActionApplicationResult
{
    public bool Success { get; set; }

    public string Reason { get; set; } = "";

    public CombatBattleState State { get; set; } = new();

    public List<CombatSimulationEvent> Events { get; set; } = new();
}

internal sealed class CombatSimulationCommand
{
    public CombatSimulationEffectKind Kind { get; set; }

    public int SourceActorId { get; set; }

    public int TargetActorId { get; set; }

    public int CardInstanceId { get; set; }

    public int Amount { get; set; }

    public string DefinitionId { get; set; } = "";

    public int Duration { get; set; }

    public long ParentSequence { get; set; }

    public int TriggerWave { get; set; }

    public string RandomStreamId { get; set; } = "";

    public ulong RandomCounter { get; set; }

    public ulong RandomValue { get; set; }
}
