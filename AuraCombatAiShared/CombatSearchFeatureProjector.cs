using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatSearchFeatureProjector
{
    public static Dictionary<string, double> ProjectLeaf(
        CombatSimulationState state,
        CombatDecisionProfile profile,
        IReadOnlyDictionary<string, double>? rootFeatures = null)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        ProjectLeafInto(result, state, profile, rootFeatures);
        return result;
    }

    public static void ProjectLeafInto(
        IDictionary<string, double> result,
        CombatSimulationState state,
        CombatDecisionProfile profile,
        IReadOnlyDictionary<string, double>? rootFeatures = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.Clear();
        CopyFinite(result, rootFeatures);
        CopyFinite(result, state.Features);

        var expectedBlockable = 0d;
        var maximumBlockable = 0d;
        var expectedUnblockable = 0d;
        var expectedDamageOverTime = 0d;
        var attackProbability = 0d;
        for (var i = 0; i < state.Threats.Length; i++)
        {
            var threat = state.Threats[i];
            if (threat.SourceRuntimeId != 0
                && !state.Enemies.Any(enemy =>
                    enemy.RuntimeId == threat.SourceRuntimeId && enemy.Hp > 0))
            {
                continue;
            }
            expectedBlockable += Math.Max(0d, threat.BlockableDamage * threat.Probability);
            maximumBlockable += Math.Max(0d, threat.BlockableDamage);
            expectedUnblockable += Math.Max(0d, threat.UnblockableDamage * threat.Probability);
            expectedDamageOverTime += Math.Max(0d, threat.DamageOverTime * threat.Probability);
            attackProbability = Math.Max(attackProbability, threat.Probability);
        }

        result["playerHp"] = state.PlayerHp;
        result["playerMaxHp"] = state.PlayerMaxHp;
        result["playerHpRatio"] = state.PlayerMaxHp <= 0
            ? 0d
            : (double)state.PlayerHp / state.PlayerMaxHp;
        result["playerDefend"] = state.PlayerDefend;
        result["power"] = state.Power;
        result["maxPower"] = state.MaxPower;
        result["nextTurnPowerOnEnd"] =
            CombatTurnRules.NextTurnPower(state.Power, state.MaxPower);
        result["bankedSurplusPower"] =
            Math.Max(0, state.Power - state.MaxPower);
        result["expiringPower"] =
            state.Power <= state.MaxPower ? Math.Max(0, state.Power) : 0;
        result["handCount"] = state.HandCount;
        result["retainedHandCount"] = state.RetainedHandCardValues.Count;
        result["handLimit"] = state.HandLimit;
        if (state.Turn > 0 || !result.ContainsKey("turn"))
        {
            result["turn"] = state.Turn;
        }
        result["drawPileCount"] = state.DrawPileValues.Count;
        result["discardPileCount"] = state.DiscardPileValues.Count;
        result["exhaustPileCount"] = state.ExhaustPileValues.Count;
        result["mechanic:time-cage.count"] = state.DeferredEffects.Count;
        result["mechanic:time-cage.payload-value"] =
            state.DeferredEffects.Sum(item =>
                Math.Max(0d, item.Semantics.Damage)
                * Math.Max(1d, item.Semantics.HitCount)
                + Math.Max(0d, item.Semantics.Defend)
                + Math.Max(0d, item.Semantics.Draw) * 2d
                + Math.Max(0d, item.Semantics.EnergyGain) * 2d);
        var retainedCount = Math.Min(
            state.HandCount,
            state.RetainedHandCardValues.Count);
        var unretainedHandCount = Math.Max(
            0,
            state.HandCount - retainedCount);
        var recyclableCount = state.DrawPileValues.Count
                              + state.DiscardPileValues.Count
                              + unretainedHandCount;
        var availableHandSlots = Math.Max(0, state.HandLimit - state.HandCount);
        var nextTurnHandSlots = Math.Max(0, state.HandLimit - retainedCount);
        var requestedDraw = Math.Max(
            0d,
            Value(state.Features, "drawPerTurn", 5d));
        var effectiveNextDraw = Math.Min(requestedDraw, nextTurnHandSlots);
        result["recyclableCardCount"] = recyclableCount;
        result["unretainedHandCount"] = unretainedHandCount;
        result["lockedHandCount"] = retainedCount;
        result["availableHandSlots"] = availableHandSlots;
        result["effectiveNextDraw"] = effectiveNextDraw;
        result["drawPileShortfall"] = Math.Max(
            0d,
            effectiveNextDraw - state.DrawPileValues.Count);
        result["reshuffleWithinNextDraw"] =
            effectiveNextDraw > state.DrawPileValues.Count
            && state.DiscardPileValues.Count + unretainedHandCount > 0
                ? 1d
                : 0d;
        result["turnsToReshuffle"] = state.DrawPileValues.Count <= 0
            ? 0d
            : effectiveNextDraw <= 0d
                ? -1d
            : Math.Ceiling(
                (double)state.DrawPileValues.Count
                / effectiveNextDraw);
        result["cycleAccessRate"] = recyclableCount <= 0
            ? 0d
            : Math.Min(
                1d,
                effectiveNextDraw / recyclableCount);
        result["enemyCount"] = state.Enemies.Count(enemy => enemy.Hp > 0);
        result["enemyHpTotal"] = state.Enemies.Sum(enemy => Math.Max(0, enemy.Hp));
        result["expectedBlockableDamage"] = expectedBlockable;
        result["maximumBlockableDamage"] = maximumBlockable;
        result["expectedUnblockableDamage"] = expectedUnblockable;
        result["expectedDamageOverTime"] = expectedDamageOverTime;
        result["expectedIncomingDamage"] =
            expectedBlockable + expectedUnblockable + expectedDamageOverTime;
        result["attackProbability"] = attackProbability;
        result["blockableThreat"] = state.ActiveBlockableThreat(profile.ThreatRiskTolerance);
        result["stepCount"] = state.StepCount;
        result[CombatTurnFeatureNames.ActionsTakenThisTurn] =
            state.TurnActionsTaken;
        result[CombatTurnFeatureNames.EnergySpentThisTurn] =
            state.TurnEnergySpent;
        result[CombatTurnFeatureNames.EnemyHpAtTurnStart] =
            state.EnemyHpAtTurnStart;
        result[CombatTurnFeatureNames.ConsecutiveNoProgressTurns] =
            state.ConsecutiveNoProgressTurns;
        result[CombatTurnFeatureNames.NoEffectActionAttemptsThisTurn] =
            state.NoEffectActionAttemptsThisTurn;
        result["setupValue"] = state.SetupValue;
        result["persistentValue"] = state.PersistentValue;
        result["damageMultiplier"] = state.DamageMultiplier;
        result["uncertainty"] = state.Uncertainty;
    }

    private static double Value(
        IReadOnlyDictionary<string, double> values,
        string key,
        double fallback)
    {
        return values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : fallback;
    }

    private static void CopyFinite(
        IDictionary<string, double> target,
        IReadOnlyDictionary<string, double>? source)
    {
        if (source == null)
        {
            return;
        }
        foreach (var pair in source)
        {
            if (!double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value))
            {
                target[pair.Key] = pair.Value;
            }
        }
    }
}
