using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public sealed class DamageRunLedger
{
    private readonly Dictionary<string, CombatantDamageStat> combatants =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> completedSessionIds =
        new(StringComparer.Ordinal);
    private DamageBestHitRecord? bestHit;

    public string AdventureId { get; private set; } = "";

    public string StartedUtc { get; private set; } = "";

    public string UpdatedUtc { get; private set; } = "";

    public int EncounterCount { get; private set; }

    public int TotalRounds { get; private set; }

    public long ConfirmedEventCount { get; private set; }

    public string LastSessionId { get; private set; } = "";

    public long LastServerSequence { get; private set; }

    public IReadOnlyCollection<CombatantDamageStat> Combatants => combatants.Values;

    public bool HasDamage => combatants.Values.Any(stat => stat.TotalHpDamage > 0 || stat.TotalShieldDamage > 0);

    public long DisplayGrandTotal(bool countShield, bool friendlyOnly, bool includeUnknown)
    {
        return combatants.Values
            .Where(stat => !friendlyOnly
                           || stat.Team == DamageTeam.Friendly
                           || includeUnknown && stat.Team == DamageTeam.Unknown)
            .Where(stat => includeUnknown || stat.Team != DamageTeam.Unknown)
            .Sum(stat => stat.DisplayTotal(countShield));
    }

    public void BeginAdventure(string adventureId, string startedUtc)
    {
        AdventureId = adventureId ?? "";
        StartedUtc = startedUtc ?? "";
        UpdatedUtc = StartedUtc;
        EncounterCount = 0;
        TotalRounds = 0;
        ConfirmedEventCount = 0;
        LastSessionId = "";
        LastServerSequence = 0;
        bestHit = null;
        combatants.Clear();
        completedSessionIds.Clear();
    }

    public bool Apply(DamageEvent damage)
    {
        if (damage == null || damage.ProtocolVersion != DamageMeterProtocol.Version)
        {
            return false;
        }

        var hp = Math.Max(0, damage.HpDamage);
        var shield = Math.Max(0, damage.ShieldDamage);
        if (hp <= 0 && shield <= 0)
        {
            return false;
        }

        EnsureAdventure();
        ConfirmedEventCount++;
        LastSessionId = damage.SessionId ?? "";
        LastServerSequence = Math.Max(0, damage.ServerSequence);
        UpdatedUtc = DateTime.UtcNow.ToString("O");

        var sourceId = string.IsNullOrWhiteSpace(damage.SourceInstanceId)
            ? "unknown"
            : damage.SourceInstanceId.Trim();
        if (!combatants.TryGetValue(sourceId, out var stat))
        {
            stat = new CombatantDamageStat
            {
                InstanceId = sourceId,
                DisplayName = NormalizeDisplayName(damage.SourceDisplayName, sourceId),
                Team = damage.SourceTeam
            };
            combatants[sourceId] = stat;
        }

        stat.DisplayName = NormalizeDisplayName(damage.SourceDisplayName, stat.DisplayName);
        if (damage.SourceTeam != DamageTeam.Unknown)
        {
            stat.Team = damage.SourceTeam;
        }

        stat.TotalHpDamage += hp;
        stat.TotalShieldDamage += shield;
        AddDetail(stat, damage, hp, shield);
        TrackBestHit(damage, hp + shield);
        return true;
    }

    public bool RecordEncounter(DamageMeterSnapshot snapshot)
    {
        if (snapshot == null
            || snapshot.InFight
            || string.IsNullOrWhiteSpace(snapshot.SessionId)
            || completedSessionIds.Contains(snapshot.SessionId))
        {
            return false;
        }

        EnsureAdventure();
        completedSessionIds.Add(snapshot.SessionId);
        EncounterCount++;
        TotalRounds += Math.Max(0, snapshot.CompletedRoundCount);
        UpdatedUtc = DateTime.UtcNow.ToString("O");
        return true;
    }

    public DamageRunAggregateSnapshot CreateSnapshot()
    {
        return new DamageRunAggregateSnapshot
        {
            AdventureId = AdventureId,
            StartedUtc = StartedUtc,
            UpdatedUtc = UpdatedUtc,
            EncounterCount = EncounterCount,
            TotalRounds = TotalRounds,
            ConfirmedEventCount = ConfirmedEventCount,
            LastSessionId = LastSessionId,
            LastServerSequence = LastServerSequence,
            BestHit = bestHit?.Copy(),
            Combatants = combatants.Values
                .OrderBy(stat => stat.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(CloneStat)
                .ToList()
        };
    }

    public bool ApplySnapshot(DamageRunAggregateSnapshot snapshot)
    {
        if (snapshot == null || snapshot.ProtocolVersion != DamageMeterProtocol.Version)
        {
            return false;
        }

        var incomingAdventureId = snapshot.AdventureId ?? "";
        if (string.Equals(AdventureId, incomingAdventureId, StringComparison.Ordinal)
            && snapshot.ConfirmedEventCount < ConfirmedEventCount)
        {
            return false;
        }

        AdventureId = incomingAdventureId;
        StartedUtc = snapshot.StartedUtc ?? "";
        UpdatedUtc = snapshot.UpdatedUtc ?? "";
        EncounterCount = Math.Max(0, snapshot.EncounterCount);
        TotalRounds = Math.Max(0, snapshot.TotalRounds);
        ConfirmedEventCount = Math.Max(0, snapshot.ConfirmedEventCount);
        LastSessionId = snapshot.LastSessionId ?? "";
        LastServerSequence = Math.Max(0, snapshot.LastServerSequence);
        bestHit = CloneBestHit(snapshot.BestHit);
        combatants.Clear();
        completedSessionIds.Clear();

        foreach (var stat in snapshot.Combatants ?? new List<CombatantDamageStat>())
        {
            if (stat == null || string.IsNullOrWhiteSpace(stat.InstanceId))
            {
                continue;
            }

            var clone = CloneStat(stat);
            clone.CurrentRoundHpDamage = 0;
            clone.CurrentRoundShieldDamage = 0;
            clone.Rounds = new List<DamageRoundStat>();
            combatants[clone.InstanceId] = clone;
        }

        return true;
    }

    private void EnsureAdventure()
    {
        if (!string.IsNullOrWhiteSpace(AdventureId))
        {
            return;
        }

        BeginAdventure(Guid.NewGuid().ToString("N"), DateTime.UtcNow.ToString("O"));
    }

    private static void AddDetail(CombatantDamageStat stat, DamageEvent damage, int hp, int shield)
    {
        var key = string.IsNullOrWhiteSpace(damage.SourceDataId)
            ? (string.IsNullOrWhiteSpace(damage.DamageType) ? "unknown" : damage.DamageType.Trim())
            : damage.SourceDataId.Trim();
        if (!stat.Details.TryGetValue(key, out var detail))
        {
            if (stat.Details.Count >= DamageMeterProtocol.MaxDetailsPerCombatant - 1)
            {
                key = "other";
            }

            if (!stat.Details.TryGetValue(key, out detail))
            {
                detail = new DamageDetailStat
                {
                    Key = key,
                    Label = string.IsNullOrWhiteSpace(damage.DetailLabel) ? key : damage.DetailLabel.Trim(),
                    Confidence = damage.AttributionConfidence
                };
                stat.Details[key] = detail;
            }
        }

        detail.HpDamage += hp;
        detail.ShieldDamage += shield;
        if (damage.AttributionConfidence > detail.Confidence)
        {
            detail.Confidence = damage.AttributionConfidence;
        }
    }

    private void TrackBestHit(DamageEvent damage, long amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (bestHit != null
            && (bestHit.Damage > amount
                || bestHit.Damage == amount && string.CompareOrdinal(bestHit.EventId, damage.EventId) <= 0))
        {
            return;
        }

        bestHit = new DamageBestHitRecord
        {
            SessionId = damage.SessionId ?? "",
            SourceInstanceId = damage.SourceInstanceId ?? "",
            SourceDisplayName = NormalizeDisplayName(damage.SourceDisplayName, damage.SourceInstanceId ?? ""),
            SourceTeam = damage.SourceTeam,
            TargetInstanceId = damage.TargetInstanceId ?? "",
            SourceDataId = damage.SourceDataId ?? "",
            DetailLabel = damage.DetailLabel ?? "",
            DamageType = damage.DamageType ?? "",
            Damage = amount,
            RoundIndex = damage.RoundIndex,
            ServerSequence = damage.ServerSequence,
            EventId = damage.EventId
        };
    }

    private static CombatantDamageStat CloneStat(CombatantDamageStat source)
    {
        return new CombatantDamageStat
        {
            InstanceId = source.InstanceId ?? "",
            DisplayName = source.DisplayName ?? "",
            Team = source.Team,
            IsDead = source.IsDead,
            TotalHpDamage = source.TotalHpDamage,
            TotalShieldDamage = source.TotalShieldDamage,
            CurrentRoundHpDamage = 0,
            CurrentRoundShieldDamage = 0,
            Rounds = new List<DamageRoundStat>(),
            Details = (source.Details ?? new Dictionary<string, DamageDetailStat>())
                .ToDictionary(
                    pair => pair.Key,
                    pair => new DamageDetailStat
                    {
                        Key = pair.Value.Key,
                        Label = pair.Value.Label,
                        HpDamage = pair.Value.HpDamage,
                        ShieldDamage = pair.Value.ShieldDamage,
                        Confidence = pair.Value.Confidence
                    },
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static DamageBestHitRecord? CloneBestHit(DamageBestHitRecord? source)
    {
        if (source == null || source.Damage <= 0)
        {
            return null;
        }

        return source.Copy();
    }

    private static string NormalizeDisplayName(string value, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }
}

[Serializable]
public sealed class DamageRunAggregateSnapshot
{
    public int ProtocolVersion { get; set; } = DamageMeterProtocol.Version;

    public string AdventureId { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public string UpdatedUtc { get; set; } = "";

    public int EncounterCount { get; set; }

    public int TotalRounds { get; set; }

    public long ConfirmedEventCount { get; set; }

    public string LastSessionId { get; set; } = "";

    public long LastServerSequence { get; set; }

    public DamageBestHitRecord? BestHit { get; set; }

    public List<CombatantDamageStat> Combatants { get; set; } = new();
}
