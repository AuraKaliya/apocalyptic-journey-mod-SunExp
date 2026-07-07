using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;

namespace SunExp.Dll.Mechanics;

public sealed class WunaEmberSnapshot
{
    public string OwnerPlayerId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public int Level { get; set; }

    public int Sequence { get; set; }

    public string Source { get; set; } = "";
}

public static class WunaEmberSyncService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, int> LastSequences = new(StringComparer.Ordinal);
    private static int localSequence;

    public static int GetStored(IStatusManager? status)
    {
        var ownerPlayerId = ResolveOwnerPlayerId(status);
        var ownerStatusId = ResolveOwnerStatusId(status);
        var ownerValue = ReadOwnerValue(ownerPlayerId);
        if (ownerValue >= 0)
        {
            return Clamp(ownerValue);
        }

        ownerValue = ReadOwnerValue(ownerStatusId);
        if (ownerValue >= 0)
        {
            return Clamp(ownerValue);
        }

        return Clamp(DictionaryUtil.ParseInt(
            PlayerApi.GetScopedGameVar(
                SunExpIds.WunaPersistentEmber,
                status,
                "0",
                migrateLegacyWhenSolo: true)));
    }

    public static int CommitLocal(IStatusManager? status, int level, string source)
    {
        var safeSource = source ?? "";
        var snapshot = new WunaEmberSnapshot
        {
            OwnerPlayerId = ResolveOwnerPlayerId(status),
            OwnerStatusId = ResolveOwnerStatusId(status),
            Level = Clamp(level),
            Sequence = NextSequence(),
            Source = safeSource
        };

        ApplySnapshot(snapshot, "local:" + safeSource);
        if (!SunExpNetworkRuntime.IsMultiplayerSession())
        {
            return snapshot.Level;
        }

        if (SunExpNetworkRuntime.IsClientOnly())
        {
            SunExpNetworkRuntime.Send(new RpcWunaEmberCommit(snapshot), safeSource);
            return snapshot.Level;
        }

        RpcWunaEmberCommit.ApplyOnServer(snapshot, SunExpRpcAuthorityRuntime.CreateLocalServerSender(safeSource), false);
        SunExpNetworkRuntime.Send(new RpcWunaEmberCommit(snapshot)
        {
            Accepted = true
        }, safeSource);
        return snapshot.Level;
    }

    public static bool ApplySnapshot(WunaEmberSnapshot? snapshot, string source)
    {
        if (snapshot == null)
        {
            return false;
        }

        snapshot.Level = Clamp(snapshot.Level);
        snapshot.OwnerPlayerId = NormalizeId(snapshot.OwnerPlayerId);
        snapshot.OwnerStatusId = NormalizeId(snapshot.OwnerStatusId);
        var ownerKey = OwnerKey(snapshot);
        if (ownerKey.Length == 0)
        {
            return false;
        }

        lock (SyncRoot)
        {
            if (snapshot.Sequence > 0
                && LastSequences.TryGetValue(ownerKey, out var previous)
                && snapshot.Sequence < previous)
            {
                SunExpLog.Debug("[WunaEmberSync] stale snapshot ignored from "
                    + source
                    + "; owner="
                    + ownerKey
                    + "; seq="
                    + snapshot.Sequence
                    + "; previous="
                    + previous
                    + ".");
                return false;
            }

            if (snapshot.Sequence > 0)
            {
                LastSequences[ownerKey] = snapshot.Sequence;
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.OwnerPlayerId))
        {
            PlayerApi.SetGameVar(OwnerGameVarKey(snapshot.OwnerPlayerId), snapshot.Level.ToString());
        }

        if (!string.IsNullOrWhiteSpace(snapshot.OwnerStatusId))
        {
            PlayerApi.SetGameVar(OwnerGameVarKey(snapshot.OwnerStatusId), snapshot.Level.ToString());
        }

        if (IsLocalOwner(snapshot))
        {
            PlayerApi.SetScopedGameVar(SunExpIds.WunaPersistentEmber, FightPlayer.Instance?.Status, snapshot.Level.ToString());
        }

        SunExpLog.Info("[WunaEmberSync] saved owner="
            + ownerKey
            + "; level="
            + snapshot.Level
            + "; seq="
            + snapshot.Sequence
            + "; source="
            + source
            + ".");
        return true;
    }

    private static bool IsLocalOwner(WunaEmberSnapshot snapshot)
    {
        return SunExpNetworkRuntime.IsLocalPlayer(snapshot.OwnerPlayerId)
            || string.Equals(snapshot.OwnerStatusId, PlayerApi.LocalPlayerStatusId(), StringComparison.Ordinal);
    }

    private static string ResolveOwnerPlayerId(IStatusManager? status)
    {
        var playerId = SunExpNetworkRuntime.LocalPlayerId();
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return NormalizeId(playerId);
        }

        return ResolveOwnerStatusId(status);
    }

    private static string ResolveOwnerStatusId(IStatusManager? status)
    {
        var id = status?.InstanceId ?? PlayerApi.LocalPlayerStatusId();
        return NormalizeId(id);
    }

    private static int NextSequence()
    {
        lock (SyncRoot)
        {
            localSequence++;
            if (localSequence <= 0)
            {
                localSequence = 1;
            }

            return localSequence;
        }
    }

    private static string OwnerKey(WunaEmberSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.OwnerPlayerId)
            ? snapshot.OwnerPlayerId
            : snapshot.OwnerStatusId;
    }

    private static int ReadOwnerValue(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return -1;
        }

        var value = PlayerApi.GetGameVar(OwnerGameVarKey(ownerId), "");
        return string.IsNullOrWhiteSpace(value) ? -1 : DictionaryUtil.ParseInt(value, -1);
    }

    private static string OwnerGameVarKey(string ownerId)
    {
        return SunExpIds.WunaPersistentEmber + "_Owner_" + Sanitize(ownerId);
    }

    private static int Clamp(int level)
    {
        return Math.Max(0, Math.Min(99, level));
    }

    private static string NormalizeId(string? value)
    {
        return (value ?? "").Trim();
    }

    private static string Sanitize(string value)
    {
        var normalized = NormalizeId(value);
        if (normalized.Length == 0)
        {
            return "";
        }

        var chars = new char[normalized.Length];
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            chars[i] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_';
        }

        return new string(chars);
    }
}
