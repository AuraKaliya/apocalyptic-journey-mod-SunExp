using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public static class OutOfRunDamageHistoryBuilder
{
    private static readonly IDamageMeterMvpEvaluator DefaultMvpEvaluator = new HighestDpsMvpEvaluator();

    public static OutOfRunDamageHistoryRecord Build(
        IEnumerable<DamageFightRecord> fights,
        OutOfRunDamageHistoryBuildRequest request,
        IDamageMeterMvpEvaluator? mvpEvaluator = null,
        bool countShield = true)
    {
        request ??= new OutOfRunDamageHistoryBuildRequest();
        var snapshots = (fights ?? Array.Empty<DamageFightRecord>())
            .Where(record => record?.Snapshot != null)
            .Select(record => record.Snapshot)
            .Where(snapshot => snapshot.ProtocolVersion == DamageMeterProtocol.Version)
            .ToList();

        var totalRounds = snapshots.Sum(snapshot => Math.Max(0, snapshot.CompletedRoundCount));
        var totals = new Dictionary<string, CombatantDamageStat>(StringComparer.OrdinalIgnoreCase);
        DamageBestHitRecord? bestHit = null;

        foreach (var snapshot in snapshots)
        {
            if (snapshot.BestHit != null
                && snapshot.BestHit.Damage > 0
                && (bestHit == null
                    || snapshot.BestHit.Damage > bestHit.Damage
                    || snapshot.BestHit.Damage == bestHit.Damage
                    && string.CompareOrdinal(snapshot.BestHit.EventId, bestHit.EventId) < 0))
            {
                bestHit = snapshot.BestHit.Copy();
            }

            foreach (var stat in snapshot.Combatants ?? new List<CombatantDamageStat>())
            {
                if (stat == null)
                {
                    continue;
                }

                var instanceId = stat.InstanceId ?? "";
                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    continue;
                }

                if (!totals.TryGetValue(instanceId, out var aggregate))
                {
                    aggregate = new CombatantDamageStat
                    {
                        InstanceId = instanceId,
                        DisplayName = stat.DisplayName ?? "",
                        Team = stat.Team
                    };
                    totals[instanceId] = aggregate;
                }

                if (!string.IsNullOrWhiteSpace(stat.DisplayName))
                {
                    aggregate.DisplayName = stat.DisplayName ?? "";
                }

                if (stat.Team != DamageTeam.Unknown)
                {
                    aggregate.Team = stat.Team;
                }

                aggregate.TotalHpDamage += stat.TotalHpDamage;
                aggregate.TotalShieldDamage += stat.TotalShieldDamage;
            }
        }

        return Build(
            new DamageRunAggregateSnapshot
            {
                AdventureId = request.AdventureId ?? "",
                TotalRounds = totalRounds,
                BestHit = bestHit?.Copy(),
                Combatants = totals.Values.ToList()
            },
            request,
            mvpEvaluator,
            countShield);
    }

    public static OutOfRunDamageHistoryRecord Build(
        DamageRunAggregateSnapshot aggregate,
        OutOfRunDamageHistoryBuildRequest request,
        IDamageMeterMvpEvaluator? mvpEvaluator = null,
        bool countShield = true)
    {
        request ??= new OutOfRunDamageHistoryBuildRequest();
        aggregate ??= new DamageRunAggregateSnapshot();
        var totalRounds = Math.Max(0, aggregate.TotalRounds);
        var aggregateSnapshot = new DamageMeterSnapshot
        {
            ProtocolVersion = DamageMeterProtocol.Version,
            SessionId = string.IsNullOrWhiteSpace(aggregate.AdventureId)
                ? request.AdventureId ?? ""
                : aggregate.AdventureId,
            CompletedRoundCount = totalRounds,
            BestHit = aggregate.BestHit?.Copy(),
            Combatants = aggregate.Combatants ?? new List<CombatantDamageStat>()
        };
        var mvp = (mvpEvaluator ?? DefaultMvpEvaluator).Evaluate(aggregateSnapshot, countShield);
        var teamTotal = aggregateSnapshot.Combatants
            .Where(stat => stat.Team == DamageTeam.Friendly)
            .Sum(stat => stat.DisplayTotal(countShield));
        var rounds = Math.Max(1, totalRounds);
        var members = BuildMembers(request.TeamMembers, aggregateSnapshot.Combatants, rounds, countShield);

        return new OutOfRunDamageHistoryRecord
        {
            AdventureId = request.AdventureId ?? "",
            ModeId = request.ModeId ?? "",
            ModeDisplayName = string.IsNullOrWhiteSpace(request.ModeDisplayName)
                ? request.ModeId ?? ""
                : request.ModeDisplayName.Trim(),
            Status = string.IsNullOrWhiteSpace(request.Status)
                ? OutOfRunDamageHistoryStatus.Failed
                : request.Status.Trim(),
            EndedUtc = request.EndedUtc ?? "",
            TeamMembers = members,
            BestHit = aggregateSnapshot.BestHit?.Copy(),
            TeamTotalDamage = teamTotal,
            TotalRounds = totalRounds,
            TeamDps = teamTotal / (double)rounds,
            Mvp = mvp
        };
    }

    private static List<OutOfRunTeamMemberSnapshot> BuildMembers(
        IReadOnlyList<OutOfRunTeamMemberSnapshot>? requested,
        IReadOnlyList<CombatantDamageStat> combatants,
        int rounds,
        bool countShield)
    {
        var byId = combatants.ToDictionary(item => item.InstanceId, StringComparer.OrdinalIgnoreCase);
        var result = new List<OutOfRunTeamMemberSnapshot>();
        foreach (var member in requested ?? Array.Empty<OutOfRunTeamMemberSnapshot>())
        {
            if (member == null || result.Count >= DamageMeterProtocol.MaxTeamMembers)
            {
                break;
            }

            byId.TryGetValue(member.InstanceId ?? "", out var stat);
            var total = stat?.DisplayTotal(countShield) ?? member.TotalDamage;
            result.Add(new OutOfRunTeamMemberSnapshot
            {
                InstanceId = member.InstanceId ?? "",
                PlayerId = string.IsNullOrWhiteSpace(member.PlayerId)
                    ? member.InstanceId ?? ""
                    : member.PlayerId ?? "",
                PlayerDisplayName = string.IsNullOrWhiteSpace(member.PlayerDisplayName)
                    ? member.DisplayName ?? ""
                    : member.PlayerDisplayName ?? "",
                RoleId = member.RoleId ?? "",
                RoleDisplayName = string.IsNullOrWhiteSpace(member.RoleDisplayName)
                    ? member.DisplayName ?? ""
                    : member.RoleDisplayName ?? "",
                DisplayName = member.DisplayName ?? "",
                AvatarPngBase64 = member.AvatarPngBase64 ?? "",
                AvatarSha256 = member.AvatarSha256 ?? "",
                TotalDamage = total,
                Dps = total / (double)Math.Max(1, rounds)
            });
        }

        foreach (var stat in combatants
                     .Where(item => item.Team == DamageTeam.Friendly)
                     .OrderByDescending(item => item.DisplayTotal(countShield)))
        {
            if (result.Count >= DamageMeterProtocol.MaxTeamMembers)
            {
                break;
            }

            if (result.Any(item => string.Equals(item.InstanceId, stat.InstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var total = stat.DisplayTotal(countShield);
            result.Add(new OutOfRunTeamMemberSnapshot
            {
                InstanceId = stat.InstanceId ?? "",
                PlayerId = stat.InstanceId ?? "",
                PlayerDisplayName = stat.InstanceId ?? "",
                RoleDisplayName = stat.DisplayName ?? "",
                DisplayName = stat.DisplayName ?? "",
                TotalDamage = total,
                Dps = total / (double)Math.Max(1, rounds)
            });
        }

        return result;
    }
}
