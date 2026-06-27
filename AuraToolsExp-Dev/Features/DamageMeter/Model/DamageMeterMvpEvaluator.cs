using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public interface IDamageMeterMvpEvaluator
{
    DamageMeterMvpResult Evaluate(DamageMeterSnapshot snapshot, bool countShield);
}

[Serializable]
public sealed class DamageMeterMvpResult
{
    public string InstanceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public long TotalDamage { get; set; }

    public double Dps { get; set; }
}

public sealed class HighestDpsMvpEvaluator : IDamageMeterMvpEvaluator
{
    public DamageMeterMvpResult Evaluate(DamageMeterSnapshot snapshot, bool countShield)
    {
        if (snapshot == null)
        {
            return new DamageMeterMvpResult();
        }

        var rounds = Math.Max(1, snapshot.CompletedRoundCount);
        return Candidates(snapshot.Combatants, countShield)
            .OrderByDescending(item => item.TotalDamage)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? new DamageMeterMvpResult();

        IEnumerable<DamageMeterMvpResult> Candidates(IEnumerable<CombatantDamageStat>? combatants, bool includeShield)
        {
            foreach (var stat in combatants ?? new List<CombatantDamageStat>())
            {
                if (stat == null || stat.Team != DamageTeam.Friendly)
                {
                    continue;
                }

                var total = stat.DisplayTotal(includeShield);
                if (total <= 0)
                {
                    continue;
                }

                yield return new DamageMeterMvpResult
                {
                    InstanceId = stat.InstanceId ?? "",
                    DisplayName = stat.DisplayName ?? "",
                    TotalDamage = total,
                    Dps = total / (double)rounds
                };
            }
        }
    }
}
