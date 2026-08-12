using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public static class DamageMeterProtocol
{
    public const int Version = 4;
    public const int MaxDamagePerEvent = 100000000;
    public const int MaxStringLength = 160;
    public const int MaxDetailsPerCombatant = 64;
    public const int MaxRoundsKept = 100;
    public const int MaxTeamMembers = 4;
    public const int MaxHistoryNameLength = 12;
}

public static class DamageMeterRecordNames
{
    public const string BestHit = "摧枯拉朽";
}

public static class DamageAllocation
{
    public static int[] ProportionalSplit(int amount, IReadOnlyList<int>? weights)
    {
        if (weights == null || weights.Count == 0)
        {
            return Array.Empty<int>();
        }

        var count = weights.Count;
        var result = new int[count];
        amount = Math.Max(0, amount);
        var normalizedWeights = new int[count];
        var totalWeight = 0L;
        for (var i = 0; i < count; i++)
        {
            var weight = Math.Max(0, weights[i]);
            normalizedWeights[i] = weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0)
        {
            return result;
        }

        var assigned = 0;
        var remainders = new long[count];
        for (var i = 0; i < count; i++)
        {
            var weightedAmount = (long)amount * normalizedWeights[i];
            result[i] = (int)(weightedAmount / totalWeight);
            remainders[i] = weightedAmount % totalWeight;
            assigned += result[i];
        }

        var extra = amount - assigned;
        for (var step = 0; step < extra; step++)
        {
            var bestIndex = -1;
            var bestRemainder = -1L;
            for (var i = 0; i < count; i++)
            {
                if (normalizedWeights[i] <= 0 || remainders[i] < 0)
                {
                    continue;
                }

                if (bestIndex < 0 || remainders[i] > bestRemainder)
                {
                    bestIndex = i;
                    bestRemainder = remainders[i];
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            result[bestIndex]++;
            remainders[bestIndex] = -1;
        }

        return result;
    }
}

public enum DamageTeam
{
    Unknown,
    Friendly,
    Enemy
}

public enum DamageAttributionConfidence
{
    Exact,
    Derived,
    Mixed,
    Unknown
}

[Serializable]
public sealed class DamageEvent
{
    public int ProtocolVersion { get; set; } = DamageMeterProtocol.Version;

    public string SessionId { get; set; } = "";

    public string ReporterPlayerId { get; set; } = "";

    public long ReporterSequence { get; set; }

    public long ServerSequence { get; set; }

    public int RoundIndex { get; set; }

    public string SourceInstanceId { get; set; } = "";

    public string SourceDisplayName { get; set; } = "";

    public DamageTeam SourceTeam { get; set; }

    public string TargetInstanceId { get; set; } = "";

    public string SourceDataId { get; set; } = "";

    public string DetailLabel { get; set; } = "";

    public string DamageType { get; set; } = "";

    public int HpDamage { get; set; }

    public int ShieldDamage { get; set; }

    public int FinalDamage { get; set; }

    public DamageAttributionConfidence AttributionConfidence { get; set; }

    public long ClientTimestampMs { get; set; }

    public string EventId => SessionId + "|" + ReporterPlayerId + "|" + ReporterSequence;

    public DamageEvent Copy()
    {
        return (DamageEvent)MemberwiseClone();
    }
}

[Serializable]
public sealed class DamageRoundStat
{
    public int RoundIndex { get; set; }

    public long HpDamage { get; set; }

    public long ShieldDamage { get; set; }
}

[Serializable]
public sealed class DamageDetailStat
{
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    public long HpDamage { get; set; }

    public long ShieldDamage { get; set; }

    public DamageAttributionConfidence Confidence { get; set; }
}

[Serializable]
public sealed class DamageBestHitRecord
{
    public string RecordName { get; set; } = DamageMeterRecordNames.BestHit;

    public string SessionId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string SourceDisplayName { get; set; } = "";

    public DamageTeam SourceTeam { get; set; }

    public string TargetInstanceId { get; set; } = "";

    public string SourceDataId { get; set; } = "";

    public string DetailLabel { get; set; } = "";

    public string DamageType { get; set; } = "";

    public long Damage { get; set; }

    public int RoundIndex { get; set; }

    public long ServerSequence { get; set; }

    public string EventId { get; set; } = "";

    public DamageBestHitRecord Copy()
    {
        return (DamageBestHitRecord)MemberwiseClone();
    }
}

[Serializable]
public sealed class CombatantDamageStat
{
    public string InstanceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public DamageTeam Team { get; set; }

    public bool IsDead { get; set; }

    public long TotalHpDamage { get; set; }

    public long TotalShieldDamage { get; set; }

    public long CurrentRoundHpDamage { get; set; }

    public long CurrentRoundShieldDamage { get; set; }

    public List<DamageRoundStat> Rounds { get; set; } = new();

    public Dictionary<string, DamageDetailStat> Details { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public long DisplayTotal(bool countShield)
    {
        return TotalHpDamage + (countShield ? TotalShieldDamage : 0);
    }

    public long DisplayCurrentRound(bool countShield)
    {
        return CurrentRoundHpDamage + (countShield ? CurrentRoundShieldDamage : 0);
    }

    public double AveragePerCompletedRound(bool countShield, int completedRounds)
    {
        return completedRounds <= 0 ? 0d : (double)DisplayTotal(countShield) / completedRounds;
    }

    public long HighestRound(bool countShield)
    {
        var highest = 0L;
        foreach (var round in Rounds)
        {
            highest = Math.Max(highest, round.HpDamage + (countShield ? round.ShieldDamage : 0));
        }

        return Math.Max(highest, DisplayCurrentRound(countShield));
    }
}

[Serializable]
public sealed class DamageMeterSnapshot
{
    public int ProtocolVersion { get; set; } = DamageMeterProtocol.Version;

    public string SessionId { get; set; } = "";

    public bool InFight { get; set; }

    public bool SharedEnabled { get; set; }

    public int CurrentRoundIndex { get; set; }

    public int CompletedRoundCount { get; set; }

    public long ServerSequence { get; set; }

    public DamageBestHitRecord? BestHit { get; set; }

    public List<CombatantDamageStat> Combatants { get; set; } = new();

    public DamageRunAggregateSnapshot? RunAggregate { get; set; }
}

[Serializable]
public sealed class DamageFightRecord
{
    public int Sequence { get; set; }

    public string SessionId { get; set; } = "";

    public string Result { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public DamageMeterSnapshot Snapshot { get; set; } = new();
}
