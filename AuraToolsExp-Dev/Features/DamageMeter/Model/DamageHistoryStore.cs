using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public sealed class DamageHistoryStore
{
    private const int RecentCacheCapacity = 30;
    private readonly List<DamageFightRecord> records = new();

    public IReadOnlyList<DamageFightRecord> Records => records;

    public int TotalCount { get; private set; }

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
        TotalCount = records.Count;
        return true;
    }

    public void ArchiveRecent(DamageFightRecord record, int totalCount)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.SessionId))
        {
            return;
        }

        records.RemoveAll(item => string.Equals(item.SessionId, record.SessionId, StringComparison.Ordinal));
        records.Add(CloneRecord(record));
        records.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
        TrimRecent();
        TotalCount = Math.Max(records.Count, totalCount);
    }

    public void Clear()
    {
        records.Clear();
        TotalCount = 0;
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

        TotalCount = records.Count;
    }

    public void ApplyRecent(IEnumerable<DamageFightRecord>? incoming, int totalCount)
    {
        ApplySnapshot(incoming);
        TrimRecent();
        TotalCount = Math.Max(records.Count, totalCount);
    }

    private void TrimRecent()
    {
        if (records.Count > RecentCacheCapacity)
        {
            records.RemoveRange(0, records.Count - RecentCacheCapacity);
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
            BestHit = source.BestHit?.Copy(),
            Combatants = source.Combatants ?? new List<CombatantDamageStat>()
        };
        return ledger.ApplySnapshot(cloneSource)
            ? ledger.CreateSnapshot()
            : new DamageMeterSnapshot();
    }
}
