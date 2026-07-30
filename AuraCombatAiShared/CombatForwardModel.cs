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

    public int StepCount { get; set; }

    public int Turn { get; set; }

    public int TurnActionsTaken { get; set; }

    public int TurnEnergySpent { get; set; }

    public int EnemyHpAtTurnStart { get; set; }

    public int ConsecutiveNoProgressTurns { get; set; }

    public bool AllEnemiesDefeated => Enemies.All(enemy => enemy.Hp <= 0);

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
        bool cloneThreats = false)
    {
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
            StepCount = StepCount,
            Turn = Turn,
            TurnActionsTaken = TurnActionsTaken,
            TurnEnergySpent = TurnEnergySpent,
            EnemyHpAtTurnStart = EnemyHpAtTurnStart,
            ConsecutiveNoProgressTurns = ConsecutiveNoProgressTurns
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

    public void MarkUsed(int actionIndex)
    {
        var word = actionIndex >> 6;
        var bit = actionIndex & 63;
        UsedActionWords[word] |= 1UL << bit;
    }

    public bool TargetAlive(int runtimeId)
    {
        return runtimeId == 0
               || runtimeId == PlayerRuntimeId
               || Enemies.Any(enemy => enemy.RuntimeId == runtimeId && enemy.Hp > 0);
    }

    public double ActiveBlockableThreat(double riskTolerance)
    {
        var expected = 0d;
        var maximum = 0d;
        for (var i = 0; i < Threats.Length; i++)
        {
            var threat = Threats[i];
            if (threat.SourceRuntimeId != 0
                && !Enemies.Any(enemy => enemy.RuntimeId == threat.SourceRuntimeId && enemy.Hp > 0))
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
                && !Enemies.Any(enemy => enemy.RuntimeId == threat.SourceRuntimeId && enemy.Hp > 0))
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
        var livingMaxHp = Enemies.Sum(enemy => Math.Max(1, enemy.MaxHp));
        var livingHp = Enemies.Sum(enemy => Math.Max(0, enemy.Hp));
        var enemyProgress = livingMaxHp <= 0
            ? 1d
            : 1d - Math.Min(1d, (double)livingHp / livingMaxHp);
        var immediateShield = Math.Min(PlayerDefend, blockable);
        var carriedShield = Math.Max(0d, PlayerDefend - blockable);
        var cycleSize = DrawPileValues.Count + DiscardPileValues.Count + HandCardValues.Count;
        var cycleAccess = cycleSize <= 0
            ? 0d
            : Math.Min(1d, (double)Math.Max(1, HandLimit) / cycleSize);
        var value = Ratio(PlayerHp, PlayerMaxHp) * 40d
                    - hpLoss * 1.8d
                    + enemyProgress * 35d
                    + Power * 0.15d
                    + immediateShield * 0.2d
                    + carriedShield * Math.Max(0d, profile.SurplusDefendRetention) * 0.2d
                    + cycleAccess * 2d
                    + SetupValue * Math.Max(0d, profile.SetupValueWeight)
                    + PersistentValue * Math.Max(0d, profile.PersistentValueWeight)
                    + DrawnCardPotential * 0.2d
                    - ConsecutiveNoProgressTurns * 8d
                    - Uncertainty * profile.UncertaintyPenalty;
        return new CombatLeafEvaluation
        {
            Value = value,
            DeathRisk = Math.Max(0d, Math.Min(1d, deathRisk))
        };
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
            Mix(ref hash, StepCount);
            Mix(ref hash, Turn);
            Mix(ref hash, TurnActionsTaken);
            Mix(ref hash, TurnEnergySpent);
            Mix(ref hash, EnemyHpAtTurnStart);
            Mix(ref hash, ConsecutiveNoProgressTurns);
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
            foreach (var pair in Features.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                foreach (var character in pair.Key)
                {
                    Mix(ref hash, character);
                }
                Mix(ref hash, Quantize(pair.Value));
            }
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
            Mix(ref hash, Power);
            Mix(ref hash, MaxPower);
            Mix(ref hash, HandCount);
            Mix(ref hash, HandLimit);
            Mix(ref hash, CostReduction);
            Mix(ref hash, Turn);
            Mix(ref hash, TurnActionsTaken);
            Mix(ref hash, TurnEnergySpent);
            Mix(ref hash, ConsecutiveNoProgressTurns);
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
        foreach (var pair in features.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            foreach (var character in pair.Key)
            {
                Mix(ref hash, character);
            }
            Mix(ref hash, Quantize(pair.Value));
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

public sealed class CombatLeafEvaluation
{
    public double Value { get; set; }

    public double DeathRisk { get; set; }
}

public static class CombatForwardModel
{
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
            drawPileKnown: belief.DrawPileCount > 0);
    }

    private static CombatSimulationState CreateCore(
        CombatStateObservation state,
        int actionCount,
        IReadOnlyList<string> sampledDrawPile,
        bool drawPileKnown)
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
            UsedActionWords = new ulong[(Math.Max(0, actionCount) + 63) / 64]
        };
    }

    public static CombatActionModel Resolve(
        CombatStateObservation root,
        CombatActionObservation action,
        bool useRegisteredResolvers = true)
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
        var outcome = new CombatActionOutcome
        {
            OutcomeId = "expected"
        };
        var immediateTargetEffects = semantics.TargetEffects
            .Where(item =>
                item.Phase == CombatSemanticEffectPhase.Immediate
                && item.Kind is CombatSemanticEffectKind.Damage
                    or CombatSemanticEffectKind.TrueDamage
                    or CombatSemanticEffectKind.DirectHpLoss)
            .ToList();
        if (immediateTargetEffects.Count > 0)
        {
            foreach (var effect in immediateTargetEffects)
            {
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
                    * Math.Max(
                        0d,
                        Math.Min(1d, effect.Probability)));
            }
        }
        else
        {
            Add(
                outcome,
                CombatEffectKind.Damage,
                action.TargetRuntimeId,
                semantics.Damage * Math.Max(1d, semantics.HitCount));
            Add(
                outcome,
                CombatEffectKind.TrueDamage,
                action.TargetRuntimeId,
                semantics.TrueDamage);
        }
        Add(outcome, CombatEffectKind.DamageOverTime, action.TargetRuntimeId, semantics.DamageOverTime);
        Add(outcome, CombatEffectKind.GainDefend, action.TargetRuntimeId, semantics.Defend);
        Add(outcome, CombatEffectKind.Heal, action.TargetRuntimeId, semantics.Heal);
        Add(outcome, CombatEffectKind.Draw, 0, semantics.Draw);
        Add(outcome, CombatEffectKind.GainEnergy, 0, semantics.EnergyGain);
        Add(outcome, CombatEffectKind.ReduceCost, 0, semantics.CostReduction);
        Add(outcome, CombatEffectKind.Buff, action.TargetRuntimeId, semantics.Buff);
        Add(outcome, CombatEffectKind.Debuff, action.TargetRuntimeId, semantics.Debuff);
        Add(outcome, CombatEffectKind.Cleanse, action.TargetRuntimeId, semantics.Cleanse);
        Add(outcome, CombatEffectKind.GenerateCard, 0, semantics.CardGeneration);
        Add(outcome, CombatEffectKind.PersistentValue, 0, semantics.PersistentValue);
        Add(outcome, CombatEffectKind.Scaling, 0, semantics.Scaling);
        Add(outcome, CombatEffectKind.DamageMultiplier, 0, semantics.DamageMultiplierGain);
        return new CombatActionModel
        {
            Confidence = Math.Max(0d, Math.Min(1d, 1d - semantics.Uncertainty / 3d)),
            Outcomes = new List<CombatActionOutcome> { outcome }
        };
    }

    public static CombatSimulationState Apply(
        CombatSimulationState source,
        CombatActionObservation action,
        int actionIndex,
        CombatActionOutcome outcome,
        CombatDecisionProfile profile)
    {
        var stateChanges = DynamicRebirthStateChanges(source, action);
        var mutatesCardPiles = action.Kind == CombatActionKind.PlayCard;
        for (var effectIndex = 0;
             !mutatesCardPiles && effectIndex < outcome.Effects.Count;
             effectIndex++)
        {
            mutatesCardPiles =
                outcome.Effects[effectIndex].Kind == CombatEffectKind.Draw;
        }
        var state = source.CloneForTransition(
            mutatesCardPiles,
            stateChanges != null && stateChanges.Count > 0);
        var effectiveCost = Math.Max(0, action.Cost - state.CostReduction);
        var reductionSpent = Math.Min(Math.Max(0, action.Cost), state.CostReduction);
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
        state.StepCount++;

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

    public static CombatSimulationState ApplyEndTurn(
        CombatSimulationState source,
        CombatDecisionProfile profile)
    {
        var state = source.CloneForTransition(
            cloneCardPiles: true,
            cloneFeatures: true);
        var livingEnemyHpBeforeEnemyPhase = state.Enemies.Sum(enemy =>
            Math.Max(0, enemy.Hp));
        var enemyHpAtTurnStart = state.EnemyHpAtTurnStart > 0
            ? state.EnemyHpAtTurnStart
            : livingEnemyHpBeforeEnemyPhase;
        var madeProgress = livingEnemyHpBeforeEnemyPhase < enemyHpAtTurnStart;
        var hasEndTurnPurpose = CombatEndTurnSafety.HasDeliberatePurpose(
            state.Features);
        state.ConsecutiveNoProgressTurns = madeProgress || hasEndTurnPurpose
            ? 0
            : state.ConsecutiveNoProgressTurns + 1;
        ResolveDeferredEffects(state, state.DeferredEffects.ToList(), 1d);
        state.DeferredEffects.Clear();
        state.Features[CombatArchetypePolicy.TimeCageCountFeature] = 0d;

        var unretained = new List<double>(state.HandCardValues);
        foreach (var retainedValue in state.RetainedHandCardValues)
        {
            RemoveClosestIfPresent(unretained, retainedValue);
        }
        state.DiscardPileValues.AddRange(unretained);
        var unretainedIds = new List<string>(state.HandCardIds);
        foreach (var retainedId in state.RetainedHandCardIds)
        {
            RemoveFirst(unretainedIds, retainedId);
        }
        state.DiscardPileCardIds.AddRange(unretainedIds);
        foreach (var cardId in unretainedIds)
        {
            ApplyTimeCageLifecycle(state, cardId, drawn: false);
        }
        state.HandCardValues = new List<double>(state.RetainedHandCardValues);
        state.HandCardIds = new List<string>(state.RetainedHandCardIds);
        state.HandCount = state.HandCardValues.Count;

        var blockable = state.ActiveBlockableThreat(profile.ThreatRiskTolerance);
        var unavoidable = 0d;
        for (var i = 0; i < state.Threats.Length; i++)
        {
            var threat = state.Threats[i];
            if (threat.SourceRuntimeId != 0
                && !state.Enemies.Any(enemy =>
                    enemy.RuntimeId == threat.SourceRuntimeId && enemy.Hp > 0))
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
        state.Power = Math.Max(state.MaxPower, state.Power);
        state.CostReduction = 0;
        state.UsedActionWords = new ulong[state.UsedActionWords.Length];
        state.StepCount++;
        state.Turn++;

        var drawPerTurn = state.Features.TryGetValue("drawPerTurn", out var configuredDraw)
            ? Math.Max(0, (int)Math.Round(configuredDraw))
            : 5;
        DrawCards(state, Math.Min(drawPerTurn, state.HandLimit));
        state.Threats = ProjectNextTurnThreats(source, state, profile);
        state.TurnActionsTaken = 0;
        state.TurnEnergySpent = 0;
        state.EnemyHpAtTurnStart = state.Enemies.Sum(enemy =>
            Math.Max(0, enemy.Hp));
        state.Features[CombatTurnFeatureNames.ActionsTakenThisTurn] = 0d;
        state.Features[CombatTurnFeatureNames.EnergySpentThisTurn] = 0d;
        state.Features[CombatTurnFeatureNames.EnemyHpAtTurnStart] =
            state.EnemyHpAtTurnStart;
        state.Features[CombatTurnFeatureNames.ConsecutiveNoProgressTurns] =
            state.ConsecutiveNoProgressTurns;
        state.Uncertainty += Math.Max(0d, profile.EndTurnUncertainty);
        return state;
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
        return Math.Max(0, action.Cost - state.CostReduction);
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
            case CombatEffectKind.GainEnergy:
                state.Power += magnitude;
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
                var shuffled = state.DiscardPileCardIds.Count
                               == state.DiscardPileValues.Count
                    ? state.DiscardPileCardIds
                        .Select((id, index) => new
                        {
                            Id = id,
                            Value = state.DiscardPileValues[index]
                        })
                        .OrderBy(item => item.Value)
                        .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : null;
                state.DrawPileValues.AddRange(
                    shuffled?.Select(item => item.Value)
                    ?? state.DiscardPileValues.OrderBy(value => value));
                state.DrawPileCardIds.AddRange(
                    shuffled?.Select(item => item.Id)
                    ?? state.DiscardPileCardIds.OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase));
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
        var absorbed = bypassDefend ? 0 : Math.Min(target.Defend, amount);
        target.Defend -= absorbed;
        target.Hp = Math.Max(0, target.Hp - Math.Max(0, amount - absorbed));
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
        double magnitude)
    {
        if (magnitude <= 0d)
        {
            return;
        }
        outcome.Effects.Add(new CombatEffectOperation
        {
            Kind = kind,
            TargetRuntimeId = targetRuntimeId,
            Magnitude = magnitude
        });
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
