using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public sealed class DamageHistoryStore
{
    private readonly List<DamageFightRecord> records = new();

    public IReadOnlyList<DamageFightRecord> Records => records;

    public bool Archive(DamageMeterSnapshot snapshot, string result, string endedUtc)
    {
        if (snapshot == null
            || snapshot.InFight
            || string.IsNullOrWhiteSpace(snapshot.SessionId)
            || records.Any(record => string.Equals(
                record.SessionId,
                snapshot.SessionId,
                StringComparison.Ordinal)))
        {
            return false;
        }

        records.Add(new DamageFightRecord
        {
            Sequence = records.Count == 0 ? 1 : records.Max(record => record.Sequence) + 1,
            SessionId = snapshot.SessionId,
            Result = string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim(),
            EndedUtc = endedUtc ?? "",
            Snapshot = CloneSnapshot(snapshot)
        });
        Trim();
        return true;
    }

    public void Clear()
    {
        records.Clear();
    }

    public List<DamageFightRecord> CreateSnapshot()
    {
        return records.Select(CloneRecord).ToList();
    }

    public void ApplySnapshot(IEnumerable<DamageFightRecord>? incoming)
    {
        records.Clear();
        if (incoming != null)
        {
            foreach (var record in incoming
                         .Where(record => record != null && !string.IsNullOrWhiteSpace(record.SessionId))
                         .OrderBy(record => record.Sequence)
                         .ThenBy(record => record.EndedUtc, StringComparer.Ordinal)
                         .GroupBy(record => record.SessionId, StringComparer.Ordinal)
                         .Select(group => group.Last()))
            {
                records.Add(CloneRecord(record));
            }
        }

        Trim();
    }

    private void Trim()
    {
        if (records.Count > DamageMeterProtocol.MaxFightHistory)
        {
            records.RemoveRange(0, records.Count - DamageMeterProtocol.MaxFightHistory);
        }
    }

    private static DamageFightRecord CloneRecord(DamageFightRecord source)
    {
        return new DamageFightRecord
        {
            Sequence = source.Sequence,
            SessionId = source.SessionId ?? "",
            Result = source.Result ?? "",
            EndedUtc = source.EndedUtc ?? "",
            Snapshot = CloneSnapshot(source.Snapshot)
        };
    }

    private static DamageMeterSnapshot CloneSnapshot(DamageMeterSnapshot? source)
    {
        if (source == null)
        {
            return new DamageMeterSnapshot();
        }

        var ledger = new DamageLedger();
        var cloneSource = new DamageMeterSnapshot
        {
            ProtocolVersion = source.ProtocolVersion,
            SessionId = source.SessionId ?? "",
            InFight = source.InFight,
            SharedEnabled = source.SharedEnabled,
            CurrentRoundIndex = source.CurrentRoundIndex,
            CompletedRoundCount = source.CompletedRoundCount,
            ServerSequence = source.ServerSequence,
            Combatants = source.Combatants ?? new List<CombatantDamageStat>()
        };
        return ledger.ApplySnapshot(cloneSource)
            ? ledger.CreateSnapshot()
            : new DamageMeterSnapshot();
    }
}
