using System;
using System.Collections.Generic;
using System.Linq;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Host-authoritative logical seats for real players and projections. Spirits
/// are attachments and deliberately never enter this ledger.
/// </summary>
public static class FriendlyRoleSeatLedger
{
    public const int Capacity = FriendlyRoleSeatPolicy.Capacity;
    private const int ReservationSeconds = 30;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, SeatReservation> Reservations =
        new(StringComparer.Ordinal);

    public static void BeginBattle()
    {
        lock (SyncRoot)
        {
            Reservations.Clear();
        }
    }

    public static bool CanReserve(string ownerPlayerId, string ownerStatusId, out string reason)
    {
        lock (SyncRoot)
        {
            PruneExpired();
            return ValidateOwner(ownerPlayerId, ownerStatusId, out reason);
        }
    }

    public static bool TryReserve(
        string token,
        string ownerPlayerId,
        string ownerStatusId,
        int battleEpoch,
        out int slotIndex,
        out string reason)
    {
        slotIndex = -1;
        reason = "";
        if (string.IsNullOrWhiteSpace(token))
        {
            reason = "missing reservation token";
            return false;
        }

        lock (SyncRoot)
        {
            PruneExpired();
            if (Reservations.TryGetValue(token, out var existing))
            {
                slotIndex = existing.SlotIndex;
                return existing.BattleEpoch == battleEpoch
                       && string.Equals(existing.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal)
                       && string.Equals(existing.OwnerStatusId, ownerStatusId, StringComparison.Ordinal);
            }
            if (!ValidateOwner(ownerPlayerId, ownerStatusId, out reason))
            {
                return false;
            }

            slotIndex = OpenSlotUnsafe();
            if (slotIndex < 0)
            {
                reason = "friendly role seats are full";
                return false;
            }

            Reservations[token] = new SeatReservation(
                token,
                ownerPlayerId,
                ownerStatusId,
                slotIndex,
                battleEpoch,
                DateTime.UtcNow.AddSeconds(ReservationSeconds));
            return true;
        }
    }

    public static bool TryClaim(
        string token,
        string ownerPlayerId,
        string ownerStatusId,
        int battleEpoch,
        out int slotIndex)
    {
        slotIndex = -1;
        lock (SyncRoot)
        {
            PruneExpired();
            if (!Reservations.TryGetValue(token ?? "", out var reservation)
                || reservation.BattleEpoch != battleEpoch
                || !string.Equals(reservation.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal)
                || !string.Equals(reservation.OwnerStatusId, ownerStatusId, StringComparison.Ordinal))
            {
                return false;
            }

            Reservations.Remove(token ?? "");
            slotIndex = reservation.SlotIndex;
            return true;
        }
    }

    public static void Release(string? token)
    {
        lock (SyncRoot)
        {
            Reservations.Remove(token ?? "");
        }
    }

    public static int? FindOpenSeat()
    {
        lock (SyncRoot)
        {
            PruneExpired();
            var slot = OpenSlotUnsafe();
            return slot < 0 ? null : slot;
        }
    }

    private static bool ValidateOwner(string ownerPlayerId, string ownerStatusId, out string reason)
    {
        var duplicate = ProjectionStateStore.HasForOwner(ownerPlayerId, ownerStatusId)
                        || Reservations.Values.Any(value =>
                            (!string.IsNullOrWhiteSpace(ownerPlayerId)
                             && string.Equals(value.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal))
                            || (!string.IsNullOrWhiteSpace(ownerStatusId)
                                && string.Equals(value.OwnerStatusId, ownerStatusId, StringComparison.Ordinal)));
        if (duplicate)
        {
            reason = "owner already has projection";
            return false;
        }

        if (OpenSlotUnsafe() < 0)
        {
            reason = "friendly role seats are full";
            return false;
        }

        reason = "";
        return true;
    }

    private static int OpenSlotUnsafe()
    {
        return FriendlyRoleSeatPolicy.FindOpenSeat(
            RealPlayerCount(),
            ProjectionStateStore.Active().Select(projection => projection.SlotIndex),
            Reservations.Values.Select(reservation => reservation.SlotIndex));
    }

    private static int RealPlayerCount()
    {
        try
        {
            var count = FightManager.Instance?.roleQueue?.Count ?? 0;
            if (count > 0)
            {
                return Math.Min(Capacity, count);
            }
        }
        catch
        {
            // Fall through to the lobby count.
        }

        try
        {
            return Math.Max(1, Math.Min(Capacity, GameEntryUI.playerCount));
        }
        catch
        {
            return 1;
        }
    }

    private static void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var token in Reservations
                     .Where(pair => pair.Value.ExpiresAtUtc <= now
                                    || pair.Value.BattleEpoch != CompanionAuthorityService.BattleEpoch)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            Reservations.Remove(token);
        }
    }

    private sealed class SeatReservation
    {
        public SeatReservation(
            string token,
            string ownerPlayerId,
            string ownerStatusId,
            int slotIndex,
            int battleEpoch,
            DateTime expiresAtUtc)
        {
            Token = token;
            OwnerPlayerId = ownerPlayerId ?? "";
            OwnerStatusId = ownerStatusId ?? "";
            SlotIndex = slotIndex;
            BattleEpoch = battleEpoch;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string Token { get; }
        public string OwnerPlayerId { get; }
        public string OwnerStatusId { get; }
        public int SlotIndex { get; }
        public int BattleEpoch { get; }
        public DateTime ExpiresAtUtc { get; }
    }
}
