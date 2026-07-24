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
        result["handCount"] = state.HandCount;
        result["handLimit"] = state.HandLimit;
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
        result["setupValue"] = state.SetupValue;
        result["persistentValue"] = state.PersistentValue;
        result["damageMultiplier"] = state.DamageMultiplier;
        result["uncertainty"] = state.Uncertainty;
        return result;
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
