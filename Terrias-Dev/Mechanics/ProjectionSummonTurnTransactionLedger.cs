using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public enum ProjectionSummonTurnTransactionState
{
    Reserved = 1,
    Ready = 2,
    Failed = 3,
    Completed = 4
}

public sealed class ProjectionSummonTurnTransaction
{
    public string Token { get; set; } = "";
    public int RoundSequence { get; set; }
    public long Order { get; set; }
    public long Revision { get; set; }
    public ProjectionSummonTurnTransactionState State { get; set; }
    public string StatusId { get; set; } = "";
    public string Generation { get; set; } = "";
    public string Detail { get; set; } = "";

    public bool IsOpen => State is ProjectionSummonTurnTransactionState.Reserved
        or ProjectionSummonTurnTransactionState.Ready;

    public bool IsTerminal => State is ProjectionSummonTurnTransactionState.Failed
        or ProjectionSummonTurnTransactionState.Completed;

    internal bool Claimed { get; set; }

    public ProjectionSummonTurnTransaction Clone()
    {
        return new ProjectionSummonTurnTransaction
        {
            Token = Token,
            RoundSequence = RoundSequence,
            Order = Order,
            Revision = Revision,
            State = State,
            StatusId = StatusId,
            Generation = Generation,
            Detail = Detail,
            Claimed = Claimed
        };
    }
}

/// <summary>
/// Battle-scoped authoritative transaction ledger for projections summoned
/// during a player round. Reservation happens before the projection GameObject
/// is spawned, so player-turn completion can hold the native action coroutine
/// until every accepted summon becomes Ready or Failed.
/// </summary>
public sealed class ProjectionSummonTurnTransactionLedger
{
    private readonly Dictionary<string, ProjectionSummonTurnTransaction> entries =
        new(StringComparer.Ordinal);
    private long nextOrder;
    private int activeRound;

    public int ActiveRound => activeRound;

    public int OpenCount(int round)
    {
        return entries.Values.Count(entry => entry.RoundSequence == round && entry.IsOpen);
    }

    public ProjectionSummonTurnTransaction? Reserve(
        string token,
        int round,
        out string reason)
    {
        var id = (token ?? "").Trim();
        if (id.Length == 0)
        {
            reason = "projection summon turn token is missing";
            return null;
        }
        if (round <= 0 || activeRound != round)
        {
            reason = "projection summon turn round is not active";
            return null;
        }
        if (entries.TryGetValue(id, out var existing))
        {
            if (existing.RoundSequence == round)
            {
                reason = "";
                return existing.Clone();
            }

            reason = "projection summon turn token belongs to another round";
            return null;
        }

        var transaction = new ProjectionSummonTurnTransaction
        {
            Token = id,
            RoundSequence = round,
            Order = ++nextOrder,
            Revision = 1L,
            State = ProjectionSummonTurnTransactionState.Reserved
        };
        entries[id] = transaction;
        reason = "";
        return transaction.Clone();
    }

    public bool TryMarkReady(
        string token,
        string statusId,
        string generation,
        out ProjectionSummonTurnTransaction snapshot,
        out string reason)
    {
        snapshot = new ProjectionSummonTurnTransaction();
        if (!TryMutable(token, out var entry, out reason)) return false;
        if (entry.State == ProjectionSummonTurnTransactionState.Ready)
        {
            if (!string.Equals(entry.StatusId, statusId ?? "", StringComparison.Ordinal)
                || !string.Equals(entry.Generation, generation ?? "", StringComparison.Ordinal))
            {
                reason = "projection summon turn ready identity changed";
                return false;
            }
            snapshot = entry.Clone();
            reason = "";
            return true;
        }
        if (entry.State != ProjectionSummonTurnTransactionState.Reserved
            || string.IsNullOrWhiteSpace(statusId)
            || string.IsNullOrWhiteSpace(generation))
        {
            reason = "projection summon turn cannot become ready";
            return false;
        }

        entry.State = ProjectionSummonTurnTransactionState.Ready;
        entry.StatusId = statusId.Trim();
        entry.Generation = generation.Trim();
        entry.Detail = "";
        entry.Revision++;
        entry.Claimed = false;
        snapshot = entry.Clone();
        reason = "";
        return true;
    }

