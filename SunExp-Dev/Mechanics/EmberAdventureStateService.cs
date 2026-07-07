using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;
using Witch;

namespace SunExp.Dll.Mechanics;

public sealed class EmberAdventureStateSnapshot
{
    public string OwnerPlayerId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public int Level { get; set; }

    public int Sequence { get; set; }

    public string Source { get; set; } = "";
}

public static class EmberAdventureStateService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, int> LastSequences = new(StringComparer.Ordinal);
    private static int localSequence;

    public static int GetStored(IStatusManager? status)
    {
        var ownerPlayerId = ResolveOwnerPlayerId(status);
        var ownerStatusId = ResolveOwnerStatusId(status);
        var ownerValue = ReadOwnerValue(SunExpIds.PersistentEmber, ownerPlayerId);
        if (ownerValue >= 0)
        {
            return Clamp(ownerValue);
        }

        ownerValue = ReadOwnerValue(SunExpIds.PersistentEmber, ownerStatusId);
        if (ownerValue >= 0)
        {
            return Clamp(ownerValue);
        }

        ownerValue = ReadOwnerValue(SunExpIds.WunaPersistentEmber, ownerPlayerId);
        if (ownerValue >= 0)
        {
            return Clamp(ownerValue);
        }

        ownerValue = ReadOwnerValue(SunExpIds.WunaPersistentEmber, ownerStatusId);
        if (ownerValue >= 0)
        {
            return Clamp(ownerValue);
        }

        var scopedValue = PlayerApi.GetScopedGameVar(
            SunExpIds.PersistentEmber,
            status,
            "",
            migrateLegacyWhenSolo: false);
        if (!string.IsNullOrWhiteSpace(scopedValue))
        {
            return Clamp(DictionaryUtil.ParseInt(scopedValue));
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
        var snapshot = new EmberAdventureStateSnapshot
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
            SunExpNetworkRuntime.Send(new RpcEmberAdventureStateCommit(snapshot), safeSource);
            return snapshot.Level;
        }

        RpcEmberAdventureStateCommit.ApplyOnServer(
            snapshot,
            SunExpRpcAuthorityRuntime.CreateLocalServerSender(safeSource),
            false);
        SunExpNetworkRuntime.Send(new RpcEmberAdventureStateCommit(snapshot)
        {
            Accepted = true
        }, safeSource);
        return snapshot.Level;
    }

    public static bool ApplySnapshot(EmberAdventureStateSnapshot? snapshot, string source)
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
                && snapshot.Sequence <= previous)
            {
                SunExpLog.Debug("[EmberAdventureState] stale or duplicate snapshot ignored from "
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
            PlayerApi.SetGameVar(OwnerGameVarKey(SunExpIds.PersistentEmber, snapshot.OwnerPlayerId), snapshot.Level.ToString());
        }

        if (!string.IsNullOrWhiteSpace(snapshot.OwnerStatusId))
        {
            PlayerApi.SetGameVar(OwnerGameVarKey(SunExpIds.PersistentEmber, snapshot.OwnerStatusId), snapshot.Level.ToString());
        }

        if (IsLocalOwner(snapshot))
        {
            PlayerApi.SetScopedGameVar(SunExpIds.PersistentEmber, FightPlayer.Instance?.Status, snapshot.Level.ToString());
        }

        SunExpLog.Info("[EmberAdventureState] saved owner="
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

    public static int RestoreForLocalPlayer(string source)
    {
        var status = FightPlayer.Instance?.Status;
        if (status == null)
        {
            return 0;
        }

        var stored = GetStored(status);
        ApplyToStatus(status.MirrorSc as ScriptExecutor, status, stored, source);
        return stored;
    }

    public static void ApplyToStatus(ScriptExecutor? executor, IStatusManager? status, int level, string source)
    {
        if (status == null)
        {
            return;
        }

        var safeLevel = Clamp(level);
        if (BuffApi.Level(status, SunExpIds.Ember) > 0 && safeLevel <= 0)
        {
            BuffApi.ClearEmberDamageBonus(executor, status);
            status.RemoveBuff(SunExpIds.Ember);
        }
        else if (safeLevel > 0)
        {
            BuffApi.SetExactLevel(status, SunExpIds.Ember, safeLevel);
            BuffApi.SyncEmberDamageBonus(executor, status);
        }
        else
        {
            BuffApi.ClearEmberDamageBonus(executor, status);
        }

        if (safeLevel <= 0)
        {
            CommitLocal(status, safeLevel, "EmberAdventureStateService.ApplyToStatus:" + source);
        }
    }

    private static bool IsLocalOwner(EmberAdventureStateSnapshot snapshot)
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

    private static string OwnerKey(EmberAdventureStateSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.OwnerPlayerId)
            ? snapshot.OwnerPlayerId
            : snapshot.OwnerStatusId;
    }

    private static int ReadOwnerValue(string key, string ownerId)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(ownerId))
        {
            return -1;
        }

        var value = PlayerApi.GetGameVar(OwnerGameVarKey(key, ownerId), "");
        return string.IsNullOrWhiteSpace(value) ? -1 : DictionaryUtil.ParseInt(value, -1);
    }

    private static string OwnerGameVarKey(string key, string ownerId)
    {
        return key + "_Owner_" + Sanitize(ownerId);
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
