using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public sealed class DamageLedger
{
    private readonly Dictionary<string, CombatantDamageStat> combatants =
        new(StringComparer.OrdinalIgnoreCase);
    private DamageBestHitRecord? bestHit;

    public string SessionId { get; private set; } = "";

    public bool InFight { get; private set; }

    public bool SharedEnabled { get; private set; }

    public int CurrentRoundIndex { get; private set; }

    public int CompletedRoundCount { get; private set; }

    public long ServerSequence { get; private set; }

    public int AveragingRoundCount =>
        CompletedRoundCount + (InFight && CurrentRoundIndex > CompletedRoundCount ? 1 : 0);

    public IReadOnlyCollection<CombatantDamageStat> Combatants => combatants.Values;

    public void StartFight(string sessionId, bool sharedEnabled)
    {
        SessionId = sessionId ?? "";
        InFight = true;
        SharedEnabled = sharedEnabled;
        CurrentRoundIndex = 0;
        CompletedRoundCount = 0;
        ServerSequence = 0;
        bestHit = null;
        combatants.Clear();
    }

    public void StartRound(int roundIndex)
    {
        if (!InFight || roundIndex <= CurrentRoundIndex)
        {
            return;
        }

        if (CurrentRoundIndex > 0)
        {
            CloseCurrentRound();
        }

        CurrentRoundIndex = roundIndex;
    }

    public void EndFight()
    {
        if (!InFight)
        {
            return;
        }

        if (CurrentRoundIndex > CompletedRoundCount)
        {
            CloseCurrentRound();
        }

        InFight = false;
    }

    public bool Apply(DamageEvent damage)
    {
        if (damage == null
            || !InFight
            || damage.ProtocolVersion != DamageMeterProtocol.Version
            || !string.Equals(damage.SessionId, SessionId, StringComparison.Ordinal)
            || damage.ServerSequence != ServerSequence + 1)
        {
            return false;
        }

        var hp = Math.Max(0, damage.HpDamage);
        var shield = Math.Max(0, damage.ShieldDamage);
        if (hp <= 0 && shield <= 0)
        {
            return false;
        }

        ServerSequence = damage.ServerSequence;
        if (damage.RoundIndex > CurrentRoundIndex)
        {
            StartRound(damage.RoundIndex);
        }

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
        stat.CurrentRoundHpDamage += hp;
        stat.CurrentRoundShieldDamage += shield;
        AddDetail(stat, damage, hp, shield);
        TrackBestHit(damage, hp + shield);
        return true;
    }

    public long NextServerSequence()
    {
        return ServerSequence + 1;
    }

    public DamageMeterSnapshot CreateSnapshot()
    {
        return new DamageMeterSnapshot
        {
            SessionId = SessionId,
            InFight = InFight,
            SharedEnabled = SharedEnabled,
            CurrentRoundIndex = CurrentRoundIndex,
            CompletedRoundCount = CompletedRoundCount,
            ServerSequence = ServerSequence,
            BestHit = bestHit?.Copy(),
            Combatants = combatants.Values
                .OrderBy(stat => stat.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(CloneStat)
                .ToList()
        };
    }

    public bool ApplySnapshot(DamageMeterSnapshot snapshot)
    {
        if (snapshot == null || snapshot.ProtocolVersion != DamageMeterProtocol.Version)
        {
            return false;
        }

        var incomingSessionId = snapshot.SessionId ?? "";
        if (string.Equals(SessionId, incomingSessionId, StringComparison.Ordinal)
            && snapshot.ServerSequence < ServerSequence)
        {
            return false;
        }

        SessionId = incomingSessionId;
        InFight = snapshot.InFight;
        SharedEnabled = snapshot.SharedEnabled;
        CurrentRoundIndex = Math.Max(0, snapshot.CurrentRoundIndex);
        CompletedRoundCount = Math.Max(0, snapshot.CompletedRoundCount);
        ServerSequence = Math.Max(0, snapshot.ServerSequence);
        bestHit = CloneBestHit(snapshot.BestHit);
        combatants.Clear();

        foreach (var stat in snapshot.Combatants ?? new List<CombatantDamageStat>())
        {
            if (stat == null || string.IsNullOrWhiteSpace(stat.InstanceId))
            {
                continue;
            }

            var clone = CloneStat(stat);
            clone.Details = new Dictionary<string, DamageDetailStat>(
                clone.Details ?? new Dictionary<string, DamageDetailStat>(),
                StringComparer.OrdinalIgnoreCase);
            combatants[clone.InstanceId] = clone;
        }

        return true;
    }

    public IReadOnlyList<CombatantDamageStat> VisibleRows(
        bool friendlyOnly,
        bool includeUnknown,
        bool countShield,
        int maxRows)
    {
        IEnumerable<CombatantDamageStat> query = combatants.Values
            .Where(stat => stat.DisplayTotal(countShield) > 0);
        if (friendlyOnly)
        {
            query = query.Where(stat => stat.Team == DamageTeam.Friendly
                                        || includeUnknown && stat.Team == DamageTeam.Unknown);
        }
        else if (!includeUnknown)
        {
            query = query.Where(stat => stat.Team != DamageTeam.Unknown);
        }

        return query
            .OrderByDescending(stat => stat.DisplayTotal(countShield))
            .ThenByDescending(stat => stat.DisplayCurrentRound(countShield))
            .ThenBy(stat => stat.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxRows))
            .ToList();
    }

    public long DisplayGrandTotal(bool countShield, bool friendlyOnly, bool includeUnknown)
    {
        return combatants.Values
            .Where(stat => !friendlyOnly
                           || stat.Team == DamageTeam.Friendly
                           || includeUnknown && stat.Team == DamageTeam.Unknown)
            .Where(stat => includeUnknown || stat.Team != DamageTeam.Unknown)
            .Sum(stat => stat.DisplayTotal(countShield));
    }

    public DamageBestHitRecord? BestHit()
    {
        return bestHit?.Copy();
    }

    private void CloseCurrentRound()
    {
        foreach (var stat in combatants.Values)
        {
            stat.Rounds.Add(new DamageRoundStat
            {
                RoundIndex = CurrentRoundIndex,
                HpDamage = stat.CurrentRoundHpDamage,
                ShieldDamage = stat.CurrentRoundShieldDamage
            });
            if (stat.Rounds.Count > DamageMeterProtocol.MaxRoundsKept)
            {
                stat.Rounds.RemoveAt(0);
            }

            stat.CurrentRoundHpDamage = 0;
            stat.CurrentRoundShieldDamage = 0;
        }

        CompletedRoundCount++;
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
                || bestHit.Damage == amount && bestHit.ServerSequence <= damage.ServerSequence))
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
            CurrentRoundHpDamage = source.CurrentRoundHpDamage,
            CurrentRoundShieldDamage = source.CurrentRoundShieldDamage,
            Rounds = (source.Rounds ?? new List<DamageRoundStat>())
                .Select(round => new DamageRoundStat
                {
                    RoundIndex = round.RoundIndex,
                    HpDamage = round.HpDamage,
                    ShieldDamage = round.ShieldDamage
                })
                .ToList(),
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

        return new DamageBestHitRecord
        {
            RecordName = string.IsNullOrWhiteSpace(source.RecordName)
                ? DamageMeterRecordNames.BestHit
                : source.RecordName.Trim(),
            SessionId = source.SessionId ?? "",
            SourceInstanceId = source.SourceInstanceId ?? "",
            SourceDisplayName = source.SourceDisplayName ?? "",
            SourceTeam = source.SourceTeam,
            TargetInstanceId = source.TargetInstanceId ?? "",
            SourceDataId = source.SourceDataId ?? "",
            DetailLabel = source.DetailLabel ?? "",
            DamageType = source.DamageType ?? "",
            Damage = source.Damage,
            RoundIndex = Math.Max(0, source.RoundIndex),
            ServerSequence = Math.Max(0, source.ServerSequence),
            EventId = source.EventId ?? ""
        };
    }

    private static string NormalizeDisplayName(string value, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return string.IsNullOrWhiteSpace(result) ? "未知单位" : result;
    }
}
