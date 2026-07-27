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

public enum CombatTerminalResolution
{
    None,
    Physical,
    ExplicitRule,
    ResurrectionEscapeOverride
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
    AllAllies,
    AllAlliesExceptSelf,
    AllOpponents,
    RandomEnemy,
    Player,
    EventSource,
    EventTarget
}

public enum CombatSimulationEffectKind
{
    Damage,
    TrueDamage,
    DirectHpLoss,
    GainBlock,
    SetBlock,
    Heal,
    SetHp,
    SetHpToMax,
    Draw,
    DiscardRandom,
    ExhaustRandom,
    GainEnergy,
    SkipTurn,
    DrawToHandLimit,
    CreateRandomCard,
    AddCardTag,
    RetrieveCards,
    EqualizeHealthByStatus,
    ModifyStatusCounter,
    WinBattle,
    EmitEvent,
    AddStatus,
    RemoveStatus,
    CreateCard,
    ChangeCardCost,
    ModifyVariable,
    ModifyVariablePercent,
    ScaleVariablePercent,
    ScaleMaxHpPercent,
    DeferVariableUntilVictory,
    CopyStatuses,
    SummonEnemy,
    Despawn
}

public enum CombatSimulationEventKind
{
    BattleStarted,
    TurnStarted,
    TurnEnded,
    IntentSelected,
    ActionStarted,
    DiceChecked,
    DeckShuffled,
    CardDrawn,
    CardCreated,
    CardPlayed,
    ActionResolved,
    CardDiscarded,
    CardExhausted,
    CardTagChanged,
    DeferredEffectTriggered,
    DamageDealt,
    BlockGained,
    BlockChanged,
    Healed,
    EnergyChanged,
    StatusAdded,
    StatusRemoved,
    ActorDefeated,
    ActorSummoned,
    CardCostChanged,
    VariableChanged,
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

    public CombatSimulationValueExpression? AmountExpression { get; set; }

    public CombatSimulationValueExpression? ConditionExpression { get; set; }

    public CombatSimulationValueRounding Rounding { get; set; } =
        CombatSimulationValueRounding.Round;

    public double Probability { get; set; } = 1d;

    public string RandomChoiceGroup { get; set; } = "";

    public double RandomChoiceWeight { get; set; } = 1d;

    public string DefinitionId { get; set; } = "";

    public string SecondaryDefinitionId { get; set; } = "";

    public string CounterKey { get; set; } = "";

    public string RequiredStatusTag { get; set; } = "";

    public string RequiredCardTag { get; set; } = "";

    public int MinimumRarity { get; set; } = 1;

    public int MaximumRarity { get; set; } = int.MaxValue;

    public int CounterLimit { get; set; } = int.MaxValue;

    public bool RemoveStatusAtCounterLimit { get; set; }

    public CombatSimulationEventKind EmittedEventKind { get; set; }

    public CombatCardZone DestinationZone { get; set; } = CombatCardZone.Hand;

    public CombatCardZone SourceZone { get; set; } = CombatCardZone.DrawPile;

    public bool UseEventCard { get; set; }

    public bool RandomizeDestination { get; set; }

    public int Duration { get; set; }

    public bool PersistAcrossBattles { get; set; }

    public bool ScaleWithStatusStacks { get; set; }

    public int MinimumVariableValue { get; set; } = int.MinValue;

    public int MaximumVariableValue { get; set; } = int.MaxValue;

    public CombatSimulationEffectDefinition Clone()
    {
        var clone = (CombatSimulationEffectDefinition)MemberwiseClone();
        clone.AmountExpression = AmountExpression?.Clone();
        clone.ConditionExpression = ConditionExpression?.Clone();
        return clone;
    }
}

public enum CombatSimulationValueRounding
{
    Round,
    Truncate,
    Floor,
    Ceiling
}

