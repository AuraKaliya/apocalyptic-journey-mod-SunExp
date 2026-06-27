using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public sealed class OutOfRunDamageHistoryStore
{
    private readonly List<OutOfRunDamageHistoryRecord> records = new();

    public IReadOnlyList<OutOfRunDamageHistoryRecord> Records => records;

    public bool Add(OutOfRunDamageHistoryRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.AdventureId))
        {
            return false;
        }

        if (records.Any(item => string.Equals(item.AdventureId, record.AdventureId, StringComparison.Ordinal)))
        {
            return false;
        }

        var clone = Clone(record);
        clone.Sequence = records.Count == 0 ? 1 : records.Max(item => item.Sequence) + 1;
        records.Add(clone);
        Trim();
        return true;
    }

    public void Clear()
    {
        records.Clear();
    }

    public OutOfRunDamageHistoryFile CreateFile()
    {
        return new OutOfRunDamageHistoryFile
        {
            Records = records.Select(Clone).ToList()
        };
    }

    public void ApplyFile(OutOfRunDamageHistoryFile? file)
    {
        records.Clear();
        foreach (var record in file?.Records ?? new List<OutOfRunDamageHistoryRecord>())
        {
            if (record == null || string.IsNullOrWhiteSpace(record.AdventureId))
            {
                continue;
            }

            records.Add(Clone(record));
        }

        Trim();
    }

    private void Trim()
    {
        records.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
        if (records.Count > DamageMeterProtocol.MaxOutOfRunHistory)
        {
            records.RemoveRange(0, records.Count - DamageMeterProtocol.MaxOutOfRunHistory);
        }
    }

    private static OutOfRunDamageHistoryRecord Clone(OutOfRunDamageHistoryRecord source)
    {
        return new OutOfRunDamageHistoryRecord
        {
            Sequence = source.Sequence,
            AdventureId = source.AdventureId ?? "",
            ModeId = source.ModeId ?? "",
            ModeDisplayName = source.ModeDisplayName ?? "",
            Status = source.Status ?? "",
            EndedUtc = source.EndedUtc ?? "",
            TeamMembers = (source.TeamMembers ?? new List<OutOfRunTeamMemberSnapshot>())
                .Take(DamageMeterProtocol.MaxTeamMembers)
                .Select(CloneMember)
                .ToList(),
            BestHit = source.BestHit?.Copy(),
            TeamTotalDamage = source.TeamTotalDamage,
            TotalRounds = Math.Max(0, source.TotalRounds),
            TeamDps = Math.Max(0d, source.TeamDps),
            Mvp = new DamageMeterMvpResult
            {
                InstanceId = source.Mvp?.InstanceId ?? "",
                DisplayName = source.Mvp?.DisplayName ?? "",
                TotalDamage = source.Mvp?.TotalDamage ?? 0,
                Dps = source.Mvp?.Dps ?? 0d
            }
        };
    }

    private static OutOfRunTeamMemberSnapshot CloneMember(OutOfRunTeamMemberSnapshot source)
    {
        return new OutOfRunTeamMemberSnapshot
        {
            InstanceId = source.InstanceId ?? "",
            PlayerId = source.PlayerId ?? "",
            PlayerDisplayName = source.PlayerDisplayName ?? "",
            RoleId = source.RoleId ?? "",
            RoleDisplayName = source.RoleDisplayName ?? "",
            DisplayName = source.DisplayName ?? "",
            AvatarPngBase64 = source.AvatarPngBase64 ?? "",
            AvatarSha256 = source.AvatarSha256 ?? "",
            TotalDamage = source.TotalDamage,
            Dps = source.Dps
        };
    }
}
