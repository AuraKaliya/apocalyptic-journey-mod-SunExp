using System;
using System.Collections.Generic;

namespace AuraCombatAi.Shared;

public sealed class CombatSimulationStateArena
{
    private readonly List<Slot> slots = new();
    private int cursor;

    public int Capacity => slots.Count;

    public int ReusedStates { get; private set; }

    internal void BeginSearch()
    {
        cursor = 0;
        ReusedStates = 0;
    }

    internal CombatSimulationState Clone(
        CombatSimulationState source,
        bool cloneCardPiles,
        bool cloneFeatures,
        bool cloneThreats)
    {
        Slot slot;
        if (cursor < slots.Count)
        {
            slot = slots[cursor];
            ReusedStates++;
        }
        else
        {
            slot = new Slot();
            slots.Add(slot);
        }
        cursor++;
        return slot.CopyFrom(
            source,
            cloneCardPiles,
            cloneFeatures,
            cloneThreats);
    }

    private sealed class Slot
    {
        private readonly CombatSimulationState state = new();
        private readonly List<double> handValues = new();
        private readonly List<double> retainedValues = new();
        private readonly List<double> drawValues = new();
        private readonly List<double> discardValues = new();
        private readonly List<double> exhaustValues = new();
        private readonly List<string> handIds = new();
        private readonly List<string> retainedIds = new();
        private readonly List<string> drawIds = new();
        private readonly List<string> discardIds = new();
        private readonly List<string> exhaustIds = new();
        private readonly List<CombatDeferredEffectSimulation> deferred = new();
        private readonly Dictionary<string, double> features =
            new(StringComparer.OrdinalIgnoreCase);
        private CombatSimulationUnit[] enemies =
            Array.Empty<CombatSimulationUnit>();
        private CombatSimulationThreat[] threats =
            Array.Empty<CombatSimulationThreat>();
        private ulong[] usedWords = Array.Empty<ulong>();
        private int[] usedCounts = Array.Empty<int>();

        public CombatSimulationState CopyFrom(
            CombatSimulationState source,
            bool cloneCardPiles,
            bool cloneFeatures,
            bool cloneThreats)
        {
            state.PlayerRuntimeId = source.PlayerRuntimeId;
            state.PlayerHp = source.PlayerHp;
            state.PlayerMaxHp = source.PlayerMaxHp;
            state.PlayerDefend = source.PlayerDefend;
            state.Power = source.Power;
            state.MaxPower = source.MaxPower;
            state.HandCount = source.HandCount;
            state.HandLimit = source.HandLimit;
            state.CostReduction = source.CostReduction;
            state.CardCostMultiplier = source.CardCostMultiplier;
            state.KnownCardTags = source.KnownCardTags;
            state.KnownCardSemantics = source.KnownCardSemantics;
            state.DrawPileKnown = source.DrawPileKnown;
            state.DrawnCardPotential = source.DrawnCardPotential;
            state.SetupValue = source.SetupValue;
            state.PersistentValue = source.PersistentValue;
            state.DamageMultiplier = source.DamageMultiplier;
            state.Uncertainty = source.Uncertainty;
            state.StepCount = source.StepCount;
            state.Turn = source.Turn;
            state.TurnActionsTaken = source.TurnActionsTaken;
            state.TurnEnergySpent = source.TurnEnergySpent;
            state.EnemyHpAtTurnStart = source.EnemyHpAtTurnStart;
            state.ConsecutiveNoProgressTurns =
                source.ConsecutiveNoProgressTurns;
            state.NoEffectActionAttemptsThisTurn =
                source.NoEffectActionAttemptsThisTurn;
            state.DeterminizationSeed = source.DeterminizationSeed;
            state.ShuffleEpoch = source.ShuffleEpoch;

            state.HandCardValues = cloneCardPiles
                ? Copy(source.HandCardValues, handValues)
                : source.HandCardValues;
            state.RetainedHandCardValues = cloneCardPiles
                ? Copy(source.RetainedHandCardValues, retainedValues)
                : source.RetainedHandCardValues;
            state.DrawPileValues = cloneCardPiles
                ? Copy(source.DrawPileValues, drawValues)
                : source.DrawPileValues;
            state.DiscardPileValues = cloneCardPiles
                ? Copy(source.DiscardPileValues, discardValues)
                : source.DiscardPileValues;
            state.ExhaustPileValues = cloneCardPiles
                ? Copy(source.ExhaustPileValues, exhaustValues)
                : source.ExhaustPileValues;
            state.HandCardIds = cloneCardPiles
                ? Copy(source.HandCardIds, handIds)
                : source.HandCardIds;
            state.RetainedHandCardIds = cloneCardPiles
                ? Copy(source.RetainedHandCardIds, retainedIds)
                : source.RetainedHandCardIds;
            state.DrawPileCardIds = cloneCardPiles
                ? Copy(source.DrawPileCardIds, drawIds)
                : source.DrawPileCardIds;
            state.DiscardPileCardIds = cloneCardPiles
                ? Copy(source.DiscardPileCardIds, discardIds)
                : source.DiscardPileCardIds;
            state.ExhaustPileCardIds = cloneCardPiles
                ? Copy(source.ExhaustPileCardIds, exhaustIds)
                : source.ExhaustPileCardIds;
            CopyDeferred(source.DeferredEffects);
            state.DeferredEffects = deferred;

            if (cloneFeatures)
            {
                features.Clear();
                foreach (var pair in source.Features)
                {
                    features[pair.Key] = pair.Value;
                }
                state.Features = features;
            }
            else
            {
                state.Features = source.Features;
            }

            CopyEnemies(source.Enemies);
            state.Enemies = enemies;
            if (cloneThreats)
            {
                CopyThreats(source.Threats);
                state.Threats = threats;
            }
            else
            {
                state.Threats = source.Threats;
            }

            if (usedWords.Length != source.UsedActionWords.Length)
            {
                usedWords = new ulong[source.UsedActionWords.Length];
            }
            Array.Copy(
                source.UsedActionWords,
                usedWords,
                source.UsedActionWords.Length);
            state.UsedActionWords = usedWords;
            if (usedCounts.Length != source.UsedActionCounts.Length)
            {
                usedCounts = new int[source.UsedActionCounts.Length];
            }
            Array.Copy(
                source.UsedActionCounts,
                usedCounts,
                source.UsedActionCounts.Length);
            state.UsedActionCounts = usedCounts;
            return state;
        }