public enum CombatSimulationValueOperation
{
    Constant,
    SourceVariable,
    TargetVariable,
    SourceStatusStacks,
    TargetStatusStacks,
    SourceStatusCounter,
    SourceStatusTagStacks,
    SourceHandCount,
    SourceHandTagCount,
    PlayerHandCount,
    SourceHp,
    TargetHp,
    SourceMaxHp,
    TargetMaxHp,
    SourceBlock,
    TargetBlock,
    LivingEnemyCount,
    Add,
    Subtract,
    Multiply,
    Divide,
    Minimum,
    Maximum,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
    Conditional,
    Floor,
    Ceiling
}

public sealed class CombatSimulationValueExpression
{
    public CombatSimulationValueOperation Operation { get; set; }

    public double Constant { get; set; }

    public string Key { get; set; } = "";

    public List<CombatSimulationValueExpression> Arguments { get; set; } = new();

    public CombatSimulationValueExpression Clone()
    {
        return new CombatSimulationValueExpression
        {
            Operation = Operation,
            Constant = Constant,
            Key = Key,
            Arguments = Arguments.Select(item => item.Clone()).ToList()
        };
    }
}

public sealed class CombatCardDefinition
{
    public string OwnerModId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Cost { get; set; }

    public int Rarity { get; set; } = 1;

    public bool Exhaust { get; set; }

    public List<string> Tags { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool RequiresEnemyTarget { get; set; }

    public CombatRuleFidelity Fidelity { get; set; } = CombatRuleFidelity.Authoritative;

    public List<CombatSimulationEffectDefinition> Effects { get; set; } = new();

    public List<CombatSimulationEffectDefinition> DrawEffects { get; set; } = new();

    public List<CombatSimulationEffectDefinition> DiscardEffects { get; set; } = new();

    public CombatCardDefinition Clone()
    {
        return new CombatCardDefinition
        {
            OwnerModId = OwnerModId,
            CardId = CardId,
            DisplayName = DisplayName,
            Cost = Cost,
            Rarity = Rarity,
            Exhaust = Exhaust,
            Tags = new List<string>(Tags),
            Metadata = new Dictionary<string, string>(
                Metadata,
                StringComparer.OrdinalIgnoreCase),
            RequiresEnemyTarget = RequiresEnemyTarget,
            Fidelity = Fidelity,
            Effects = Effects.Select(effect => effect.Clone()).ToList(),
            DrawEffects = DrawEffects.Select(effect => effect.Clone()).ToList(),
            DiscardEffects = DiscardEffects.Select(effect => effect.Clone()).ToList()
        };
    }
}

public sealed class CombatEnemyIntentDefinition
{
    public string IntentId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Weight { get; set; } = 1;

    public int Priority { get; set; }

    public int CooldownTurns { get; set; }

    public CombatSimulationValueExpression? CooldownExpression { get; set; }

    public CombatSimulationValueExpression? PriorityExpression { get; set; }

    public CombatSimulationValueExpression? AvailabilityExpression { get; set; }

    public int MinimumTurn { get; set; } = 1;

    public int MaximumTurn { get; set; } = int.MaxValue;

    public double MinimumHpRatio { get; set; }

    public double MaximumHpRatio { get; set; } = 1d;

    public bool PreventConsecutiveUse { get; set; }

    public List<string> Tags { get; set; } = new();

    public List<CombatSimulationEffectDefinition> Effects { get; set; } = new();

    public CombatEnemyIntentDefinition Clone()
    {
        return new CombatEnemyIntentDefinition
        {
            IntentId = IntentId,
            DisplayName = DisplayName,
            Weight = Weight,
            Priority = Priority,
            CooldownTurns = CooldownTurns,
            CooldownExpression = CooldownExpression?.Clone(),
            PriorityExpression = PriorityExpression?.Clone(),
            AvailabilityExpression = AvailabilityExpression?.Clone(),
            MinimumTurn = MinimumTurn,
            MaximumTurn = MaximumTurn,
            MinimumHpRatio = MinimumHpRatio,
            MaximumHpRatio = MaximumHpRatio,
            PreventConsecutiveUse = PreventConsecutiveUse,
            Tags = new List<string>(Tags),
            Effects = Effects.Select(effect => effect.Clone()).ToList()
        };
    }
}

public enum CombatStatusTriggerOwnerRelation
{
    Any,
    EventSource,
    EventTarget,
    EventTargetAllyExceptSelf
}

public enum CombatStatusCounterIncrementMode
{
    None,
    Fixed,
    EventAmount,
    HandCount,
    HandTagCount,
    StatusTagStacks
}

public sealed class CombatEnemyDefinition
{
    public string OwnerModId { get; set; } = "";

