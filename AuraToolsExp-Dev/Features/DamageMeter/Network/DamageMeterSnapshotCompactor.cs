using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;
internal static class DamageMeterSnapshotCompactor
{
    private const int NetworkSnapshotSoftLimitBytes = AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes;
    private const int NetworkDetailsSoftLimit = 24;
    private const int NetworkRoundsSoftLimit = 32;
    private const int NetworkCombatantsSoftLimit = 16;
    private const int NetworkMinimalCombatantsLimit = 8;

    internal static void CompactNetworkSnapshot(DamageMeterSnapshot snapshot, string source)
    {
        if (snapshot == null || SnapshotFits(snapshot))
        {
            return;
        }

        var beforeBytes = EstimateSnapshotBytes(snapshot);
        TrimDetailsAndRounds(snapshot, NetworkDetailsSoftLimit, NetworkRoundsSoftLimit);
        if (SnapshotFits(snapshot))
        {
            LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
            return;
        }

        TrimCombatants(snapshot, NetworkCombatantsSoftLimit);
        if (SnapshotFits(snapshot))
        {
            LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
            return;
        }

        TrimDetailsAndRounds(snapshot, maxDetails: 8, maxRounds: 12);
        if (SnapshotFits(snapshot))
        {
            LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
            return;
        }

        MinimizeNetworkSnapshot(snapshot);
        LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
    }

    internal static void MinimizeNetworkSnapshot(DamageMeterSnapshot snapshot)
    {
        TrimCombatants(snapshot, NetworkMinimalCombatantsLimit);
        TrimDetailsAndRounds(snapshot, maxDetails: 0, maxRounds: 0);
    }

    internal static DamageMeterSnapshot CreateStatusOnlySnapshot(DamageMeterSnapshot? source)
    {
        return new DamageMeterSnapshot
        {
            ProtocolVersion = DamageMeterProtocol.Version,
            SessionId = source?.SessionId ?? DamageMeterNetworkRuntime.Ledger.SessionId,
            InFight = source?.InFight ?? DamageMeterNetworkRuntime.Ledger.InFight,
            SharedEnabled = source?.SharedEnabled ?? DamageMeterNetworkRuntime.Ledger.SharedEnabled,
            CurrentRoundIndex = source?.CurrentRoundIndex ?? DamageMeterNetworkRuntime.Ledger.CurrentRoundIndex,
            CompletedRoundCount = source?.CompletedRoundCount ?? DamageMeterNetworkRuntime.Ledger.CompletedRoundCount,
            ServerSequence = source?.ServerSequence ?? DamageMeterNetworkRuntime.Ledger.ServerSequence,
            RunAggregate = CreateStatusOnlyAggregate(source?.RunAggregate),
            Combatants = new List<CombatantDamageStat>()
        };
    }

    internal static DamageRunAggregateSnapshot CreateStatusOnlyAggregate(DamageRunAggregateSnapshot? source)
    {
        return new DamageRunAggregateSnapshot
        {
            AdventureId = source?.AdventureId ?? DamageMeterNetworkRuntime.RunAggregate.AdventureId,
            StartedUtc = source?.StartedUtc ?? DamageMeterNetworkRuntime.RunAggregate.StartedUtc,
            UpdatedUtc = source?.UpdatedUtc ?? DamageMeterNetworkRuntime.RunAggregate.UpdatedUtc,
            EncounterCount = source?.EncounterCount ?? DamageMeterNetworkRuntime.RunAggregate.EncounterCount,
            TotalRounds = source?.TotalRounds ?? DamageMeterNetworkRuntime.RunAggregate.TotalRounds,
            ConfirmedEventCount = source?.ConfirmedEventCount ?? DamageMeterNetworkRuntime.RunAggregate.ConfirmedEventCount,
            LastSessionId = source?.LastSessionId ?? DamageMeterNetworkRuntime.RunAggregate.LastSessionId,
            LastServerSequence = source?.LastServerSequence ?? DamageMeterNetworkRuntime.RunAggregate.LastServerSequence,
            BestHit = source?.BestHit?.Copy(),
            Combatants = new List<CombatantDamageStat>()
        };
    }

    internal static bool SnapshotFits(DamageMeterSnapshot snapshot)
    {
        return !AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(snapshot, out var bytes, out _)
               || bytes <= NetworkSnapshotSoftLimitBytes;
    }

    internal static int EstimateSnapshotBytes(DamageMeterSnapshot snapshot)
    {
        return AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(snapshot, out var bytes, out _)
            ? bytes
            : 0;
    }

    internal static void TrimCombatants(DamageMeterSnapshot snapshot, int maximum)
    {
        snapshot.Combatants = (snapshot.Combatants ?? new List<CombatantDamageStat>())
            .Where(stat => stat != null)
            .OrderByDescending(stat => stat.DisplayTotal(true))
            .ThenBy(stat => stat.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maximum))
            .ToList();
    }

    internal static void TrimDetailsAndRounds(DamageMeterSnapshot snapshot, int maxDetails, int maxRounds)
    {
        foreach (var stat in snapshot.Combatants ?? new List<CombatantDamageStat>())
        {
            if (stat == null)
            {
                continue;
            }

            stat.Rounds = maxRounds <= 0
                ? new List<DamageRoundStat>()
                : (stat.Rounds ?? new List<DamageRoundStat>())
                    .Skip(Math.Max(0, (stat.Rounds?.Count ?? 0) - maxRounds))
                    .ToList();

            stat.Details = maxDetails <= 0
                ? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase)
                : (stat.Details ?? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(pair => (pair.Value?.HpDamage ?? 0) + (pair.Value?.ShieldDamage ?? 0))
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(maxDetails)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        TrimAggregateDetails(snapshot.RunAggregate, maxDetails);
    }

    internal static void TrimAggregateDetails(DamageRunAggregateSnapshot? aggregate, int maxDetails)
    {
        if (aggregate == null)
        {
            return;
        }

        foreach (var stat in aggregate.Combatants ?? new List<CombatantDamageStat>())
        {
            if (stat == null)
            {
                continue;
            }

            stat.Rounds = new List<DamageRoundStat>();
            stat.CurrentRoundHpDamage = 0;
            stat.CurrentRoundShieldDamage = 0;
            stat.Details = maxDetails <= 0
                ? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase)
                : (stat.Details ?? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(pair => (pair.Value?.HpDamage ?? 0) + (pair.Value?.ShieldDamage ?? 0))
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(maxDetails)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static void LogSnapshotCompacted(string source, int beforeBytes, int afterBytes)
    {
        AuraToolsLog.Warn("[DamageMeter] compacted network snapshot. source="
                          + source
                          + ", bytes="
                          + beforeBytes
                          + "->"
                          + afterBytes
                          + ", softLimit="
                          + NetworkSnapshotSoftLimitBytes);
    }

}