        private void CopyDeferred(
            IReadOnlyList<CombatDeferredEffectSimulation> source)
        {
            while (deferred.Count < source.Count)
            {
                deferred.Add(new CombatDeferredEffectSimulation());
            }
            if (deferred.Count > source.Count)
            {
                deferred.RemoveRange(source.Count, deferred.Count - source.Count);
            }
            for (var i = 0; i < source.Count; i++)
            {
                var input = source[i];
                var output = deferred[i];
                output.Sequence = input.Sequence;
                output.StatusId = input.StatusId;
                output.SourceId = input.SourceId;
                output.TargetRuntimeId = input.TargetRuntimeId;
                // Semantic descriptors are immutable during forward simulation.
                output.Semantics = input.Semantics;
            }
        }

        private void CopyEnemies(IReadOnlyList<CombatSimulationUnit> source)
        {
            if (enemies.Length != source.Count)
            {
                enemies = new CombatSimulationUnit[source.Count];
                for (var i = 0; i < enemies.Length; i++)
                {
                    enemies[i] = new CombatSimulationUnit();
                }
            }
            for (var i = 0; i < source.Count; i++)
            {
                var input = source[i];
                var output = enemies[i];
                output.RuntimeId = input.RuntimeId;
                output.Hp = input.Hp;
                output.MaxHp = input.MaxHp;
                output.Defend = input.Defend;
                output.Features.Clear();
                foreach (var pair in input.Features)
                {
                    output.Features[pair.Key] = pair.Value;
                }
            }
        }

        private void CopyThreats(IReadOnlyList<CombatSimulationThreat> source)
        {
            if (threats.Length != source.Count)
            {
                threats = new CombatSimulationThreat[source.Count];
                for (var i = 0; i < threats.Length; i++)
                {
                    threats[i] = new CombatSimulationThreat();
                }
            }
            for (var i = 0; i < source.Count; i++)
            {
                var input = source[i];
                var output = threats[i];
                output.SourceRuntimeId = input.SourceRuntimeId;
                output.Probability = input.Probability;
                output.BlockableDamage = input.BlockableDamage;
                output.UnblockableDamage = input.UnblockableDamage;
                output.DamageOverTime = input.DamageOverTime;
            }
        }

        private static List<T> Copy<T>(IEnumerable<T> source, List<T> target)
        {
            target.Clear();
            target.AddRange(source);
            return target;
        }
    }
}