    public bool TryMarkFailed(
        string token,
        string detail,
        out ProjectionSummonTurnTransaction snapshot,
        out string reason)
    {
        snapshot = new ProjectionSummonTurnTransaction();
        if (!TryMutable(token, out var entry, out reason)) return false;
        if (entry.State == ProjectionSummonTurnTransactionState.Failed)
        {
            snapshot = entry.Clone();
            reason = "";
            return true;
        }
        if (entry.IsTerminal)
        {
            reason = "projection summon turn is already terminal";
            return false;
        }

        entry.State = ProjectionSummonTurnTransactionState.Failed;
        entry.Detail = detail ?? "";
        entry.Revision++;
        entry.Claimed = false;
        snapshot = entry.Clone();
        reason = "";
        return true;
    }

    public bool TryMarkCompleted(
        string token,
        out ProjectionSummonTurnTransaction snapshot,
        out string reason)
    {
        snapshot = new ProjectionSummonTurnTransaction();
        if (!TryMutable(token, out var entry, out reason)) return false;
        if (entry.State == ProjectionSummonTurnTransactionState.Completed)
        {
            snapshot = entry.Clone();
            reason = "";
            return true;
        }
        if (entry.State != ProjectionSummonTurnTransactionState.Ready)
        {
            reason = "projection summon turn is not ready to complete";
            return false;
        }

        entry.State = ProjectionSummonTurnTransactionState.Completed;
        entry.Detail = "";
        entry.Revision++;
        entry.Claimed = false;
        snapshot = entry.Clone();
        reason = "";
        return true;
    }

    public bool TryClaimReady(
        int round,
        out ProjectionSummonTurnTransaction transaction)
    {
        transaction = new ProjectionSummonTurnTransaction();
        var entry = entries.Values
            .Where(value => value.RoundSequence == round
                            && value.State == ProjectionSummonTurnTransactionState.Ready
                            && !value.Claimed)
            .OrderBy(value => value.Order)
            .ThenBy(value => value.Token, StringComparer.Ordinal)
            .FirstOrDefault();
        if (entry == null) return false;
        entry.Claimed = true;
        transaction = entry.Clone();
        return true;
    }

    public void ReleaseClaim(string token)
    {
        if (entries.TryGetValue((token ?? "").Trim(), out var entry))
        {
            entry.Claimed = false;
        }
    }

    public IReadOnlyList<ProjectionSummonTurnTransaction> BeginRound(int round)
    {
        if (round <= 0) return Array.Empty<ProjectionSummonTurnTransaction>();
        var staleOpen = entries.Values
            .Where(entry => entry.RoundSequence < round && entry.IsOpen)
            .OrderBy(entry => entry.RoundSequence)
            .ThenBy(entry => entry.Order)
            .Select(entry => entry.Clone())
            .ToArray();
        foreach (var token in entries.Values
                     .Where(entry => entry.RoundSequence < round)
                     .Select(entry => entry.Token)
                     .ToArray())
        {
            entries.Remove(token);
        }
        activeRound = round;
        return staleOpen;
    }

