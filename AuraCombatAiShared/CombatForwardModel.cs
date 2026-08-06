using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public sealed class CombatSimulationState
{
    public int PlayerRuntimeId { get; set; }

    public int PlayerHp { get; set; }

    public int PlayerMaxHp { get; set; }

    public int PlayerDefend { get; set; }

    public int Power { get; set; }

    public int MaxPower { get; set; }

    public int HandCount { get; set; }

    public int HandLimit { get; set; } = 10;

    public int CostReduction { get; set; }

    public double CardCostMultiplier { get; set; } = 1d;

    public Dictionary<string, List<string>> KnownCardTags { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<double> HandCardValues { get; set; } = new();

    public List<double> RetainedHandCardValues { get; set; } = new();

    public List<double> DrawPileValues { get; set; } = new();

    public List<double> DiscardPileValues { get; set; } = new();

    public List<double> ExhaustPileValues { get; set; } = new();

    public List<string> HandCardIds { get; set; } = new();

    public List<string> RetainedHandCardIds { get; set; } = new();

    public List<string> DrawPileCardIds { get; set; } = new();

    public List<string> DiscardPileCardIds { get; set; } = new();

    public List<string> ExhaustPileCardIds { get; set; } = new();

    public List<CombatDeferredEffectSimulation> DeferredEffects { get; set; } = new();

    public Dictionary<string, CombatActionSemantics> KnownCardSemantics { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool DrawPileKnown { get; set; }

    public double DrawnCardPotential { get; set; }

    public double SetupValue { get; set; }

    public double PersistentValue { get; set; }

    public double DamageMultiplier { get; set; } = 1d;

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public double Uncertainty { get; set; }

    public CombatSimulationUnit[] Enemies { get; set; } = Array.Empty<CombatSimulationUnit>();

    public CombatSimulationThreat[] Threats { get; set; } = Array.Empty<CombatSimulationThreat>();

    public ulong[] UsedActionWords { get; set; } = Array.Empty<ulong>();

    public int[] UsedActionCounts { get; set; } = Array.Empty<int>();

    public int StepCount { get; set; }

    public int Turn { get; set; }

    public int TurnActionsTaken { get; set; }

    public int TurnEnergySpent { get; set; }

    public int EnemyHpAtTurnStart { get; set; }

    public int ConsecutiveNoProgressTurns { get; set; }

    public int NoEffectActionAttemptsThisTurn { get; set; }

    public int DeterminizationSeed { get; set; }

    public int ShuffleEpoch { get; set; }

    public bool AllEnemiesDefeated
    {
        get
        {
            for (var index = 0; index < Enemies.Length; index++)
            {
                if (Enemies[index].Hp > 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public CombatSimulationState Clone()
    {
        return CloneForTransition(
            cloneCardPiles: true,
            cloneFeatures: true,
            cloneThreats: true);
    }

    internal CombatSimulationState CloneForTransition(
        bool cloneCardPiles,
        bool cloneFeatures,
        bool cloneThreats = false,
        CombatSimulationStateArena? arena = null)
    {
        if (arena != null)
        {
            return arena.Clone(
                this,
                cloneCardPiles,
                cloneFeatures,
                cloneThreats);
        }
        var enemies = new CombatSimulationUnit[Enemies.Length];
        for (var i = 0; i < enemies.Length; i++)
        {
            enemies[i] = Enemies[i].Clone();
        }

        var threats = Threats;
        if (cloneThreats)
        {
            threats = new CombatSimulationThreat[Threats.Length];
            for (var i = 0; i < threats.Length; i++)
            {
                threats[i] = Threats[i].Clone();
            }
        }

        var deferredEffects =
            new List<CombatDeferredEffectSimulation>(DeferredEffects.Count);
        for (var i = 0; i < DeferredEffects.Count; i++)
        {
            deferredEffects.Add(DeferredEffects[i].Clone());
        }

        return new CombatSimulationState
        {
            PlayerRuntimeId = PlayerRuntimeId,
            PlayerHp = PlayerHp,
            PlayerMaxHp = PlayerMaxHp,
            PlayerDefend = PlayerDefend,
            Power = Power,
            MaxPower = MaxPower,
            HandCount = HandCount,
            HandLimit = HandLimit,
            CostReduction = CostReduction,
            CardCostMultiplier = CardCostMultiplier,
            KnownCardTags = KnownCardTags,
            HandCardValues = cloneCardPiles
                ? new List<double>(HandCardValues)
                : HandCardValues,
            RetainedHandCardValues = cloneCardPiles
                ? new List<double>(RetainedHandCardValues)
                : RetainedHandCardValues,
            DrawPileValues = cloneCardPiles
                ? new List<double>(DrawPileValues)
                : DrawPileValues,
            DiscardPileValues = cloneCardPiles
                ? new List<double>(DiscardPileValues)
                : DiscardPileValues,
            ExhaustPileValues = cloneCardPiles
                ? new List<double>(ExhaustPileValues)
                : ExhaustPileValues,
            HandCardIds = cloneCardPiles
                ? new List<string>(HandCardIds)
                : HandCardIds,
            RetainedHandCardIds = cloneCardPiles
                ? new List<string>(RetainedHandCardIds)
                : RetainedHandCardIds,
            DrawPileCardIds = cloneCardPiles
                ? new List<string>(DrawPileCardIds)
                : DrawPileCardIds,
            DiscardPileCardIds = cloneCardPiles
                ? new List<string>(DiscardPileCardIds)
                : DiscardPileCardIds,
            ExhaustPileCardIds = cloneCardPiles
                ? new List<string>(ExhaustPileCardIds)
                : ExhaustPileCardIds,
            DeferredEffects = deferredEffects,
            // This catalog is immutable after root-state construction.
            KnownCardSemantics = KnownCardSemantics,
            DrawPileKnown = DrawPileKnown,
            DrawnCardPotential = DrawnCardPotential,
            SetupValue = SetupValue,
            PersistentValue = PersistentValue,
            DamageMultiplier = DamageMultiplier,
            Features = cloneFeatures
                ? new Dictionary<string, double>(
                    Features,
                    StringComparer.OrdinalIgnoreCase)
                : Features,
            Uncertainty = Uncertainty,
            Enemies = enemies,
            Threats = threats,
            UsedActionWords = (ulong[])UsedActionWords.Clone(),
            UsedActionCounts = (int[])UsedActionCounts.Clone(),
            StepCount = StepCount,
            Turn = Turn,
            TurnActionsTaken = TurnActionsTaken,
            TurnEnergySpent = TurnEnergySpent,
            EnemyHpAtTurnStart = EnemyHpAtTurnStart,
            ConsecutiveNoProgressTurns = ConsecutiveNoProgressTurns,
            NoEffectActionAttemptsThisTurn =
                NoEffectActionAttemptsThisTurn,
            DeterminizationSeed = DeterminizationSeed,
            ShuffleEpoch = ShuffleEpoch
        };
    }

    internal static CombatActionSemantics CloneSemantics(
        CombatActionSemantics source)
    {
        return CombatPlayerObservationBoundary.NormalizeSemantics(source);
    }

    public bool WasUsed(int actionIndex)
    {
        var word = actionIndex >> 6;
        var bit = actionIndex & 63;
        return word < UsedActionWords.Length && (UsedActionWords[word] & (1UL << bit)) != 0UL;
    }

    public int UseCount(int actionIndex)
    {
        return actionIndex >= 0 && actionIndex < UsedActionCounts.Length
            ? UsedActionCounts[actionIndex]
            : WasUsed(actionIndex) ? 1 : 0;
    }

    public void MarkUsed(int actionIndex)
    {
        var word = actionIndex >> 6;
        var bit = actionIndex & 63;
        UsedActionWords[word] |= 1UL << bit;
        if (actionIndex >= 0 && actionIndex < UsedActionCounts.Length)
        {
            UsedActionCounts[actionIndex]++;
        }
    }

    public void UnmarkUsed(int actionIndex)
    {
        var word = actionIndex >> 6;
        var bit = actionIndex & 63;
        if (actionIndex >= 0 && actionIndex < UsedActionCounts.Length)
        {
            UsedActionCounts[actionIndex] = Math.Max(
                0,
                UsedActionCounts[actionIndex] - 1);
        }
        if (word < UsedActionWords.Length
            && UseCount(actionIndex) == 0)
        {
            UsedActionWords[word] &= ~(1UL << bit);
        }
    }

    public bool TargetAlive(int runtimeId)
    {
        if (runtimeId == 0 || runtimeId == PlayerRuntimeId)
        {
            return true;
        }
        for (var index = 0; index < Enemies.Length; index++)
        {
            if (Enemies[index].RuntimeId == runtimeId
                && Enemies[index].Hp > 0)
            {
                return true;
            }
        }
        return false;
    }

    public double ActiveBlockableThreat(double riskTolerance)
    {
        var expected = 0d;
        var maximum = 0d;
        for (var i = 0; i < Threats.Length; i++)
        {
            var threat = Threats[i];
            if (threat.SourceRuntimeId != 0
                && !IsEnemyAlive(threat.SourceRuntimeId))
            {
                continue;
            }
            expected += Math.Max(0d, threat.BlockableDamage * threat.Probability);
            maximum += Math.Max(0d, threat.BlockableDamage);
        }

        var normalized = Math.Max(0d, Math.Min(1d, riskTolerance));
        return expected + Math.Max(0d, maximum - expected) * normalized;
    }

    public CombatLeafEvaluation EvaluateLeaf(CombatDecisionProfile profile)
    {
        if (AllEnemiesDefeated)
        {
            return new CombatLeafEvaluation
            {
                Value = 100d
                        + 20d * Ratio(PlayerHp, PlayerMaxHp)
                        + Math.Min(20d, PlayerDefend * 0.15d)
                        + Power * 0.15d,
                DeathRisk = 0d
            };
        }

        var blockable = 0d;
        var unblockable = 0d;
        var dot = 0d;
        var attackProbability = 0d;
        for (var i = 0; i < Threats.Length; i++)
        {
            var threat = Threats[i];
            if (threat.SourceRuntimeId != 0
                && !IsEnemyAlive(threat.SourceRuntimeId))
            {
                continue;
            }
            blockable += Math.Max(0d, threat.BlockableDamage * threat.Probability);
            unblockable += Math.Max(0d, threat.UnblockableDamage * threat.Probability);
            dot += Math.Max(0d, threat.DamageOverTime * threat.Probability);
            attackProbability = Math.Max(attackProbability, threat.Probability);
        }

        var hpLoss = Math.Max(0d, blockable - PlayerDefend) + unblockable + dot;
        var hpAfter = PlayerHp - hpLoss;
        var deathRisk = hpAfter <= 0d
            ? Math.Max(0.5d, attackProbability)
            : Math.Max(0d, Math.Min(1d, hpLoss / Math.Max(1d, PlayerHp) - 0.65d));
        var livingMaxHp = 0;
        var livingHp = 0;
        for (var i = 0; i < Enemies.Length; i++)
        {
            livingMaxHp += Math.Max(1, Enemies[i].MaxHp);
            livingHp += Math.Max(0, Enemies[i].Hp);
        }
        var enemyProgress = livingMaxHp <= 0
            ? 1d
            : 1d - Math.Min(1d, (double)livingHp / livingMaxHp);
        var immediateShield = Math.Min(PlayerDefend, blockable);
        var carriedShield = Math.Max(0d, PlayerDefend - blockable);
        var cycleSize = DrawPileValues.Count + DiscardPileValues.Count + HandCardValues.Count;
        var cycleAccess = cycleSize <= 0
            ? 0d
            : Math.Min(1d, (double)Math.Max(1, HandLimit) / cycleSize);
        var handAssetValue = 0d;
        for (var i = 0; i < HandCardValues.Count; i++)
        {
            handAssetValue += Math.Max(0d, HandCardValues[i]);
        }
        var renewableAssetValue = 0d;
        for (var i = 0; i < DrawPileValues.Count; i++)
        {
            renewableAssetValue += Math.Max(0d, DrawPileValues[i]);
        }
        for (var i = 0; i < DiscardPileValues.Count; i++)
        {
            renewableAssetValue += Math.Max(0d, DiscardPileValues[i]);
        }
        var transformDepletionRisk = Features.TryGetValue(
            "postTransformDepletionRisk",
            out var observedTransformDepletionRisk)
            ? observedTransformDepletionRisk
            : 0d;
        var value = Ratio(PlayerHp, PlayerMaxHp) * 40d
                    - hpLoss * 1.8d
                    + enemyProgress * 35d
                    + Power * 0.15d
                    + immediateShield * 0.2d
                    + carriedShield * Math.Max(0d, profile.SurplusDefendRetention) * 0.2d
                    + cycleAccess * 2d
                    + Math.Min(18d, handAssetValue * 0.08d)
                    + Math.Min(12d, renewableAssetValue * 0.04d)
                    + SetupValue * Math.Max(0d, profile.SetupValueWeight)
                    + PersistentValue * Math.Max(0d, profile.PersistentValueWeight)
                    + DrawnCardPotential * 0.2d
                    - ConsecutiveNoProgressTurns * 8d
                    - NoEffectActionAttemptsThisTurn * 40d
                    - transformDepletionRisk * 0.8d
                    - Uncertainty * profile.UncertaintyPenalty;
        return new CombatLeafEvaluation
        {
            Value = value,
            DeathRisk = Math.Max(0d, Math.Min(1d, deathRisk))
        };
    }

    private bool IsEnemyAlive(int runtimeId)
    {
        for (var index = 0; index < Enemies.Length; index++)
        {
            if (Enemies[index].RuntimeId == runtimeId
                && Enemies[index].Hp > 0)
            {
                return true;
            }
        }
        return false;
    }

    public ulong Hash()
    {
        unchecked
        {
            var hash = 1469598103934665603UL;
            Mix(ref hash, PlayerHp);
            Mix(ref hash, PlayerDefend);
            Mix(ref hash, Power);
            Mix(ref hash, HandCount);
            Mix(ref hash, CostReduction);
            Mix(ref hash, Quantize(CardCostMultiplier));
            Mix(ref hash, StepCount);
            Mix(ref hash, Turn);
            Mix(ref hash, TurnActionsTaken);
            Mix(ref hash, TurnEnergySpent);
            Mix(ref hash, EnemyHpAtTurnStart);
            Mix(ref hash, ConsecutiveNoProgressTurns);
            Mix(ref hash, NoEffectActionAttemptsThisTurn);
            Mix(ref hash, ShuffleEpoch);
            Mix(ref hash, Quantize(SetupValue));
            Mix(ref hash, Quantize(PersistentValue));
            Mix(ref hash, Quantize(DamageMultiplier));
            Mix(ref hash, Quantize(DrawnCardPotential));
            Mix(ref hash, DrawPileKnown ? 1 : 0);
            Mix(ref hash, Quantize(Uncertainty));
            for (var i = 0; i < HandCardValues.Count; i++)
            {
                Mix(ref hash, Quantize(HandCardValues[i]));
            }
            for (var i = 0; i < HandCardIds.Count; i++)
            {
                Mix(ref hash, HandCardIds[i]);
            }
            for (var i = 0; i < RetainedHandCardValues.Count; i++)
            {
                Mix(ref hash, Quantize(RetainedHandCardValues[i]));
            }
            for (var i = 0; i < RetainedHandCardIds.Count; i++)
            {
                Mix(ref hash, RetainedHandCardIds[i]);
            }
            for (var i = 0; i < DrawPileValues.Count; i++)
            {
                Mix(ref hash, Quantize(DrawPileValues[i]));
            }
            for (var i = 0; i < DrawPileCardIds.Count; i++)
            {
                Mix(ref hash, DrawPileCardIds[i]);
            }
            for (var i = 0; i < DiscardPileValues.Count; i++)
            {
                Mix(ref hash, Quantize(DiscardPileValues[i]));
            }
            for (var i = 0; i < DiscardPileCardIds.Count; i++)
            {
                Mix(ref hash, DiscardPileCardIds[i]);
            }
            for (var i = 0; i < ExhaustPileValues.Count; i++)
            {
                Mix(ref hash, Quantize(ExhaustPileValues[i]));
            }
            for (var i = 0; i < ExhaustPileCardIds.Count; i++)
            {
                Mix(ref hash, ExhaustPileCardIds[i]);
            }
            for (var i = 0; i < HandCardIds.Count; i++)
            {
                Mix(ref hash, HandCardIds[i]);
            }
            for (var i = 0; i < DrawPileCardIds.Count; i++)
            {
                Mix(ref hash, DrawPileCardIds[i]);
            }
            for (var i = 0; i < DiscardPileCardIds.Count; i++)
            {
                Mix(ref hash, DiscardPileCardIds[i]);
            }
            for (var i = 0; i < DeferredEffects.Count; i++)
            {
                Mix(ref hash, DeferredEffects[i].Sequence);
                Mix(ref hash, DeferredEffects[i].SourceId);
                Mix(ref hash, DeferredEffects[i].TargetRuntimeId);
            }
            MixFeatures(ref hash, Features);
            for (var i = 0; i < Enemies.Length; i++)
            {
                Mix(ref hash, Enemies[i].RuntimeId);
                Mix(ref hash, Enemies[i].Hp);
                Mix(ref hash, Enemies[i].Defend);
                MixFeatures(ref hash, Enemies[i].Features);
            }
            for (var i = 0; i < UsedActionWords.Length; i++)
            {
                hash ^= UsedActionWords[i];
                hash *= 1099511628211UL;
            }
            for (var i = 0; i < UsedActionCounts.Length; i++)
            {
                Mix(ref hash, UsedActionCounts[i]);
            }
            return hash;
        }
    }

    public ulong CycleHash()
    {
        unchecked
        {
            // A cycle is identified by the resources required to repeat it.
            // Monotonic payoffs such as damage, block, setup value, and stacked
            // state are assessed separately by CombatLoopSafetyAnalyzer.
            var hash = 1469598103934665603UL;
            Mix(ref hash, MaxPower);
            Mix(ref hash, HandCount);
            Mix(ref hash, HandLimit);
            Mix(ref hash, CostReduction);
            Mix(ref hash, Quantize(CardCostMultiplier));
            Mix(ref hash, DrawPileKnown ? 1 : 0);
            for (var i = 0; i < HandCardValues.Count; i++)
            {
                Mix(ref hash, Quantize(HandCardValues[i]));
            }
            for (var i = 0; i < RetainedHandCardValues.Count; i++)
            {
                Mix(ref hash, Quantize(RetainedHandCardValues[i]));
            }
            for (var i = 0; i < DrawPileValues.Count; i++)
            {
                Mix(ref hash, Quantize(DrawPileValues[i]));
            }
            for (var i = 0; i < DiscardPileValues.Count; i++)
            {
                Mix(ref hash, Quantize(DiscardPileValues[i]));
            }
            for (var i = 0; i < ExhaustPileValues.Count; i++)
            {
                Mix(ref hash, Quantize(ExhaustPileValues[i]));
            }
            for (var i = 0; i < DeferredEffects.Count; i++)
            {
                Mix(ref hash, DeferredEffects[i].StatusId);
                Mix(ref hash, DeferredEffects[i].SourceId);
                Mix(ref hash, DeferredEffects[i].TargetRuntimeId);
            }
            return hash;
        }
    }

    private static void MixFeatures(
        ref ulong hash,
        IReadOnlyDictionary<string, double> features)
    {
        unchecked
        {
            // Search-state feature maps are logically unordered. Combining
            // two independently avalanched commutative accumulators keeps the
            // hash stable across dictionary implementations without sorting
            // and allocating an OrderedEnumerable for every visited node.
            ulong sum = 0UL;
            ulong xor = 0UL;
            var count = 0;
            foreach (var pair in features)
            {
                var entry = 1469598103934665603UL;
                Mix(ref entry, pair.Key);
                Mix(ref entry, Quantize(pair.Value));
                entry ^= entry >> 30;
                entry *= 0xbf58476d1ce4e5b9UL;
                entry ^= entry >> 27;
                entry *= 0x94d049bb133111ebUL;
                entry ^= entry >> 31;
                sum += entry;
                var shift = (int)(entry & 63UL);
                xor ^= shift == 0
                    ? entry
                    : entry << shift | entry >> (64 - shift);
                count++;
            }
            Mix(ref hash, count);
            Mix(ref hash, sum);
            Mix(ref hash, xor);
        }
    }

    private static void Mix(ref ulong hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 1099511628211UL;
        }
    }

    private static void Mix(ref ulong hash, string? value)
    {
        foreach (var character in value ?? "")
        {
            Mix(ref hash, character);
        }
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
    }

    private static int Quantize(double value)
    {
        var finite = double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(finite * 1000d)));
    }

    private static double Ratio(double value, double maximum)
    {
        return maximum <= 0d
            ? 0d
            : Math.Max(0d, Math.Min(1d, value / maximum));
    }
}

public sealed class CombatDeferredEffectSimulation
{
    public int Sequence { get; set; }

    public string StatusId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public int TargetRuntimeId { get; set; }

    public CombatActionSemantics Semantics { get; set; } = new();

    public CombatDeferredEffectSimulation Clone()
    {
        return new CombatDeferredEffectSimulation
        {
            Sequence = Sequence,
            StatusId = StatusId,
            SourceId = SourceId,
            TargetRuntimeId = TargetRuntimeId,
            Semantics = CombatSimulationState.CloneSemantics(Semantics)
        };
    }
}

public sealed class CombatSimulationUnit
{
    public int RuntimeId { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int Defend { get; set; }

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatSimulationUnit Clone()
    {
        return new CombatSimulationUnit
        {
            RuntimeId = RuntimeId,
            Hp = Hp,
            MaxHp = MaxHp,
            Defend = Defend,
            Features = new Dictionary<string, double>(
                Features,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class CombatSimulationThreat
{
    public int SourceRuntimeId { get; set; }

    public double Probability { get; set; }

    public double BlockableDamage { get; set; }

    public double UnblockableDamage { get; set; }

    public double DamageOverTime { get; set; }

    public CombatSimulationThreat Clone()
    {
        return (CombatSimulationThreat)MemberwiseClone();
    }
}

public struct CombatLeafEvaluation
{
    public double Value { get; set; }

    public double DeathRisk { get; set; }
}

public static class CombatForwardModel
{
    [ThreadStatic]
    private static List<CombatDeferredEffectSimulation>?
        threadDeferredEffectSnapshot;

    [ThreadStatic]
    private static List<double>? threadUnretainedValues;

    [ThreadStatic]
    private static List<string>? threadUnretainedIds;

    public static CombatSimulationState Create(
        CombatStateObservation state,
        int actionCount)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var belief = CombatBeliefTracker.FromObservation(state);
        return Create(
            state,
            actionCount,
            belief,
            CombatPublicObservationHasher.Seed(state, 0));
    }

    public static CombatSimulationState Create(
        CombatStateObservation state,
        int actionCount,
        CombatBeliefState belief,
        int determinizationSeed)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (belief == null) throw new ArgumentNullException(nameof(belief));
        return CreateCore(
            state,
            actionCount,
            CombatRootDeterminizer.SampleDrawPile(
                belief,
                determinizationSeed),
            drawPileKnown: belief.DrawPileCount > 0,
            determinizationSeed);
    }

    internal static void ResetRootDeterminization(
        CombatSimulationState reusableRoot,
        CombatBeliefState belief,
        int determinizationSeed,
        List<string> unknownWorkspace,
        IReadOnlyDictionary<string, double>? knowledgeValues = null)
    {
        if (reusableRoot == null)
        {
            throw new ArgumentNullException(nameof(reusableRoot));
        }
        CombatRootDeterminizer.SampleDrawPileInto(
            belief,
            determinizationSeed,
            reusableRoot.DrawPileCardIds,
            unknownWorkspace);
        reusableRoot.DrawPileValues.Clear();
        for (var i = 0; i < reusableRoot.DrawPileCardIds.Count; i++)
        {
            var cardId = reusableRoot.DrawPileCardIds[i];
            reusableRoot.DrawPileValues.Add(
                knowledgeValues != null
                && knowledgeValues.TryGetValue(cardId, out var cached)
                    ? cached
                    : KnowledgeValue(cardId));
        }
        reusableRoot.DrawPileKnown = belief.DrawPileCount > 0;
        reusableRoot.DeterminizationSeed = determinizationSeed;
        reusableRoot.ShuffleEpoch = 0;
    }

    private static CombatSimulationState CreateCore(
        CombatStateObservation state,
        int actionCount,
        IReadOnlyList<string> sampledDrawPile,
        bool drawPileKnown,
        int determinizationSeed)
    {
        var threats = BuildThreats(state);
        return new CombatSimulationState
        {
            PlayerRuntimeId = state.Player.RuntimeId,
            PlayerHp = state.Player.CurrentHp,
            PlayerMaxHp = state.Player.MaxHp,
            PlayerDefend = state.Player.Defend,
            Power = state.CurrentPower,
            MaxPower = state.MaxPower,
            HandCount = state.HandCount,
            HandLimit = ResolveHandLimit(state),
            CardCostMultiplier = Math.Max(
                0d,
                state.Features.TryGetValue(
                    "cardCostMultiplier",
                    out var cardCostMultiplier)
                    ? cardCostMultiplier
                    : 1d),
            HandCardValues = state.HandCardIds
                .Select(KnowledgeValue)
                .ToList(),
            RetainedHandCardValues = state.RetainedHandCardIds
                .Select(KnowledgeValue)
                .ToList(),
            DrawPileValues = sampledDrawPile
                .Select(KnowledgeValue)
                .ToList(),
            DiscardPileValues = state.DiscardPileCardIds
                .Select(KnowledgeValue)
                .ToList(),
            ExhaustPileValues = state.ExhaustPileCardIds
                .Select(KnowledgeValue)
                .ToList(),
            HandCardIds = new List<string>(state.HandCardIds),
            RetainedHandCardIds = new List<string>(state.RetainedHandCardIds),
            DrawPileCardIds = new List<string>(sampledDrawPile),
            DiscardPileCardIds = new List<string>(state.DiscardPileCardIds),
            ExhaustPileCardIds = new List<string>(state.ExhaustPileCardIds),
            DeferredEffects = (state.DeferredEffects
                               ?? new List<CombatDeferredEffectObservation>())
                .OrderBy(item => item.Sequence)
                .Select(item => new CombatDeferredEffectSimulation
                {
                    Sequence = item.Sequence,
                    StatusId = item.StatusId,
                    SourceId = item.SourceId,
                    TargetRuntimeId = item.TargetRuntimeId,
                    Semantics = CombatSimulationState.CloneSemantics(
                        item.Semantics ?? new CombatActionSemantics())
                })
                .ToList(),
            KnownCardSemantics = (state.Actions
                                  ?? new List<CombatActionObservation>())
                .Where(action => action != null
                                 && !string.IsNullOrWhiteSpace(action.SourceId))
                .GroupBy(action => action.SourceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => CombatSimulationState.CloneSemantics(
                        group.First().Semantics ?? new CombatActionSemantics()),
                    StringComparer.OrdinalIgnoreCase),
            KnownCardTags = state.CardTagsById.ToDictionary(
                pair => pair.Key,
                pair => new List<string>(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            DrawPileKnown = drawPileKnown,
            Features = BuildStateFeatures(state),
            Enemies = state.Enemies.Select(enemy => new CombatSimulationUnit
            {
                RuntimeId = enemy.RuntimeId,
                Hp = enemy.CurrentHp,
                MaxHp = enemy.MaxHp,
                Defend = enemy.Defend,
                Features = BuildEnemyFeatures(enemy)
            }).ToArray(),
            Threats = threats,
            Turn = state.Features.TryGetValue("turn", out var turn)
                ? Math.Max(1, (int)Math.Round(turn))
                : 1,
            TurnActionsTaken = Math.Max(
                0,
                (int)Math.Round(Value(
                    state.Features,
                    CombatTurnFeatureNames.ActionsTakenThisTurn))),
            TurnEnergySpent = Math.Max(
                0,
                (int)Math.Round(Value(
                    state.Features,
                    CombatTurnFeatureNames.EnergySpentThisTurn))),
            EnemyHpAtTurnStart = Math.Max(
                0,
                (int)Math.Round(Value(
                    state.Features,
                    CombatTurnFeatureNames.EnemyHpAtTurnStart))),
            ConsecutiveNoProgressTurns = Math.Max(
                0,
                (int)Math.Round(Value(
                    state.Features,
                    CombatTurnFeatureNames.ConsecutiveNoProgressTurns))),
            NoEffectActionAttemptsThisTurn = Math.Max(
                0,
                (int)Math.Round(Value(
                    state.Features,
                    CombatTurnFeatureNames.NoEffectActionAttemptsThisTurn))),
            DeterminizationSeed = determinizationSeed,
            ShuffleEpoch = 0,
            UsedActionWords = new ulong[(Math.Max(0, actionCount) + 63) / 64],
            UsedActionCounts = new int[Math.Max(0, actionCount)]
        };
    }

    public static CombatActionModel Resolve(
        CombatStateObservation root,
        CombatActionObservation action,
        bool useRegisteredResolvers = true)
    {
        return Resolve(root, action, useRegisteredResolvers, arena: null);
    }

    internal static CombatActionModel Resolve(
        CombatStateObservation root,
        CombatActionObservation action,
        bool useRegisteredResolvers,
        CombatActionModelArena? arena)
    {
        if (useRegisteredResolvers
            && CombatAiRegistry.TryResolveEffects(root, action, out var provided)
            && provided != null
            && provided.Outcomes.Count > 0)
        {
            NormalizeOutcomes(provided);
            return provided;
        }

        var semantics = action.Semantics ?? new CombatActionSemantics();
        var outcome = arena?.RentOutcome() ?? new CombatActionOutcome();
        outcome.OutcomeId = "expected";
        var hasImmediateTargetEffects = false;
        foreach (var effect in semantics.TargetEffects)
        {
            if (effect.Phase != CombatSemanticEffectPhase.Immediate
                || effect.Kind is not (CombatSemanticEffectKind.Damage
                    or CombatSemanticEffectKind.TrueDamage
                    or CombatSemanticEffectKind.DirectHpLoss))
            {
                continue;
            }
            hasImmediateTargetEffects = true;
            var magnitude = Math.Max(
                0d,
                effect.Kind == CombatSemanticEffectKind.Damage
                    ? effect.EffectiveDurabilityAmount
                    : effect.EffectiveAmount);
            Add(
                outcome,
                effect.Kind == CombatSemanticEffectKind.Damage
                    ? CombatEffectKind.Damage
                    : CombatEffectKind.TrueDamage,
                effect.TargetRuntimeId,
                magnitude
                * Math.Max(0d, Math.Min(1d, effect.Probability)),
                arena);
        }
        if (!hasImmediateTargetEffects)
        {
            Add(
                outcome,
                CombatEffectKind.Damage,
                action.TargetRuntimeId,
                semantics.Damage * Math.Max(1d, semantics.HitCount),
                arena);
            Add(
                outcome,
                CombatEffectKind.TrueDamage,
                action.TargetRuntimeId,
                semantics.TrueDamage,
                arena);
        }
        Add(outcome, CombatEffectKind.DamageOverTime, action.TargetRuntimeId, semantics.DamageOverTime, arena);
        Add(outcome, CombatEffectKind.GainDefend, action.TargetRuntimeId, semantics.Defend, arena);
        var hasImmediateHealEffects = false;
        foreach (var effect in semantics.TargetEffects)
        {
            if (effect.Phase != CombatSemanticEffectPhase.Immediate
                || effect.Kind != CombatSemanticEffectKind.Heal)
            {
                continue;
            }
            hasImmediateHealEffects = true;
            Add(
                outcome,
                CombatEffectKind.Heal,
                effect.TargetRuntimeId,
                Math.Max(0d, effect.EffectiveAmount)
                * Math.Max(0d, Math.Min(1d, effect.Probability)),
                arena);
        }
        if (!hasImmediateHealEffects)
        {
            Add(outcome, CombatEffectKind.Heal, action.TargetRuntimeId, semantics.Heal, arena);
        }
        Add(outcome, CombatEffectKind.Draw, 0, semantics.Draw, arena);
        if (!semantics.RestoreEnergyToMaximum
            && !semantics.EnergyMinimum.HasValue
            && !semantics.EnergySetAmount.HasValue)
        {
            Add(outcome, CombatEffectKind.GainEnergy, 0, semantics.EnergyGain, arena);
        }
        if (semantics.RestoreEnergyToMaximum)
        {
            var effect = RentEffect(arena);
            effect.Kind = CombatEffectKind.SetEnergy;
            effect.SemanticId = "maximum";
            outcome.Effects.Add(effect);
        }
        else if (semantics.EnergyMinimum.HasValue)
        {
            var effect = RentEffect(arena);
            effect.Kind = CombatEffectKind.SetEnergy;
            effect.Magnitude = Math.Max(0d, semantics.EnergyMinimum.Value);
            effect.SemanticId = "minimum";
            outcome.Effects.Add(effect);
        }
        else if (semantics.EnergySetAmount.HasValue)
        {
            var effect = RentEffect(arena);
            effect.Kind = CombatEffectKind.SetEnergy;
            effect.Magnitude = Math.Max(0d, semantics.EnergySetAmount.Value);
            effect.SemanticId = "absolute";
            outcome.Effects.Add(effect);
        }
        Add(outcome, CombatEffectKind.ReduceCost, 0, semantics.CostReduction, arena);
        Add(outcome, CombatEffectKind.Buff, action.TargetRuntimeId, semantics.Buff, arena);
        Add(outcome, CombatEffectKind.Debuff, action.TargetRuntimeId, semantics.Debuff, arena);
        Add(outcome, CombatEffectKind.Cleanse, action.TargetRuntimeId, semantics.Cleanse, arena);
        Add(outcome, CombatEffectKind.GenerateCard, 0, semantics.CardGeneration, arena);
        Add(outcome, CombatEffectKind.PersistentValue, 0, semantics.PersistentValue, arena);
        Add(outcome, CombatEffectKind.Scaling, 0, semantics.Scaling, arena);
        Add(outcome, CombatEffectKind.DamageMultiplier, 0, semantics.DamageMultiplierGain, arena);
        foreach (var retrieval in semantics.CardRetrievals)
        {
            AddRetrieval(outcome, retrieval, selectionRank: 0, arena);
        }
        var randomCardCost = HasRandomCardCostStatus(root)
                             && action.Kind == CombatActionKind.PlayCard;
        if (randomCardCost)
        {
            var effect = RentEffect(arena);
            effect.Kind = CombatEffectKind.SetCardCostMultiplier;
            effect.SemanticId = "post-action-random-cost";
            outcome.Effects.Add(effect);
        }
        var retrievalBranchCount = semantics.CardRetrievals.Count == 0
            ? 1
            : Math.Max(
                1,
                Math.Min(
                    3,
                    semantics.CardRetrievals.Max(item =>
                        item.CandidateBranchCount)));
        var branchCount = Math.Max(
            retrievalBranchCount,
            randomCardCost ? 3 : 1);
        var model = arena?.RentModel() ?? new CombatActionModel();
        model.Confidence = Math.Max(
            0d,
            Math.Min(1d, 1d - semantics.Uncertainty / 3d));
        if (branchCount == 1)
        {
            model.Outcomes.Add(outcome);
        }
        else
        {
            for (var rank = 0; rank < branchCount; rank++)
            {
                model.Outcomes.Add(CloneBranchedOutcome(
                    outcome,
                    rank,
                    randomCardCost
                        ? rank == 0 ? 0.34d : 0.33d
                        : 1d / branchCount,
                    arena));
            }
        }
        return model;
    }

    public static CombatSimulationState Apply(
        CombatSimulationState source,
        CombatActionObservation action,
        int actionIndex,
        CombatActionOutcome outcome,
        CombatDecisionProfile profile,
        CombatSimulationStateArena? arena = null)
    {
        var stateChanges = DynamicRebirthStateChanges(source, action);
        var handTransform = action.Semantics?.HandTransform;
        var mutatesCardPiles = action.Kind == CombatActionKind.PlayCard
                               || handTransform != null;
        for (var effectIndex = 0;
             !mutatesCardPiles && effectIndex < outcome.Effects.Count;
             effectIndex++)
        {
            mutatesCardPiles = outcome.Effects[effectIndex].Kind
                is CombatEffectKind.Draw or CombatEffectKind.RetrieveCards;
        }
        var state = source.CloneForTransition(
            mutatesCardPiles,
            stateChanges != null && stateChanges.Count > 0
            || handTransform != null,
            arena: arena);
        var rawCost = RawDynamicCost(state, action);
        var effectiveCost = Math.Max(0, rawCost - state.CostReduction);
        var reductionSpent = Math.Min(rawCost, state.CostReduction);
        state.CostReduction = Math.Max(0, state.CostReduction - reductionSpent);
        state.Power = Math.Max(0, state.Power - effectiveCost);
        state.TurnActionsTaken++;
        state.TurnEnergySpent += effectiveCost;
        var recycle =
            action.Features.TryGetValue("recycle", out var recycleValue)
            && recycleValue > 0d
            || string.Equals(
                action.SourceId,
                "Crowdfundingcard_8",
                StringComparison.OrdinalIgnoreCase)
            && Value(
                source.Features,
                CombatArchetypePolicy.ResurrectionCountFeature) > 0d;
        if (action.Kind == CombatActionKind.PlayCard && !recycle)
        {
            state.HandCount = Math.Max(0, state.HandCount - 1);
            var cardValue = KnowledgeValue(action.SourceId);
            RemoveClosest(state.HandCardValues, cardValue);
            RemoveFirst(state.HandCardIds, action.SourceId);
            if (action.Features.TryGetValue("retain", out var retained)
                && retained > 0d)
            {
                RemoveClosestIfPresent(state.RetainedHandCardValues, cardValue);
                RemoveFirst(state.RetainedHandCardIds, action.SourceId);
            }
            if (action.Features.TryGetValue("exhaustOnUse", out var exhaust)
                && exhaust > 0d)
            {
                state.ExhaustPileValues.Add(cardValue);
                state.ExhaustPileCardIds.Add(action.SourceId);
            }
            else if (action.Features.TryGetValue("ouroboros", out var ouroboros)
                     && ouroboros > 0d)
            {
                state.DrawPileValues.Add(cardValue);
                state.DrawPileCardIds.Add(action.SourceId);
            }
            else
            {
                state.DiscardPileValues.Add(cardValue);
                state.DiscardPileCardIds.Add(action.SourceId);
            }
        }
        if (!recycle)
        {
            state.MarkUsed(actionIndex);
        }
        if (handTransform != null)
        {
            ApplyHandTransform(state, action, handTransform);
        }
        state.StepCount++;

        if (stateChanges != null
            && stateChanges.TryGetValue("playerMaxHp", out var maximumHpDelta)
            && Math.Abs(maximumHpDelta) > 0.000001d)
        {
            state.PlayerMaxHp = Math.Max(
                1,
                state.PlayerMaxHp + (int)Math.Round(maximumHpDelta));
            state.PlayerHp = Math.Min(state.PlayerHp, state.PlayerMaxHp);
        }

        for (var i = 0; i < outcome.Effects.Count; i++)
        {
            if (string.Equals(
                    action.SourceId,
                    "timekeeper_12",
                    StringComparison.OrdinalIgnoreCase)
                && outcome.Effects[i].Kind == CombatEffectKind.Draw)
            {
                continue;
            }
            ApplyEffect(state, outcome.Effects[i], action.TargetRuntimeId);
        }
        if (action.Kind == CombatActionKind.PlayCard
            && action.Semantics?.CardRetrievals.Count > 0
            && state.HandCardIds.Contains(
                action.SourceId,
                StringComparer.OrdinalIgnoreCase))
        {
            state.UnmarkUsed(actionIndex);
        }
        var selfHpLoss = Math.Max(
            0d,
            (action.Semantics?.SelfHpLoss ?? 0d)
            + (action.Semantics?.EndOfCycleSelfHpLoss ?? 0d));
        if (selfHpLoss > 0d)
        {
            state.PlayerHp = Math.Max(
                0,
                state.PlayerHp
                - Math.Max(0, (int)Math.Ceiling(selfHpLoss)));
        }
        if (stateChanges != null)
        {
            foreach (var pair in stateChanges)
            {
                state.Features[pair.Key] = Value(state.Features, pair.Key) + pair.Value;
                if (string.Equals(
                        pair.Key,
                        "status:buff_rebirth",
                        StringComparison.OrdinalIgnoreCase))
                {
                    state.Features[CombatArchetypePolicy.RebirthStacksFeature] =
                        Math.Max(
                            0d,
                            Value(
                                state.Features,
                                CombatArchetypePolicy.RebirthStacksFeature)
                            + pair.Value);
                }
                if (string.Equals(
                        pair.Key,
                        "status:buff_keenedge",
                        StringComparison.OrdinalIgnoreCase))
                {
                    state.Features[CombatArchetypePolicy.KeenEdgeFeature] =
                        Math.Max(
                            0d,
                            Value(
                                state.Features,
                                CombatArchetypePolicy.KeenEdgeFeature)
                            + pair.Value);
                }
            }
        }
        ApplyArchetypeAction(state, source, action);
        ApplyRebirthIfNeeded(state);
        state.Uncertainty += Math.Max(0d, 1d - Math.Min(1d, outcome.Probability))
                             * profile.UncertaintyPenalty;
        return state;
    }

    private static void ApplyHandTransform(
        CombatSimulationState state,
        CombatActionObservation action,
        CombatHandTransformSemantic transform)
    {
        if (!transform.TransformAllHandCards
            || string.IsNullOrWhiteSpace(transform.TargetCardId)
            || state.HandCardIds.Count == 0)
        {
            return;
        }
        var count = state.HandCardIds.Count;
        var targetValue = action.Features.TryGetValue(
                "handTransformTargetCardValue",
                out var configuredValue)
            ? Math.Max(0d, configuredValue)
            : KnowledgeValue(transform.TargetCardId);
        state.HandCardIds = Enumerable.Repeat(
                transform.TargetCardId,
                count)
            .ToList();
        state.HandCardValues = Enumerable.Repeat(targetValue, count).ToList();
        state.HandCount = count;
        if (transform.TargetRetained)
        {
            state.RetainedHandCardIds = Enumerable.Repeat(
                    transform.TargetCardId,
                    count)
                .ToList();
            state.RetainedHandCardValues = Enumerable.Repeat(
                    targetValue,
                    count)
                .ToList();
        }
        else
        {
            state.RetainedHandCardIds.Clear();
            state.RetainedHandCardValues.Clear();
        }
        CopyFeature(
            action.Features,
            state.Features,
            "postTransformDepletionRisk");
        CopyFeature(
            action.Features,
            state.Features,
            "handTransformRenewableDeckValue");
        CopyFeature(
            action.Features,
            state.Features,
            "handTransformRenewableCardCount");
        CopyFeature(
            action.Features,
            state.Features,
            "expectedGrowthFromTransform");
    }

    private static void CopyFeature(
        IReadOnlyDictionary<string, double> source,
        IDictionary<string, double> target,
        string key)
    {
        if (source.TryGetValue(key, out var value)
            && !double.IsNaN(value)
            && !double.IsInfinity(value))
        {
            target[key] = value;
        }
    }

    public static CombatSimulationState ApplyEndTurn(
        CombatSimulationState source,
        CombatDecisionProfile profile,
        CombatSimulationStateArena? arena = null)
    {
        var state = source.CloneForTransition(
            cloneCardPiles: true,
            cloneFeatures: true,
            arena: arena);
        var livingEnemyHpBeforeEnemyPhase = LivingEnemyHp(state.Enemies);
        var enemyHpAtTurnStart = state.EnemyHpAtTurnStart > 0
            ? state.EnemyHpAtTurnStart
            : livingEnemyHpBeforeEnemyPhase;
        var madeProgress = livingEnemyHpBeforeEnemyPhase < enemyHpAtTurnStart;
        var hasEndTurnPurpose = CombatEndTurnSafety.HasDeliberatePurpose(
            state.Features);
        state.ConsecutiveNoProgressTurns = madeProgress || hasEndTurnPurpose
            ? 0
            : state.ConsecutiveNoProgressTurns + 1;
        var deferredSnapshot = threadDeferredEffectSnapshot ??= new List<
            CombatDeferredEffectSimulation>();
        deferredSnapshot.Clear();
        deferredSnapshot.AddRange(state.DeferredEffects);
        ResolveDeferredEffects(state, deferredSnapshot, 1d);
        state.DeferredEffects.Clear();
        state.Features[CombatArchetypePolicy.TimeCageCountFeature] = 0d;
        ApplyProjectedLifecycle(state, startTurn: false);
        ApplyRebirthIfNeeded(state);

        var unretained = threadUnretainedValues ??= new List<double>();
        unretained.Clear();
        unretained.AddRange(state.HandCardValues);
        foreach (var retainedValue in state.RetainedHandCardValues)
        {
            RemoveClosestIfPresent(unretained, retainedValue);
        }
        state.DiscardPileValues.AddRange(unretained);
        var unretainedIds = threadUnretainedIds ??= new List<string>();
        unretainedIds.Clear();
        unretainedIds.AddRange(state.HandCardIds);
        foreach (var retainedId in state.RetainedHandCardIds)
        {
            RemoveFirst(unretainedIds, retainedId);
        }
        state.DiscardPileCardIds.AddRange(unretainedIds);
        foreach (var cardId in unretainedIds)
        {
            ApplyTimeCageLifecycle(state, cardId, drawn: false);
        }
        state.HandCardValues.Clear();
        state.HandCardValues.AddRange(state.RetainedHandCardValues);
        state.HandCardIds.Clear();
        state.HandCardIds.AddRange(state.RetainedHandCardIds);
        state.HandCount = state.HandCardValues.Count;

        var blockable = state.ActiveBlockableThreat(profile.ThreatRiskTolerance);
        var unavoidable = 0d;
        for (var i = 0; i < state.Threats.Length; i++)
        {
            var threat = state.Threats[i];
            if (threat.SourceRuntimeId != 0
                && !state.TargetAlive(threat.SourceRuntimeId))
            {
                continue;
            }
            unavoidable += Math.Max(
                0d,
                (threat.UnblockableDamage + threat.DamageOverTime)
                * threat.Probability);
        }

        var blocked = Math.Min(state.PlayerDefend, Math.Max(0, (int)Math.Round(blockable)));
        state.PlayerDefend = Math.Max(0, state.PlayerDefend - blocked);
        var hpLoss = Math.Max(0d, blockable - blocked) + unavoidable;
        state.PlayerHp = Math.Max(0, state.PlayerHp - Math.Max(0, (int)Math.Ceiling(hpLoss)));
        ApplyRebirthIfNeeded(state);
        // The game clears ordinary shield before the next player action window.
        // End-of-round effects have already contributed to the enemy phase above.
        state.PlayerDefend = 0;
        state.Power = CombatTurnRules.NextTurnPower(
            state.Power,
            state.MaxPower);
        state.CostReduction = 0;
        Array.Clear(
            state.UsedActionWords,
            0,
            state.UsedActionWords.Length);
        Array.Clear(
            state.UsedActionCounts,
            0,
            state.UsedActionCounts.Length);
        state.StepCount++;
        state.Turn++;

        ApplyProjectedLifecycle(state, startTurn: true);
        ApplyRebirthIfNeeded(state);
        var drawPerTurn = state.Features.TryGetValue("drawPerTurn", out var configuredDraw)
            ? Math.Max(0, (int)Math.Round(configuredDraw))
            : 5;
        DrawCards(state, Math.Min(drawPerTurn, state.HandLimit));
        state.Threats = ProjectNextTurnThreats(source, state, profile);
        state.TurnActionsTaken = 0;
        state.TurnEnergySpent = 0;
        state.NoEffectActionAttemptsThisTurn = 0;
        state.EnemyHpAtTurnStart = LivingEnemyHp(state.Enemies);
        state.Features[CombatTurnFeatureNames.ActionsTakenThisTurn] = 0d;
        state.Features[CombatTurnFeatureNames.EnergySpentThisTurn] = 0d;
        state.Features[CombatTurnFeatureNames.EnemyHpAtTurnStart] =
            state.EnemyHpAtTurnStart;
        state.Features[CombatTurnFeatureNames.ConsecutiveNoProgressTurns] =
            state.ConsecutiveNoProgressTurns;
        state.Features[CombatTurnFeatureNames.NoEffectActionAttemptsThisTurn] =
            0d;
        state.Uncertainty += Math.Max(0d, profile.EndTurnUncertainty);
        return state;
    }

    private static int LivingEnemyHp(CombatSimulationUnit[] enemies)
    {
        var total = 0;
        for (var index = 0; index < enemies.Length; index++)
        {
            total += Math.Max(0, enemies[index].Hp);
        }
        return total;
    }

    private static void ApplyProjectedLifecycle(
        CombatSimulationState state,
        bool startTurn)
    {
        var prefix = startTurn ? "startTurn" : "endTurn";
        var hpLoss = Math.Max(
            0,
            (int)Math.Ceiling(
                Value(state.Features, prefix + "LifecycleHpLoss")));
        var heal = Math.Max(
            0,
            (int)Math.Floor(
                Value(state.Features, prefix + "LifecycleHeal")));
        var defend = Math.Max(
            0,
            (int)Math.Floor(
                Value(state.Features, prefix + "LifecycleDefend")));
        var powerGain = Math.Max(
            0,
            (int)Math.Floor(
                Value(state.Features, prefix + "LifecyclePowerGain")));
        var powerLoss = Math.Max(
            0,
            (int)Math.Ceiling(
                Value(state.Features, prefix + "LifecyclePowerLoss")));
        var draw = Math.Max(
            0,
            (int)Math.Floor(
                Value(state.Features, prefix + "LifecycleDraw")));
        state.PlayerHp = Math.Max(
            0,
            Math.Min(
                state.PlayerMaxHp,
                state.PlayerHp + heal - hpLoss));
        state.PlayerDefend = Math.Max(0, state.PlayerDefend + defend);
        state.Power = Math.Max(
            0,
            state.Power + powerGain - powerLoss);
        if (draw > 0)
        {
            DrawCards(state, draw);
        }
    }

    private static CombatSimulationThreat[] ProjectNextTurnThreats(
        CombatSimulationState source,
        CombatSimulationState next,
        CombatDecisionProfile profile)
    {
        if (next.AllEnemiesDefeated)
        {
            return Array.Empty<CombatSimulationThreat>();
        }

        var retention = Math.Max(
            0d,
            Math.Min(1d, profile.NextTurnThreatRetention));
        var probabilityFloor = Math.Max(
            0d,
            Math.Min(1d, profile.UnknownNextTurnThreatProbabilityFloor));
        var projected = new List<CombatSimulationThreat>();
        var coveredSources = new HashSet<int>();
        for (var i = 0; i < source.Threats.Length; i++)
        {
            var threat = source.Threats[i];
            var enemy = threat.SourceRuntimeId == 0
                ? null
                : next.Enemies.FirstOrDefault(item =>
                    item.RuntimeId == threat.SourceRuntimeId && item.Hp > 0);
            if (threat.SourceRuntimeId != 0 && enemy == null)
            {
                continue;
            }
            var escalation = enemy == null
                ? 0d
                : Value(enemy.Features, "escalationPressure");
            var damageScale = 1d + Math.Min(0.75d, escalation * 0.01d);
            projected.Add(new CombatSimulationThreat
            {
                SourceRuntimeId = threat.SourceRuntimeId,
                Probability = Math.Max(
                    probabilityFloor,
                    Math.Min(1d, threat.Probability * retention)),
                BlockableDamage = Math.Max(0d, threat.BlockableDamage)
                                  * damageScale,
                UnblockableDamage = Math.Max(0d, threat.UnblockableDamage)
                                    * damageScale,
                DamageOverTime = Math.Max(0d, threat.DamageOverTime)
                                 * damageScale
            });
            if (threat.SourceRuntimeId != 0)
            {
                coveredSources.Add(threat.SourceRuntimeId);
            }
        }

        foreach (var enemy in next.Enemies.Where(item => item.Hp > 0))
        {
            if (coveredSources.Contains(enemy.RuntimeId))
            {
                continue;
            }
            var attack = Math.Max(0d, Value(enemy.Features, "attack"));
            if (attack <= 0d)
            {
                continue;
            }
            var actionCount = Math.Max(
                1d,
                Value(enemy.Features, "actionCount"));
            var escalation = Math.Max(
                0d,
                Value(enemy.Features, "escalationPressure"));
            projected.Add(new CombatSimulationThreat
            {
                SourceRuntimeId = enemy.RuntimeId,
                Probability = probabilityFloor,
                BlockableDamage = attack
                                  * actionCount
                                  * (1d + Math.Min(0.75d, escalation * 0.01d))
            });
        }

        if (projected.Count == 0)
        {
            var fallback = Math.Max(
                Value(source.Features, "maximumBlockableDamage"),
                Value(source.Features, "expectedBlockableDamage"));
            if (fallback > 0d)
            {
                projected.Add(new CombatSimulationThreat
                {
                    Probability = probabilityFloor,
                    BlockableDamage = fallback
                });
            }
        }
        return projected.ToArray();
    }

    private static Dictionary<string, double> BuildEnemyFeatures(
        CombatUnitObservation enemy)
    {
        var result = new Dictionary<string, double>(
            enemy.Features,
            StringComparer.OrdinalIgnoreCase);
        result["attack"] = Math.Max(0d, enemy.Attack);
        if (!result.ContainsKey("actionCount"))
        {
            result["actionCount"] = 1d;
        }
        return result;
    }

    public static int EffectiveCost(CombatSimulationState state, CombatActionObservation action)
    {
        return Math.Max(0, RawDynamicCost(state, action) - state.CostReduction);
    }

    private static int RawDynamicCost(
        CombatSimulationState state,
        CombatActionObservation action)
    {
        if (action.Kind != CombatActionKind.PlayCard
            || !action.Features.TryGetValue("cardBaseCost", out var rawBaseCost))
        {
            return Math.Max(0, action.Cost);
        }
        var baseCost = Math.Max(0, (int)Math.Round(rawBaseCost));
        var cap = Math.Max(
            0,
            (int)Math.Round(action.Features.TryGetValue(
                "cardCostCap",
                out var configuredCap)
                ? configuredCap
                : 4d));
        var scaledCost = Math.Min(
            (int)(baseCost * Math.Max(0d, state.CardCostMultiplier)),
            cap);
        return Math.Max(
            0,
            scaledCost
            + (int)Math.Round(Value(action.Features, "cardTotalExCost"))
            + (int)Math.Round(Value(action.Features, "cardExCost"))
            + (int)Math.Round(Value(action.Features, "cardOnceExCost")));
    }

    private static void ApplyEffect(
        CombatSimulationState state,
        CombatEffectOperation effect,
        int fallbackTargetId)
    {
        var magnitude = Math.Max(0, (int)Math.Round(effect.Magnitude));
        var targetId = effect.TargetRuntimeId != 0 ? effect.TargetRuntimeId : fallbackTargetId;
        switch (effect.Kind)
        {
            case CombatEffectKind.Damage:
                ApplyDamage(
                    state,
                    targetId,
                    Math.Max(0, (int)Math.Round(effect.Magnitude * state.DamageMultiplier)),
                    bypassDefend: false);
                break;
            case CombatEffectKind.TrueDamage:
            case CombatEffectKind.DamageOverTime:
                ApplyDamage(state, targetId, magnitude, bypassDefend: true);
                break;
            case CombatEffectKind.GainDefend:
                var defendTarget = FindTarget(state, targetId);
                if (defendTarget != null)
                {
                    defendTarget.Defend += magnitude;
                    break;
                }
                state.PlayerDefend += magnitude;
                break;
            case CombatEffectKind.Heal:
                var healTarget = FindTarget(state, targetId);
                if (healTarget != null)
                {
                    healTarget.Hp = Math.Min(healTarget.MaxHp, healTarget.Hp + magnitude);
                    break;
                }
                state.PlayerHp = Math.Min(state.PlayerMaxHp, state.PlayerHp + magnitude);
                break;
            case CombatEffectKind.Draw:
                var availableSlots = Math.Max(0, state.HandLimit - state.HandCount);
                DrawCards(state, Math.Min(magnitude, availableSlots));
                break;
            case CombatEffectKind.GenerateCard:
                state.HandCount = Math.Min(state.HandLimit, state.HandCount + magnitude);
                break;
            case CombatEffectKind.RetrieveCards:
                RetrieveCards(state, effect, magnitude);
                break;
            case CombatEffectKind.GainEnergy:
                state.Power += magnitude;
                break;
            case CombatEffectKind.SetEnergy:
                state.Power = string.Equals(
                    effect.SemanticId,
                    "maximum",
                    StringComparison.OrdinalIgnoreCase)
                    ? Math.Max(0, state.MaxPower)
                    : string.Equals(
                        effect.SemanticId,
                        "minimum",
                        StringComparison.OrdinalIgnoreCase)
                        ? Math.Max(state.Power, magnitude)
                        : magnitude;
                break;
            case CombatEffectKind.SetCardCostMultiplier:
                state.CardCostMultiplier = Math.Max(0d, effect.Magnitude);
                break;
            case CombatEffectKind.ReduceCost:
                state.CostReduction += magnitude;
                break;
            case CombatEffectKind.PersistentValue:
            case CombatEffectKind.Scaling:
                state.PersistentValue += Math.Max(0d, effect.Magnitude);
                break;
            case CombatEffectKind.Buff:
            case CombatEffectKind.Debuff:
            case CombatEffectKind.Cleanse:
                state.SetupValue += Math.Max(0d, effect.Magnitude);
                break;
            case CombatEffectKind.DamageMultiplier:
                state.DamageMultiplier = Math.Max(
                    0d,
                    state.DamageMultiplier + Math.Max(0d, effect.Magnitude));
                break;
        }
    }

    private static void AddRetrieval(
        CombatActionOutcome outcome,
        CombatCardRetrievalSemantic retrieval,
        int selectionRank,
        CombatActionModelArena? arena = null)
    {
        if (retrieval == null || retrieval.Amount <= 0)
        {
            return;
        }
        var effect = RentEffect(arena);
        effect.Kind = CombatEffectKind.RetrieveCards;
        effect.Magnitude = retrieval.Amount;
        effect.SemanticId = retrieval.RequiredCardTag ?? "";
        effect.SourceCardZone = retrieval.SourceZone;
        effect.DestinationCardZone = retrieval.DestinationZone;
        effect.SelectionRank = Math.Max(0, selectionRank);
        outcome.Effects.Add(effect);
    }

    private static CombatActionOutcome CloneBranchedOutcome(
        CombatActionOutcome source,
        int selectionRank,
        double probability,
        CombatActionModelArena? arena = null)
    {
        var clone = arena?.RentOutcome() ?? new CombatActionOutcome();
        clone.OutcomeId = "branch-rank-" + selectionRank;
        clone.Probability = probability;
        foreach (var sourceEffect in source.Effects)
        {
            var effect = RentEffect(arena);
            effect.Kind = sourceEffect.Kind;
            effect.TargetRuntimeId = sourceEffect.TargetRuntimeId;
            effect.Magnitude = sourceEffect.Kind == CombatEffectKind.SetCardCostMultiplier
                    ? selectionRank
                    : sourceEffect.Magnitude;
            effect.SecondaryMagnitude = sourceEffect.SecondaryMagnitude;
            effect.SemanticId = sourceEffect.SemanticId;
            effect.SourceCardZone = sourceEffect.SourceCardZone;
            effect.DestinationCardZone = sourceEffect.DestinationCardZone;
            effect.SelectionRank = sourceEffect.Kind == CombatEffectKind.RetrieveCards
                    ? selectionRank
                    : sourceEffect.SelectionRank;
            clone.Effects.Add(effect);
        }
        return clone;
    }

    private static bool HasRandomCardCostStatus(CombatStateObservation root)
    {
        return root.Player?.Statuses?.Any(status =>
            string.Equals(
                status.StatusId,
                "buff_chaos",
                StringComparison.OrdinalIgnoreCase)
            || status.DisplayName?.IndexOf(
                "Chaos",
                StringComparison.OrdinalIgnoreCase) >= 0
            || status.DisplayName?.IndexOf(
                "混乱",
                StringComparison.OrdinalIgnoreCase) >= 0) == true;
    }

    private static void RetrieveCards(
        CombatSimulationState state,
        CombatEffectOperation effect,
        int requestedAmount)
    {
        if (requestedAmount <= 0
            || effect.SourceCardZone == effect.DestinationCardZone
            || !TryGetCardZone(
                state,
                effect.SourceCardZone,
                out var sourceIds,
                out var sourceValues)
            || !TryGetCardZone(
                state,
                effect.DestinationCardZone,
                out var destinationIds,
                out var destinationValues))
        {
            return;
        }
        var amount = effect.DestinationCardZone == CombatCardZoneKind.Hand
            ? Math.Min(
                requestedAmount,
                Math.Max(0, state.HandLimit - state.HandCount))
            : requestedAmount;
        var candidates = Enumerable.Range(
                0,
                Math.Min(sourceIds.Count, sourceValues.Count))
            .Where(index => HasCardTag(
                state,
                sourceIds[index],
                effect.SemanticId))
            .OrderByDescending(index => sourceValues[index])
            .ThenBy(index => sourceIds[index], StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (amount <= 0 || candidates.Count == 0)
        {
            return;
        }
        var rank = Math.Min(
            Math.Max(0, effect.SelectionRank),
            Math.Max(0, candidates.Count - 1));
        var selected = candidates
            .Skip(rank)
            .Take(amount)
            .ToList();
        if (selected.Count < amount)
        {
            selected.AddRange(candidates
                .Take(amount - selected.Count)
                .Where(index => !selected.Contains(index)));
        }
        var moved = selected
            .Distinct()
            .Select(index => (
                Index: index,
                Id: sourceIds[index],
                Value: sourceValues[index]))
            .ToList();
        foreach (var item in moved.OrderByDescending(item => item.Index))
        {
            sourceIds.RemoveAt(item.Index);
            sourceValues.RemoveAt(item.Index);
        }
        foreach (var item in moved)
        {
            destinationIds.Add(item.Id);
            destinationValues.Add(item.Value);
        }
        if (effect.SourceCardZone == CombatCardZoneKind.Hand)
        {
            state.HandCount = Math.Max(0, state.HandCount - moved.Count);
        }
        if (effect.DestinationCardZone == CombatCardZoneKind.Hand)
        {
            state.HandCount = Math.Min(
                state.HandLimit,
                state.HandCount + moved.Count);
        }
    }

    private static bool TryGetCardZone(
        CombatSimulationState state,
        CombatCardZoneKind zone,
        out List<string> ids,
        out List<double> values)
    {
        switch (zone)
        {
            case CombatCardZoneKind.Hand:
                ids = state.HandCardIds;
                values = state.HandCardValues;
                return true;
            case CombatCardZoneKind.DiscardPile:
                ids = state.DiscardPileCardIds;
                values = state.DiscardPileValues;
                return true;
            case CombatCardZoneKind.ExhaustPile:
                ids = state.ExhaustPileCardIds;
                values = state.ExhaustPileValues;
                return true;
            default:
                ids = state.DrawPileCardIds;
                values = state.DrawPileValues;
                return true;
        }
    }

    private static bool HasCardTag(
        CombatSimulationState state,
        string cardId,
        string requiredTag)
    {
        return string.IsNullOrWhiteSpace(requiredTag)
               || state.KnownCardTags.TryGetValue(cardId ?? "", out var tags)
               && tags.Contains(
                   requiredTag,
                   StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyArchetypeAction(
        CombatSimulationState state,
        CombatSimulationState source,
        CombatActionObservation action)
    {
        var id = action.SourceId ?? "";
        if (string.Equals(
                id,
                "Crowdfundingcard_11",
                StringComparison.OrdinalIgnoreCase))
        {
            var keenEdge = Math.Max(
                0d,
                Value(
                    source.Features,
                    CombatArchetypePolicy.KeenEdgeFeature));
            var resurrectionCount = Math.Max(
                0,
                (int)Math.Round(Value(
                    source.Features,
                    CombatArchetypePolicy.ResurrectionCountFeature)));
            var dynamicDamage =
                (1d + keenEdge) * Math.Pow(2d, resurrectionCount) * 5d;
            var modeledDamage =
                Math.Max(0d, action.Semantics?.Damage ?? 0d)
                * Math.Max(1d, action.Semantics?.HitCount ?? 1d);
            var correction = Math.Max(
                0,
                (int)Math.Round(dynamicDamage - modeledDamage));
            if (correction > 0)
            {
                ApplyDamage(
                    state,
                    action.TargetRuntimeId,
                    correction,
                    bypassDefend: false);
            }
        }
        if (string.Equals(id, "timekeeper_4", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = state.DeferredEffects.ToList();
            ResolveDeferredEffects(state, snapshot, 1d);
            ResolveDeferredEffects(state, snapshot, 1d);
            ClearDeferredEffects(state);
            return;
        }
        if (string.Equals(id, "timekeeper_5", StringComparison.OrdinalIgnoreCase))
        {
            state.DeferredEffects.Reverse();
            ResequenceDeferredEffects(state);
            return;
        }
        if (string.Equals(id, "timekeeper_6", StringComparison.OrdinalIgnoreCase))
        {
            ResolveDeferredEffects(state, state.DeferredEffects.ToList(), 0.5d);
            state.Uncertainty += Math.Max(0.5d, state.DeferredEffects.Count * 0.25d);
            return;
        }
        if (string.Equals(id, "timekeeper_7", StringComparison.OrdinalIgnoreCase))
        {
            ResolveDeferredEffects(state, state.DeferredEffects.ToList(), 1d);
            return;
        }
        if (string.Equals(id, "timekeeper_8", StringComparison.OrdinalIgnoreCase))
        {
            if (state.DeferredEffects.Count > 0)
            {
                var first = state.DeferredEffects[0];
                ResolveDeferredEffects(state, new[] { first }, 1d);
                ResolveDeferredEffects(state, new[] { first }, 1d);
            }
            return;
        }
        if (string.Equals(id, "timekeeper_12", StringComparison.OrdinalIgnoreCase))
        {
            var packaged = state.HandCardIds
                .Where(cardId => !CombatArchetypePolicy.IsFrozenCard(cardId))
                .ToList();
            foreach (var cardId in packaged)
            {
                var value = KnowledgeValue(cardId);
                RemoveFirst(state.HandCardIds, cardId);
                RemoveClosestIfPresent(state.HandCardValues, value);
                state.HandCount = Math.Max(0, state.HandCount - 1);
                state.ExhaustPileCardIds.Add(cardId);
                state.ExhaustPileValues.Add(value);
                EnqueueDeferredEffect(state, cardId, 0);
            }
            DrawCards(
                state,
                Math.Min(
                    packaged.Count + 1,
                    Math.Max(0, state.HandLimit - state.HandCount)));
            return;
        }
        if (string.Equals(id, "timekeeper_13", StringComparison.OrdinalIgnoreCase))
        {
            if (state.DeferredEffects.Count > 0)
            {
                var last = state.DeferredEffects[state.DeferredEffects.Count - 1];
                ResolveDeferredEffects(state, new[] { last }, 1d);
                ResolveDeferredEffects(state, new[] { last }, 1d);
            }
        }
    }

    private static IReadOnlyDictionary<string, double>? DynamicRebirthStateChanges(
        CombatSimulationState source,
        CombatActionObservation action)
    {
        var original = action.Semantics?.StateChanges;
        var id = action.SourceId ?? "";
        if (!string.Equals(
                id,
                "Crowdfundingcard_8",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                id,
                "Crowdfundingcard_10",
                StringComparison.OrdinalIgnoreCase))
        {
            return original;
        }
        var result = original == null
            ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double>(
                original,
                StringComparer.OrdinalIgnoreCase);
        if (string.Equals(
                id,
                "Crowdfundingcard_8",
                StringComparison.OrdinalIgnoreCase))
        {
            if (Value(
                    source.Features,
                    CombatArchetypePolicy.ResurrectionCountFeature) > 0d)
            {
                result["status:buff_rebirth"] = 9d;
            }
            else
            {
                result.Remove("status:buff_rebirth");
            }
            return result;
        }
        var stacks = Math.Max(
            0,
            (int)Math.Round(Value(
                source.Features,
                CombatArchetypePolicy.RebirthStacksFeature)));
        var spent = stacks / 2;
        result["status:buff_rebirth"] = -spent;
        result["status:buff_keenedge"] = spent / 2;
        return result;
    }

    private static void EnqueueDeferredEffect(
        CombatSimulationState state,
        string sourceId,
        int targetRuntimeId)
    {
        var semantics = state.KnownCardSemantics.TryGetValue(
            sourceId,
            out var known)
            ? CombatSimulationState.CloneSemantics(known)
            : DeferredSemantics(state, sourceId);
        state.DeferredEffects.Add(new CombatDeferredEffectSimulation
        {
            Sequence = state.DeferredEffects.Count,
            StatusId = "buff_timelock",
            SourceId = sourceId,
            TargetRuntimeId = targetRuntimeId,
            Semantics = semantics
        });
        state.Features[CombatArchetypePolicy.TimeCageCountFeature] =
            state.DeferredEffects.Count;
    }

    private static void ResolveDeferredEffects(
        CombatSimulationState state,
        IReadOnlyList<CombatDeferredEffectSimulation> effects,
        double multiplier)
    {
        foreach (var effect in effects)
        {
            var semantics = DeferredSemantics(
                state,
                effect.SourceId,
                effect.Semantics);
            ApplyDeferredSemantics(
                state,
                effect.SourceId,
                effect.TargetRuntimeId,
                semantics,
                multiplier);
        }
    }

    private static void ApplyDeferredSemantics(
        CombatSimulationState state,
        string sourceId,
        int targetRuntimeId,
        CombatActionSemantics semantics,
        double multiplier)
    {
        if (multiplier <= 0d)
        {
            return;
        }
        var damage = Math.Max(0d, semantics.Damage)
                     * Math.Max(1d, semantics.HitCount)
                     * multiplier;
        if (damage > 0d)
        {
            if (string.Equals(
                    sourceId,
                    "timekeeper_9",
                    StringComparison.OrdinalIgnoreCase))
            {
                var living = Math.Max(1, state.Enemies.Count(enemy => enemy.Hp > 0));
                ApplyDamage(
                    state,
                    0,
                    Math.Max(0, (int)Math.Round(damage / living)),
                    bypassDefend: false);
            }
            else if (string.Equals(
                         sourceId,
                         "timekeeper_17",
                         StringComparison.OrdinalIgnoreCase))
            {
                ApplyDamage(
                    state,
                    0,
                    Math.Max(0, (int)Math.Round(damage)),
                    bypassDefend: false);
            }
            else
            {
                ApplyDamage(
                    state,
                    targetRuntimeId != 0
                        ? targetRuntimeId
                        : ConservativeDeferredTarget(state),
                    Math.Max(0, (int)Math.Round(damage)),
                    bypassDefend: false);
            }
        }
        var trueDamage = Math.Max(0d, semantics.TrueDamage) * multiplier;
        if (trueDamage > 0d)
        {
            ApplyDamage(
                state,
                targetRuntimeId != 0
                    ? targetRuntimeId
                    : ConservativeDeferredTarget(state),
                Math.Max(0, (int)Math.Round(trueDamage)),
                bypassDefend: true);
        }
        state.PlayerDefend += Math.Max(
            0,
            (int)Math.Round(Math.Max(0d, semantics.Defend) * multiplier));
        state.PlayerHp = Math.Min(
            state.PlayerMaxHp,
            state.PlayerHp
            + Math.Max(
                0,
                (int)Math.Round(Math.Max(0d, semantics.Heal) * multiplier)));
        var energy = Math.Max(
            0,
            (int)Math.Round(Math.Max(0d, semantics.EnergyGain) * multiplier));
        state.Power += energy;
        DrawCards(
            state,
            Math.Min(
                Math.Max(
                    0,
                    (int)Math.Round(Math.Max(0d, semantics.Draw) * multiplier)),
                Math.Max(0, state.HandLimit - state.HandCount)));
        state.SetupValue += Math.Max(0d, semantics.Buff) * multiplier;
        state.PersistentValue +=
            (Math.Max(0d, semantics.PersistentValue)
             + Math.Max(0d, semantics.Scaling))
            * multiplier;
        var hpLoss = Math.Max(0d, semantics.SelfHpLoss) * multiplier;
        if (hpLoss > 0d)
        {
            state.PlayerHp = Math.Max(
                0,
                state.PlayerHp - Math.Max(0, (int)Math.Ceiling(hpLoss)));
            ApplyRebirthIfNeeded(state);
        }
    }

    private static CombatActionSemantics DeferredSemantics(
        CombatSimulationState state,
        string sourceId,
        CombatActionSemantics? fallback = null)
    {
        var result = fallback == null
            ? new CombatActionSemantics()
            : CombatSimulationState.CloneSemantics(fallback);
        var count = Math.Max(1, state.DeferredEffects.Count);
        switch (sourceId)
        {
            case "timekeeper_3":
            {
                var uses = Math.Max(
                    0d,
                    Value(state.Features, "mechanic:timekeeper_3.uses"));
                result.Defend = uses;
                state.Features["mechanic:timekeeper_3.uses"] = uses + 1d;
                break;
            }
            case "timekeeper_9":
                result.Damage = 27d;
                result.HitCount = 1d;
                break;
            case "timekeeper_10":
                result.Draw = 1d;
                break;
            case "timekeeper_14":
                result.Defend = count;
                break;
            case "timekeeper_17":
                result.Damage = count;
                result.HitCount = 1d;
                break;
            case "timekeeper_18":
                result.Draw = 2d;
                result.DeckValue = 1d;
                break;
        }
        return result;
    }

    private static void ClearDeferredEffects(CombatSimulationState state)
    {
        state.DeferredEffects.Clear();
        state.Features[CombatArchetypePolicy.TimeCageCountFeature] = 0d;
    }

    private static int ConservativeDeferredTarget(CombatSimulationState state)
    {
        return state.Enemies
            .Where(enemy => enemy.Hp > 0)
            .OrderByDescending(enemy => enemy.Hp + enemy.Defend)
            .ThenBy(enemy => enemy.RuntimeId)
            .Select(enemy => enemy.RuntimeId)
            .FirstOrDefault();
    }

    private static void ResequenceDeferredEffects(CombatSimulationState state)
    {
        for (var i = 0; i < state.DeferredEffects.Count; i++)
        {
            state.DeferredEffects[i].Sequence = i;
        }
    }

    private static Dictionary<string, double> BuildStateFeatures(CombatStateObservation state)
    {
        var result = CombatPublicFeaturePolicy.SanitizeState(state.Features);
        foreach (var pair in CombatPublicFeaturePolicy.SanitizeUnit(
                     state.Player?.Features))
        {
            result["player." + pair.Key] = pair.Value;
        }
        return result;
    }

    private static void DrawCards(CombatSimulationState state, int amount)
    {
        for (var i = 0; i < amount && state.HandCount < state.HandLimit; i++)
        {
            if (state.DrawPileValues.Count == 0 && state.DiscardPileValues.Count > 0)
            {
                var shuffled = new List<KeyValuePair<string, double>>(
                    state.DiscardPileValues.Count);
                for (var cardIndex = 0;
                     cardIndex < state.DiscardPileValues.Count;
                     cardIndex++)
                {
                    shuffled.Add(new KeyValuePair<string, double>(
                        cardIndex < state.DiscardPileCardIds.Count
                            ? state.DiscardPileCardIds[cardIndex]
                            : "",
                        state.DiscardPileValues[cardIndex]));
                }
                ShuffleRecycledCards(state, shuffled);
                state.DrawPileValues.AddRange(
                    shuffled.Select(item => item.Value));
                state.DrawPileCardIds.AddRange(
                    shuffled.Select(item => item.Key));
                state.DiscardPileValues.Clear();
                state.DiscardPileCardIds.Clear();
            }
            if (state.DrawPileValues.Count == 0)
            {
                if (!state.DrawPileKnown)
                {
                    state.HandCount++;
                }
                continue;
            }

            var index = state.DrawPileValues.Count - 1;
            var cardValue = state.DrawPileValues[index];
            state.DrawPileValues.RemoveAt(index);
            state.HandCardValues.Add(cardValue);
            var cardId = index < state.DrawPileCardIds.Count
                ? state.DrawPileCardIds[index]
                : "";
            if (index < state.DrawPileCardIds.Count)
            {
                state.DrawPileCardIds.RemoveAt(index);
            }
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                state.HandCardIds.Add(cardId);
                ApplyTimeCageLifecycle(state, cardId, drawn: true);
            }
            state.DrawnCardPotential += Math.Max(0d, cardValue);
            state.HandCount++;
        }
    }

    private static void ShuffleRecycledCards(
        CombatSimulationState state,
        IList<KeyValuePair<string, double>> cards)
    {
        unchecked
        {
            var random = (uint)state.DeterminizationSeed
                         ^ (uint)(state.ShuffleEpoch + 1) * 0x9E3779B9u
                         ^ (uint)(state.Turn + 1) * 0x85EBCA6Bu
                         ^ (uint)(state.StepCount + 1) * 0xC2B2AE35u;
            if (random == 0u)
            {
                random = 0xA341316Cu;
            }
            for (var index = cards.Count - 1; index > 0; index--)
            {
                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                var selected = (int)(random % (uint)(index + 1));
                var current = cards[index];
                cards[index] = cards[selected];
                cards[selected] = current;
            }
            state.ShuffleEpoch++;
        }
    }

    private static void ApplyTimeCageLifecycle(
        CombatSimulationState state,
        string cardId,
        bool drawn)
    {
        if (!CombatArchetypePolicy.IsAutomaticTimeCagePayload(cardId))
        {
            return;
        }
        EnqueueDeferredEffect(state, cardId, 0);
        if (string.Equals(
                cardId,
                "timekeeper_10",
                StringComparison.OrdinalIgnoreCase)
            && drawn)
        {
            state.Power++;
        }
        if (string.Equals(
                cardId,
                "timekeeper_17",
                StringComparison.OrdinalIgnoreCase))
        {
            ApplyDamage(state, 0, 2, bypassDefend: false);
        }
    }

    private static void ApplyRebirthIfNeeded(CombatSimulationState state)
    {
        if (state.PlayerHp > 0)
        {
            return;
        }
        var stacks = Math.Max(
            0,
            (int)Math.Round(Value(
                state.Features,
                CombatArchetypePolicy.RebirthStacksFeature)));
        if (stacks < 30)
        {
            return;
        }
        state.PlayerHp = Math.Min(state.PlayerMaxHp, stacks);
        state.Features[CombatArchetypePolicy.RebirthStacksFeature] =
            Math.Max(0, stacks - 100);
        state.Features["status:buff_rebirth"] =
            Math.Max(0, stacks - 100);
        state.Features[CombatArchetypePolicy.ResurrectionCountFeature] =
            Value(
                state.Features,
                CombatArchetypePolicy.ResurrectionCountFeature)
            + 1d;
        state.Features["mechanic:rebirth.phase"] =
            (int)CombatRebirthPhase.Activated;
    }

    private static void RemoveClosest(IList<double> values, double expected)
    {
        if (values.Count == 0)
        {
            return;
        }
        var bestIndex = 0;
        var bestDistance = Math.Abs(values[0] - expected);
        for (var i = 1; i < values.Count; i++)
        {
            var distance = Math.Abs(values[i] - expected);
            if (distance < bestDistance)
            {
                bestIndex = i;
                bestDistance = distance;
            }
        }
        values.RemoveAt(bestIndex);
    }

    private static void RemoveClosestIfPresent(IList<double> values, double expected)
    {
        if (values.Count > 0)
        {
            RemoveClosest(values, expected);
        }
    }

    private static void RemoveFirst(IList<string> values, string expected)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(
                    values[i],
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                values.RemoveAt(i);
                return;
            }
        }
    }

    private static double Value(IReadOnlyDictionary<string, double> values, string key)
    {
        return values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static CombatSimulationUnit? FindTarget(CombatSimulationState state, int targetId)
    {
        if (targetId == 0)
        {
            return null;
        }

        for (var i = 0; i < state.Enemies.Length; i++)
        {
            if (state.Enemies[i].RuntimeId == targetId)
            {
                return state.Enemies[i];
            }
        }

        return null;
    }

    private static void ApplyDamage(
        CombatSimulationState state,
        int targetId,
        int amount,
        bool bypassDefend)
    {
        if (targetId == 0)
        {
            for (var i = 0; i < state.Enemies.Length; i++)
            {
                DamageUnit(state.Enemies[i], amount, bypassDefend);
            }
            return;
        }

        for (var i = 0; i < state.Enemies.Length; i++)
        {
            if (state.Enemies[i].RuntimeId == targetId)
            {
                DamageUnit(state.Enemies[i], amount, bypassDefend);
                return;
            }
        }
    }

    private static void DamageUnit(CombatSimulationUnit target, int amount, bool bypassDefend)
    {
        if (target.Hp <= 0)
        {
            return;
        }
        var positiveAmount = Math.Max(0, amount);
        var absorbed = bypassDefend
            ? 0
            : Math.Min(target.Defend, positiveAmount);
        target.Defend -= absorbed;
        var requestedHpDamage = Math.Max(0, positiveAmount - absorbed);
        var hpDamage = requestedHpDamage;
        if (CombatDamageLimitPolicy.TryGetRemainingBudget(
                target.Features,
                out var remaining))
        {
            hpDamage = (int)Math.Min(hpDamage, Math.Floor(remaining));
        }
        hpDamage = Math.Min(target.Hp, hpDamage);
        target.Hp = Math.Max(0, target.Hp - hpDamage);
        CombatDamageLimitPolicy.ConsumeBudget(target.Features, hpDamage);
    }

    private static CombatSimulationThreat[] BuildThreats(CombatStateObservation state)
    {
        var forecast = state.Threat ?? new CombatThreatForecast();
        if (forecast.Intents.Count > 0)
        {
            return forecast.Intents.Select(intent => new CombatSimulationThreat
            {
                SourceRuntimeId = intent.SourceRuntimeId,
                Probability = Math.Max(0d, Math.Min(1d, intent.Probability)),
                BlockableDamage = intent.BlockableDamage,
                UnblockableDamage = intent.UnblockableDamage,
                DamageOverTime = intent.DamageOverTime
            }).ToArray();
        }

        return new[]
        {
            new CombatSimulationThreat
            {
                Probability = Math.Max(0d, Math.Min(1d, forecast.AttackProbability)),
                BlockableDamage = forecast.ExpectedBlockableDamage,
                UnblockableDamage = forecast.ExpectedUnblockableDamage,
                DamageOverTime = forecast.ExpectedDamageOverTime
            }
        };
    }

    private static int ResolveHandLimit(CombatStateObservation state)
    {
        if (state.Features.TryGetValue("handLimit", out var configured))
        {
            return Math.Max(1, Math.Min(99, (int)Math.Round(configured)));
        }
        return 10;
    }

    private static double KnowledgeValue(string sourceId)
    {
        var action = new CombatActionObservation
        {
            SourceId = sourceId,
            Kind = CombatActionKind.PlayCard
        };
        if (!CombatKnowledgeRegistry.TryDescribeAction(
                action,
                out var semantics,
                out var fidelity,
                out _)
            || fidelity == CombatKnowledgeFidelity.Unsupported)
        {
            return 0d;
        }
        var confidence = fidelity == CombatKnowledgeFidelity.Authoritative
            ? 1d
            : fidelity == CombatKnowledgeFidelity.Derived
                ? 0.7d
                : 0.4d;
        var value = semantics.Damage * 0.45d
                    + semantics.TrueDamage * 0.6d
                    + semantics.Defend * 0.3d
                    + semantics.Heal * 0.35d
                    + semantics.Draw * 0.7d
                    + semantics.EnergyGain
                    + semantics.Buff * 0.5d
                    + semantics.Debuff * 0.45d
                    + semantics.Scaling
                    + semantics.PersistentValue
                    + semantics.DamageMultiplierGain * 100d;
        return Math.Max(0d, value * confidence);
    }

    private static void Add(
        CombatActionOutcome outcome,
        CombatEffectKind kind,
        int targetRuntimeId,
        double magnitude,
        CombatActionModelArena? arena = null)
    {
        if (magnitude <= 0d)
        {
            return;
        }
        var effect = RentEffect(arena);
        effect.Kind = kind;
        effect.TargetRuntimeId = targetRuntimeId;
        effect.Magnitude = magnitude;
        outcome.Effects.Add(effect);
    }

    private static CombatEffectOperation RentEffect(
        CombatActionModelArena? arena)
    {
        return arena?.RentEffect() ?? new CombatEffectOperation();
    }

    private static void NormalizeOutcomes(CombatActionModel model)
    {
        var total = model.Outcomes.Sum(outcome => Math.Max(0d, outcome.Probability));
        if (total <= 0d)
        {
            var equal = 1d / model.Outcomes.Count;
            for (var i = 0; i < model.Outcomes.Count; i++)
            {
                model.Outcomes[i].Probability = equal;
            }
            return;
        }
        for (var i = 0; i < model.Outcomes.Count; i++)
        {
            model.Outcomes[i].Probability = Math.Max(0d, model.Outcomes[i].Probability) / total;
        }
    }
}
