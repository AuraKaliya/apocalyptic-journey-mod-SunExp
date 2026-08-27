using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public enum SpiritInitialRosterAttemptStatus
{
    Unknown,
    Pending,
    Evaluating,
    Completed,
    Disabled,
    Blocked
}

public sealed class SpiritInitialRosterAttemptSnapshot
{
    public string ProfileKey { get; set; } = "";

    public SpiritInitialRosterAttemptStatus Status { get; set; }

    public long LastAttemptedCatalogEpoch { get; set; } = -1;

    public string Reason { get; set; } = "";
}

public sealed class SpiritInitialRosterAttemptLedger
{
    private readonly Dictionary<string, SpiritInitialRosterAttemptSnapshot> attempts =
        new(StringComparer.Ordinal);

    public bool IsTerminal(string profileKey)
    {
        var status = Snapshot(profileKey).Status;
        return status == SpiritInitialRosterAttemptStatus.Completed
               || status == SpiritInitialRosterAttemptStatus.Disabled
               || status == SpiritInitialRosterAttemptStatus.Blocked;
    }

    public bool TryBeginReadyAttempt(string profileKey, long catalogEpoch)
    {
        var attempt = GetOrCreate(profileKey);
        if (IsTerminal(profileKey)
            || attempt.Status == SpiritInitialRosterAttemptStatus.Evaluating
            || attempt.LastAttemptedCatalogEpoch == catalogEpoch)
        {
            return false;
        }

        attempt.Status = SpiritInitialRosterAttemptStatus.Evaluating;
        attempt.LastAttemptedCatalogEpoch = catalogEpoch;
        attempt.Reason = "";
        return true;
    }

    public bool MarkPending(string profileKey, string reason)
    {
        var attempt = GetOrCreate(profileKey);
        var normalized = reason ?? "initial roster dependency is not ready";
        var changed = attempt.Status != SpiritInitialRosterAttemptStatus.Pending
                      || !string.Equals(attempt.Reason, normalized, StringComparison.Ordinal);
        attempt.Status = SpiritInitialRosterAttemptStatus.Pending;
        attempt.Reason = normalized;
        return changed;
    }

    public void MarkCompleted(string profileKey, string reason)
    {
        MarkTerminal(profileKey, SpiritInitialRosterAttemptStatus.Completed, reason);
    }

    public void MarkDisabled(string profileKey, string reason)
    {
        MarkTerminal(profileKey, SpiritInitialRosterAttemptStatus.Disabled, reason);
    }

    public void MarkBlocked(string profileKey, string reason)
    {
        MarkTerminal(profileKey, SpiritInitialRosterAttemptStatus.Blocked, reason);
    }

    public IReadOnlyList<string> PendingProfileKeys()
    {
        return attempts
            .Where(pair => pair.Value.Status == SpiritInitialRosterAttemptStatus.Pending)
            .Select(pair => pair.Key)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public SpiritInitialRosterAttemptSnapshot Snapshot(string profileKey)
    {
        if (!attempts.TryGetValue(profileKey ?? "", out var attempt))
        {
            return new SpiritInitialRosterAttemptSnapshot
            {
                ProfileKey = profileKey ?? ""
            };
        }
        return Clone(attempt);
    }

    private SpiritInitialRosterAttemptSnapshot GetOrCreate(string profileKey)
    {
        var key = (profileKey ?? "").Trim();
        if (!attempts.TryGetValue(key, out var attempt))
        {
            attempt = new SpiritInitialRosterAttemptSnapshot { ProfileKey = key };
            attempts[key] = attempt;
        }
        return attempt;
    }

    private void MarkTerminal(
        string profileKey,
        SpiritInitialRosterAttemptStatus status,
        string reason)
    {
        var attempt = GetOrCreate(profileKey);
        attempt.Status = status;
        attempt.Reason = reason ?? "";
    }

    private static SpiritInitialRosterAttemptSnapshot Clone(SpiritInitialRosterAttemptSnapshot source)
    {
        return new SpiritInitialRosterAttemptSnapshot
        {
            ProfileKey = source.ProfileKey,
            Status = source.Status,
            LastAttemptedCatalogEpoch = source.LastAttemptedCatalogEpoch,
            Reason = source.Reason
        };
    }
}