    public string EnemyId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int MaxHp { get; set; }

    public int InitialBlock { get; set; }

    public int ActionCount { get; set; } = 1;

    public CombatRuleFidelity Fidelity { get; set; } = CombatRuleFidelity.Authoritative;

    public Dictionary<string, double> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<CombatInitialStatus> InitialStatuses { get; set; } = new();

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
            ActionCount = ActionCount,
            Fidelity = Fidelity,
            Variables = new Dictionary<string, double>(
                Variables,
                StringComparer.OrdinalIgnoreCase),
            InitialStatuses = InitialStatuses.Select(status => status.Clone()).ToList(),
            Intents = Intents.Select(intent => intent.Clone()).ToList()
        };
    }
}

public sealed class CombatStatusTriggerDefinition
{
    public string TriggerId { get; set; } = "";

    public CombatSimulationEventKind EventKind { get; set; }

    public int Priority { get; set; }

    public int ConsumeStacks { get; set; }

    public CombatStatusTriggerOwnerRelation OwnerRelation { get; set; }

    public int MinimumStacks { get; set; }

    public int MaximumStacks { get; set; } = int.MaxValue;

    public int EveryNthEvent { get; set; } = 1;

    public int MinimumEventAmount { get; set; } = int.MinValue;

    public string RequiredActionTag { get; set; } = "";

    public string ForbiddenActionTag { get; set; } = "";

    public string RequiredDefinitionId { get; set; } = "";

    public string RequiredEventMessage { get; set; } = "";

    public CombatSimulationValueExpression? ConditionExpression { get; set; }

    public string CounterKey { get; set; } = "";

    public CombatStatusCounterIncrementMode CounterIncrementMode { get; set; }

    public int CounterIncrement { get; set; } = 1;

    public string CounterFilter { get; set; } = "";

    public int MinimumCounterValue { get; set; } = int.MinValue;

    public int MaximumCounterValue { get; set; } = int.MaxValue;

    public int CounterStep { get; set; }

    public int CounterStepOrigin { get; set; }

    public bool ResetCounterAfterTrigger { get; set; }

    public bool RemoveStatusAfterTrigger { get; set; }

    public List<CombatSimulationEffectDefinition> Effects { get; set; } = new();