    public bool TryApplyAuthoritative(
        ProjectionSummonTurnTransaction? snapshot,
        out string reason)
    {
        if (snapshot == null
            || string.IsNullOrWhiteSpace(snapshot.Token)
            || snapshot.RoundSequence <= 0
            || snapshot.Order <= 0
            || snapshot.Revision <= 0
            || !Enum.IsDefined(typeof(ProjectionSummonTurnTransactionState), snapshot.State))
        {
            reason = "projection summon turn snapshot is invalid";
            return false;
        }
        if ((snapshot.State is ProjectionSummonTurnTransactionState.Ready
                or ProjectionSummonTurnTransactionState.Completed)
            && (string.IsNullOrWhiteSpace(snapshot.StatusId)
                || string.IsNullOrWhiteSpace(snapshot.Generation)))
        {
            reason = "projection summon turn ready snapshot is missing actor identity";
            return false;
        }
        if (activeRound > 0 && snapshot.RoundSequence < activeRound)
        {
            reason = "projection summon turn snapshot is stale";
            return false;
        }

        var token = snapshot.Token.Trim();
        if (!entries.TryGetValue(token, out var entry))
        {
            entries[token] = snapshot.Clone();
            entries[token].Claimed = false;
            nextOrder = Math.Max(nextOrder, snapshot.Order);
            reason = "";
            return true;
        }
        if (entry.RoundSequence != snapshot.RoundSequence
            || entry.Order != snapshot.Order)
        {
            reason = "projection summon turn snapshot identity changed";
            return false;
        }
        if (snapshot.Revision < entry.Revision)
        {
            reason = "projection summon turn snapshot is stale";
            return false;
        }
        if (snapshot.Revision == entry.Revision)
        {
            if (entry.State != snapshot.State
                || !string.Equals(entry.StatusId, snapshot.StatusId ?? "", StringComparison.Ordinal)
                || !string.Equals(entry.Generation, snapshot.Generation ?? "", StringComparison.Ordinal)
                || !string.Equals(entry.Detail, snapshot.Detail ?? "", StringComparison.Ordinal))
            {
                reason = "projection summon turn snapshot conflicts at the same revision";
                return false;
            }
            reason = "";
            return true;
        }
        if (!CanAdvance(entry.State, snapshot.State))
        {
            reason = "projection summon turn snapshot regressed";
            return false;
        }

        entry.Revision = snapshot.Revision;
        entry.State = snapshot.State;
        entry.StatusId = snapshot.StatusId ?? "";
        entry.Generation = snapshot.Generation ?? "";
        entry.Detail = snapshot.Detail ?? "";
        entry.Claimed = false;
        reason = "";
        return true;
    }

    public IReadOnlyList<ProjectionSummonTurnTransaction> Snapshot(int round)
    {
        return entries.Values
            .Where(entry => entry.RoundSequence == round)
            .OrderBy(entry => entry.Order)
            .ThenBy(entry => entry.Token, StringComparer.Ordinal)
            .Select(entry => entry.Clone())
            .ToArray();
    }

    public bool TryGet(string token, out ProjectionSummonTurnTransaction transaction)
    {
        transaction = new ProjectionSummonTurnTransaction();
        if (!entries.TryGetValue((token ?? "").Trim(), out var entry)) return false;
        transaction = entry.Clone();
        return true;
    }

    public void Clear()
    {
        entries.Clear();
        nextOrder = 0L;
        activeRound = 0;
    }

    private bool TryMutable(
        string token,
        out ProjectionSummonTurnTransaction entry,
        out string reason)
    {
        if (!entries.TryGetValue((token ?? "").Trim(), out var found))
        {
            entry = new ProjectionSummonTurnTransaction();
            reason = "projection summon turn transaction is missing";
            return false;
        }
        entry = found;
        reason = "";
        return true;
    }

    private static bool CanAdvance(
        ProjectionSummonTurnTransactionState current,
        ProjectionSummonTurnTransactionState next)
    {
        return current switch
        {
            ProjectionSummonTurnTransactionState.Reserved =>
                next is ProjectionSummonTurnTransactionState.Ready
                    or ProjectionSummonTurnTransactionState.Failed,
            ProjectionSummonTurnTransactionState.Ready =>
                next is ProjectionSummonTurnTransactionState.Completed
                    or ProjectionSummonTurnTransactionState.Failed,
            ProjectionSummonTurnTransactionState.Failed =>
                next == ProjectionSummonTurnTransactionState.Failed,
            ProjectionSummonTurnTransactionState.Completed =>
                next == ProjectionSummonTurnTransactionState.Completed,
            _ => false
        };
    }
}
