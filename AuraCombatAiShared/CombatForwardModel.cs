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

    public bool AllEnemiesDefeated => Enemies.All(enemy => enemy.Hp <= 0);

    public CombatSimulationState Clone()
    {
        var enemies = new CombatSimulationUnit[Enemies.Length];
        for (var i = 0; i < enemies.Length; i++)
        {
            enemies[i] = Enemies[i].Clone();
        }

        var threats = new CombatSimulationThreat[Threats.Length];
        for (var i = 0; i < threats.Length; i++)
        {
            threats[i] = Threats[i].Clone();
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
            HandCardValues = new List<double>(HandCardValues),
            RetainedHandCardValues = new List<double>(RetainedHandCardValues),
            DrawPileValues = new List<double>(DrawPileValues),
            DiscardPileValues = new List<double>(DiscardPileValues),
            ExhaustPileValues = new List<double>(ExhaustPileValues),
            DrawPileKnown = DrawPileKnown,
            DrawnCardPotential = DrawnCardPotential,
            SetupValue = SetupValue,
            PersistentValue = PersistentValue,
            DamageMultiplier = DamageMultiplier,
            Features = new Dictionary<string, double>(Features, StringComparer.OrdinalIgnoreCase),
            Uncertainty = Uncertainty,
            Enemies = enemies,
            Threats = threats,
            UsedActionWords = (ulong[])UsedActionWords.Clone(),
            StepCount = StepCount,
            Turn = Turn
        };
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
            }
            for (var i = 0; i < UsedActionWords.Length; i++)
            {
                hash ^= UsedActionWords[i];
                hash *= 1099511628211UL;
            }
            return hash;
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

public sealed class CombatSimulationUnit
{
    public int RuntimeId { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int Defend { get; set; }

    public CombatSimulationUnit Clone()
    {
        return (CombatSimulationUnit)MemberwiseClone();
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
            DrawPileValues = state.DrawPileCardIds
                .Select(KnowledgeValue)
                .ToList(),
            DiscardPileValues = state.DiscardPileCardIds
                .Select(KnowledgeValue)
                .ToList(),
            ExhaustPileValues = state.ExhaustPileCardIds
                .Select(KnowledgeValue)
                .ToList(),
            DrawPileKnown = state.Features.ContainsKey("drawPileCount")
                            || state.DrawPileCardIds.Count > 0,
            Features = BuildStateFeatures(state),
            Enemies = state.Enemies.Select(enemy => new CombatSimulationUnit
            {
                RuntimeId = enemy.RuntimeId,
                Hp = enemy.CurrentHp,
                MaxHp = enemy.MaxHp,
                Defend = enemy.Defend
            }).ToArray(),
            Threats = threats,
            Turn = state.Features.TryGetValue("turn", out var turn)
                ? Math.Max(1, (int)Math.Round(turn))
                : 1,
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
        Add(outcome, CombatEffectKind.Damage, action.TargetRuntimeId, semantics.Damage * Math.Max(1d, semantics.HitCount));
        Add(outcome, CombatEffectKind.TrueDamage, action.TargetRuntimeId, semantics.TrueDamage);
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
        var state = source.Clone();
        var effectiveCost = Math.Max(0, action.Cost - state.CostReduction);
        var reductionSpent = Math.Min(Math.Max(0, action.Cost), state.CostReduction);
        state.CostReduction = Math.Max(0, state.CostReduction - reductionSpent);
        state.Power = Math.Max(0, state.Power - effectiveCost);
        var recycle = action.Features.TryGetValue("recycle", out var recycleValue)
                      && recycleValue > 0d;
        if (action.Kind == CombatActionKind.PlayCard && !recycle)
        {
            state.HandCount = Math.Max(0, state.HandCount - 1);
            var cardValue = KnowledgeValue(action.SourceId);
            RemoveClosest(state.HandCardValues, cardValue);
            if (action.Features.TryGetValue("retain", out var retained)
                && retained > 0d)
            {
                RemoveClosestIfPresent(state.RetainedHandCardValues, cardValue);
            }
            if (action.Features.TryGetValue("exhaustOnUse", out var exhaust)
                && exhaust > 0d)
            {
                state.ExhaustPileValues.Add(cardValue);
            }
            else if (action.Features.TryGetValue("ouroboros", out var ouroboros)
                     && ouroboros > 0d)
            {
                state.DrawPileValues.Add(cardValue);
            }
            else
            {
                state.DiscardPileValues.Add(cardValue);
            }
        }
        if (!recycle)
        {
            state.MarkUsed(actionIndex);
        }
        state.StepCount++;

        for (var i = 0; i < outcome.Effects.Count; i++)
        {
            ApplyEffect(state, outcome.Effects[i], action.TargetRuntimeId);
        }
        foreach (var pair in action.Semantics?.StateChanges
                     ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase))
        {
            state.Features[pair.Key] = Value(state.Features, pair.Key) + pair.Value;
        }
        state.Uncertainty += Math.Max(0d, 1d - Math.Min(1d, outcome.Probability))
                             * profile.UncertaintyPenalty;
        return state;
    }

    public static CombatSimulationState ApplyEndTurn(
        CombatSimulationState source,
        CombatDecisionProfile profile)
    {
        var state = source.Clone();
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

        var unretained = new List<double>(state.HandCardValues);
        foreach (var retainedValue in state.RetainedHandCardValues)
        {
            RemoveClosestIfPresent(unretained, retainedValue);
        }
        state.DiscardPileValues.AddRange(unretained);
        state.HandCardValues = new List<double>(state.RetainedHandCardValues);
        state.HandCount = state.HandCardValues.Count;
        state.Power = state.MaxPower;
        state.CostReduction = 0;
        state.UsedActionWords = new ulong[state.UsedActionWords.Length];
        state.StepCount++;
        state.Turn++;

        var drawPerTurn = state.Features.TryGetValue("drawPerTurn", out var configuredDraw)
            ? Math.Max(0, (int)Math.Round(configuredDraw))
            : 5;
        DrawCards(state, Math.Min(drawPerTurn, state.HandLimit));
        state.Threats = Array.Empty<CombatSimulationThreat>();
        state.Uncertainty += 0.25d;
        return state;
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
                var powerCap = state.MaxPower > 0
                    ? Math.Max(state.MaxPower, state.Power)
                    : state.Power + magnitude;
                state.Power = Math.Min(powerCap, state.Power + magnitude);
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

    private static Dictionary<string, double> BuildStateFeatures(CombatStateObservation state)
    {
        var result = new Dictionary<string, double>(
            state.Features ?? new Dictionary<string, double>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in state.Player?.Features ?? new Dictionary<string, double>())
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
                // The authoritative simulator owns seeded shuffle order.  The forward
                // model preserves the full cycle and uses a stable value ordering.
                state.DrawPileValues.AddRange(
                    state.DiscardPileValues.OrderBy(value => value));
                state.DiscardPileValues.Clear();
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
            state.DrawnCardPotential += Math.Max(0d, cardValue);
            state.HandCount++;
        }
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