    public CombatStatusTriggerDefinition Clone()
    {
        return new CombatStatusTriggerDefinition
        {
            TriggerId = TriggerId,
            EventKind = EventKind,
            Priority = Priority,
            ConsumeStacks = ConsumeStacks,
            OwnerRelation = OwnerRelation,
            MinimumStacks = MinimumStacks,
            MaximumStacks = MaximumStacks,
            EveryNthEvent = EveryNthEvent,
            MinimumEventAmount = MinimumEventAmount,
            RequiredActionTag = RequiredActionTag,
            ForbiddenActionTag = ForbiddenActionTag,
            RequiredDefinitionId = RequiredDefinitionId,
            RequiredEventMessage = RequiredEventMessage,
            ConditionExpression = ConditionExpression?.Clone(),
            CounterKey = CounterKey,
            CounterIncrementMode = CounterIncrementMode,
            CounterIncrement = CounterIncrement,
            CounterFilter = CounterFilter,
            MinimumCounterValue = MinimumCounterValue,
            MaximumCounterValue = MaximumCounterValue,
            CounterStep = CounterStep,
            CounterStepOrigin = CounterStepOrigin,
            ResetCounterAfterTrigger = ResetCounterAfterTrigger,
            RemoveStatusAfterTrigger = RemoveStatusAfterTrigger,
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

    public int ReducePerTurn { get; set; }

    public int ReducePerUse { get; set; }

    public int ReducePerAttacked { get; set; }

    public bool CanRemainAtZero { get; set; }

    public int MaximumStacks { get; set; } = int.MaxValue;

    public List<string> Tags { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> DynamicModifiersPerStack { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

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
            ReducePerTurn = ReducePerTurn,
            ReducePerUse = ReducePerUse,
            ReducePerAttacked = ReducePerAttacked,
            CanRemainAtZero = CanRemainAtZero,
            MaximumStacks = MaximumStacks,
            Tags = new List<string>(Tags),
            Metadata = new Dictionary<string, string>(
                Metadata,
                StringComparer.OrdinalIgnoreCase),
            DynamicModifiersPerStack = new Dictionary<string, double>(
                DynamicModifiersPerStack,
                StringComparer.OrdinalIgnoreCase),
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

    public Dictionary<string, double> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatEnemySetup
{
    public string EnemyId { get; set; } = "";

    public string InstanceKey { get; set; } = "";

    public double HpScale { get; set; } = 1d;

    public double AttackScale { get; set; } = 1d;

    public int InitialBlockBonus { get; set; }

    public List<CombatInitialStatus> InitialStatuses { get; set; } = new();

    public Dictionary<string, double> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatInitialStatus
{
    public string StatusId { get; set; } = "";

    public int Stacks { get; set; } = 1;

    public int Duration { get; set; }

    public CombatSimulationValueExpression? StacksExpression { get; set; }

    public CombatSimulationValueExpression? ConditionExpression { get; set; }

    public CombatInitialStatus Clone()
    {
        return new CombatInitialStatus
        {
            StatusId = StatusId,
            Stacks = Stacks,
            Duration = Duration,
            StacksExpression = StacksExpression?.Clone(),
            ConditionExpression = ConditionExpression?.Clone()
        };
    }
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

    public bool RetainBlockBetweenTurns { get; set; } = true;

    public bool MovePlayedCardAfterResolution { get; set; }

    public List<string> InitialDiscardCards { get; set; } = new();

    public int DirectHpLossAfterPlayerCard { get; set; }

    public List<CombatScenarioRewardRule> RewardRules { get; set; } = new();

    public List<CombatScenarioRewardCatalogEntry> RewardCatalog { get; set; } =
        new();

    public Dictionary<string, string> CampaignVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

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

    public string ApparentCardId { get; set; } = "";

    public int CostModifier { get; set; }

    public List<string> Tags { get; set; } = new();

    public List<string> EnchantmentIds { get; set; } = new();

    public Dictionary<string, string> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsVisibleFake =>
        Variables.TryGetValue("IsFake", out var value)
        && bool.TryParse(value, out var parsed)
        && parsed;

    public CombatCardInstanceState Clone()
    {
        var clone = (CombatCardInstanceState)MemberwiseClone();
        clone.Tags = new List<string>(Tags);
        clone.EnchantmentIds = new List<string>(EnchantmentIds);
        clone.Variables = new Dictionary<string, string>(
            Variables,
            StringComparer.OrdinalIgnoreCase);
        return clone;
    }
}

public sealed class CombatStatusState
{
    public string StatusId { get; set; } = "";

    public int Stacks { get; set; }

    public int Duration { get; set; }

    public int SourceActorId { get; set; }

    public Dictionary<string, int> TriggerCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatStatusState Clone()
    {
        var clone = (CombatStatusState)MemberwiseClone();
        clone.TriggerCounts = new Dictionary<string, int>(
            TriggerCounts,
            StringComparer.OrdinalIgnoreCase);
        return clone;
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

    public List<string> CurrentIntentIds { get; set; } = new();

    public List<string> PreviousIntentIds { get; set; } = new();

    public Dictionary<string, double> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> IntentCooldowns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

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
            CurrentIntentIds = new List<string>(CurrentIntentIds),
            PreviousIntentIds = new List<string>(PreviousIntentIds),
            Variables = new Dictionary<string, double>(Variables, StringComparer.OrdinalIgnoreCase),
            IntentCooldowns = new Dictionary<string, int>(
                IntentCooldowns,
                StringComparer.OrdinalIgnoreCase),
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

public sealed class CombatDeferredVariableChangeState
{
    public int ActorId { get; set; }

    public string DefinitionId { get; set; } = "";

    public int Amount { get; set; }

    public bool PersistAcrossBattles { get; set; }

    public int MinimumVariableValue { get; set; } = int.MinValue;

    public int MaximumVariableValue { get; set; } = int.MaxValue;

    public CombatDeferredVariableChangeState Clone()
    {
        return (CombatDeferredVariableChangeState)MemberwiseClone();
    }
}

public sealed class CombatDeferredEffectState
{
    public int Sequence { get; set; }

    public int ActorId { get; set; }

    public string StatusId { get; set; } = "";

    public string SourceCardId { get; set; } = "";

    public int SourceCardInstanceId { get; set; }

    public int TargetActorId { get; set; }

    public CombatDeferredEffectState Clone()
    {
        return (CombatDeferredEffectState)MemberwiseClone();
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

    public List<CombatDeferredVariableChangeState> DeferredVictoryVariableChanges
    {
        get;
        set;
    } = new();

    public List<CombatDeferredEffectState> DeferredEffects { get; set; } = new();

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
            DeferredVictoryVariableChanges = DeferredVictoryVariableChanges
                .Select(item => item.Clone())
                .ToList(),
            DeferredEffects = DeferredEffects
                .Select(item => item.Clone())
                .ToList(),
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

/// <summary>
/// Marks a policy that only reads <see cref="CombatSimulationPolicyContext.State"/>
/// during the synchronous SelectAction call. The simulation engine may lend its
/// live state to these trusted policies instead of allocating a defensive deep
/// clone for every decision.
/// </summary>
public interface ICombatSimulationBorrowedStatePolicy : ICombatSimulationPolicy
{
}

public sealed class CombatSimulationPolicyDecisionMetrics
{
    public int SearchSimulations { get; set; }

    public int SearchNodes { get; set; }

    public bool SearchStoppedEarly { get; set; }

    public string SearchBudgetTier { get; set; } = "";

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }
}

public interface ICombatSimulationPolicyMetricsProvider
{
    CombatSimulationPolicyDecisionMetrics LastDecisionMetrics { get; }
}

public sealed class CombatSimulationEvent
{
    public long Sequence { get; set; }

    public long ParentSequence { get; set; }

    public long CausalChainId { get; set; }

    public string HandlerId { get; set; } = "";

    public string SourceRewardId { get; set; } = "";

    public long SourceActionId { get; set; }

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

    public int PolicyDecisions { get; set; }

    public long SearchSimulations { get; set; }

    public long SearchNodes { get; set; }

    public int SearchEarlyStops { get; set; }

    public Dictionary<string, int> SearchBudgetTierCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int ForcedEndTurns { get; set; }

    public int RuleTerminalOverrides { get; set; }

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }

    public Dictionary<string, int> CardPlayCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatSimulationFailureDiagnostics
{
    public string LimitScope { get; set; } = "";

    public int Turn { get; set; }

    public long ActionSequence { get; set; }

    public long TotalCommandCount { get; set; }

    public int ActionCommandCount { get; set; }

    public string ActionDefinitionId { get; set; } = "";

    public string PendingCommand { get; set; } = "";

    public long CausalChainId { get; set; }

    public string HandlerId { get; set; } = "";

    public string SourceRewardId { get; set; } = "";

    public long SourceActionId { get; set; }

    public string TerminalOutcome { get; set; } = "";

    public string TerminalResolution { get; set; } = "";

    public List<string> RecentCommands { get; set; } = new();

    public List<string> RecentEvents { get; set; } = new();

    public List<string> StateSummary { get; set; } = new();

    public CombatSimulationFailureDiagnostics Clone()
    {
        return new CombatSimulationFailureDiagnostics
        {
            LimitScope = LimitScope,
            Turn = Turn,
            ActionSequence = ActionSequence,
            TotalCommandCount = TotalCommandCount,
            ActionCommandCount = ActionCommandCount,
            ActionDefinitionId = ActionDefinitionId,
            PendingCommand = PendingCommand,
            CausalChainId = CausalChainId,
            HandlerId = HandlerId,
            SourceRewardId = SourceRewardId,
            SourceActionId = SourceActionId,
            TerminalOutcome = TerminalOutcome,
            TerminalResolution = TerminalResolution,
            RecentCommands = new List<string>(RecentCommands),
            RecentEvents = new List<string>(RecentEvents),
            StateSummary = new List<string>(StateSummary)
        };
    }
}

public sealed class CombatSimulationResult
{
    public string ScenarioId { get; set; } = "";

    public ulong Seed { get; set; }

    public string RulesetHash { get; set; } = "";

    public string PolicyId { get; set; } = "";

    public CombatSimulationOutcome Outcome { get; set; }

    public CombatTerminationReason TerminationReason { get; set; }

    public bool TerminalConsistencyValid { get; set; } = true;

    public string TerminalConsistencyReason { get; set; } = "";

    public bool ExplicitRuleTermination { get; set; }

    public CombatTerminalResolution TerminalResolution { get; set; }

    public CombatSimulationOutcome InitialTerminalOutcome { get; set; }

    public CombatTerminationReason InitialTerminationReason { get; set; }

    public int InitialTerminalPlayerHp { get; set; }

    public int InitialTerminalLivingEnemyCount { get; set; }

    public int TerminalPlayerHp { get; set; }

    public int TerminalLivingEnemyCount { get; set; }

    public int Turns { get; set; }

    public int FinalPlayerHp { get; set; }

    public string FinalStateHash { get; set; } = "";

    public double SemanticCoverage { get; set; }

    public List<string> UnsupportedDefinitions { get; set; } = new();

    public CombatSimulationMetrics Metrics { get; set; } = new();

    public CombatSimulationFailureDiagnostics FailureDiagnostics { get; set; } =
        new();

    public Dictionary<string, int> PersistentVariableDeltas { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> RewardVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> CampaignVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<CombatSimulationRewardMutation> RewardMutations { get; set; } =
        new();

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

    public string SecondaryDefinitionId { get; set; } = "";

    public string CounterKey { get; set; } = "";

    public string RequiredStatusTag { get; set; } = "";

    public string RequiredCardTag { get; set; } = "";

    public int MinimumRarity { get; set; } = 1;

    public int MaximumRarity { get; set; } = int.MaxValue;

    public int CounterLimit { get; set; } = int.MaxValue;

    public bool RemoveStatusAtCounterLimit { get; set; }

    public CombatSimulationEventKind EmittedEventKind { get; set; }

    public CombatCardZone DestinationZone { get; set; } = CombatCardZone.Hand;

    public CombatCardZone SourceZone { get; set; } = CombatCardZone.DrawPile;

    public bool UseEventCard { get; set; }

    public bool RandomizeDestination { get; set; }

    public int Duration { get; set; }

    public bool PersistAcrossBattles { get; set; }

    public int MinimumVariableValue { get; set; } = int.MinValue;

    public int MaximumVariableValue { get; set; } = int.MaxValue;

    public long ParentSequence { get; set; }

    public long CausalChainId { get; set; }

    public string HandlerId { get; set; } = "";

    public string SourceRewardId { get; set; } = "";

    public long SourceActionId { get; set; }

    public int TriggerWave { get; set; }

    public string RandomStreamId { get; set; } = "";

    public ulong RandomCounter { get; set; }

    public ulong RandomValue { get; set; }
}
