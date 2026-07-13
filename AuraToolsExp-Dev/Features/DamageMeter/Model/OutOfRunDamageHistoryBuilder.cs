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
        var rounds = Math.Max(1, totalRounds);
        var members = BuildMembers(request.TeamMembers, aggregateSnapshot.Combatants, rounds, countShield);
        var settlementSnapshot = new DamageMeterSnapshot
        {
            ProtocolVersion = aggregateSnapshot.ProtocolVersion,
            SessionId = aggregateSnapshot.SessionId,
            CompletedRoundCount = aggregateSnapshot.CompletedRoundCount,
            BestHit = aggregateSnapshot.BestHit?.Copy(),
            Combatants = members.Select(member => new CombatantDamageStat
            {
                InstanceId = member.InstanceId ?? "",
                DisplayName = member.RoleDisplayName ?? "",
                Team = DamageTeam.Friendly,
                TotalHpDamage = Math.Max(0, member.TotalDamage)
            }).ToList()
        };
        var mvp = (mvpEvaluator ?? DefaultMvpEvaluator).Evaluate(settlementSnapshot, countShield: false);
        var teamTotal = members.Sum(member => Math.Max(0, member.TotalDamage));

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
        var result = new List<OutOfRunTeamMemberSnapshot>();
        var seenPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in requested ?? Array.Empty<OutOfRunTeamMemberSnapshot>())
        {
            if (result.Count >= DamageMeterProtocol.MaxTeamMembers)
            {
                break;
            }

            if (member == null
                || string.IsNullOrWhiteSpace(member.PlayerId)
                || !seenPlayers.Add(member.PlayerId!))
            {
                continue;
            }

            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                member.InstanceId ?? "",
                member.PlayerId ?? ""
            };
            var matched = combatants
                .Where(stat => stat != null && aliases.Contains(stat.InstanceId ?? ""))
                .ToList();
            var total = matched.Count > 0
                ? matched.Sum(stat => stat.DisplayTotal(countShield))
                : member.TotalDamage;
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

        return result;
    }

    private static string FallbackDisplayName(CombatantDamageStat stat)
    {
        var instanceId = stat?.InstanceId?.Trim() ?? "";
        var displayName = stat?.DisplayName?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(displayName)
               || string.Equals(displayName, instanceId, StringComparison.OrdinalIgnoreCase)
            ? "未知玩家"
            : displayName;
    }
}
