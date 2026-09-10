using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using AuraShared.Core;
using Data.Save;
using Newtonsoft.Json;
using Terrias.Dll.Contracts;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Application;

public static class EmberAdventureStateService
{
    private static bool initialized;
    public static Func<EmberUpdateRequest, bool>? Send { private get; set; }
    private static EmberClientOutbox? local;
    private static string scope = "";
    private static long room = -1;
    private static bool baselineReady;
    private static string queryToken = "";
    private static DateTime retryAt;
    private static int attempts;
    private static int applyingConfirmed;

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        AuraNetworkIdentityRuntime.Changed += RefreshScope;
        AuraNetworkIdentityRuntime.Updating += Tick;
    }

    public static int GetStored(IStatusManager? status)
    {
        Initialize(); RefreshScope();
        if (local != null && local.HasDesired) return local.DesiredLevel;
        if (local != null && baselineReady) return local.Confirmed.Level;
        var player = AuraNetworkIdentityRuntime.LocalPlayerId;
        var save = GameSaveManager.GetNowSave();
        if (save?.GameVars != null && save.GameVars.TryGetValue(StateKey(player), out var json))
            return Clamp(JsonConvert.DeserializeObject<EmberPersistentValue>(json)?.Level ?? 0);
        return ReadLegacy(player, status?.InstanceId ?? PlayerApi.LocalPlayerStatusId());
    }

    public static int CommitLocal(IStatusManager? status, int level, string source)
    {
        if (applyingConfirmed > 0) return Clamp(level);
        if (status != null && status.InstanceId != PlayerApi.LocalPlayerStatusId()) return Clamp(level);
        Initialize();
        if (!AuraNetworkIdentityRuntime.EnsureCurrentAdventure()) return Clamp(level);
        RefreshScope();
        if (local == null) return Clamp(level);
        var value = Clamp(level);
        if (local.HasDesired && local.DesiredLevel == value
            || !local.HasDesired && baselineReady && local.Confirmed.Level == value) return value;
        local.HasDesired = true; local.DesiredLevel = value; local.Status = "Pending";
        attempts = 0; retryAt = DateTime.MinValue;
        SaveOutbox();
        Tick();
        return value;
    }

    internal static EmberSyncResult Resolve(EmberUpdateRequest request, TerriasRpcSender sender)
    {
        var result = new EmberSyncResult { RequestId = request?.RequestId ?? "" };
        if (request == null || !AuraNetworkIdentityRuntime.IsAuthority || !sender.IsAvailable || !sender.IsLobbyMember
            || request.PlayerId != sender.PlayerId || !AuraNetworkIdentityRuntime.MatchesAdventure(request.AdventureId)
            || !AuraNetworkIdentityState.ValidId(request.RequestId))
        { result.Reason = "冒险或玩家身份不匹配。"; return result; }
        var save = GameSaveManager.GetNowSave();
        if (save == null) { result.Reason = "权威存档不可用。"; return result; }
        var key = StateKey(sender.PlayerId);
        var previous = save.GameVars.TryGetValue(key, out var json) ? json : null;
        var state = previous == null ? new EmberPersistentValue { Level = ReadLegacy(sender.PlayerId, sender.PlayerId) }
            : JsonConvert.DeserializeObject<EmberPersistentValue>(previous) ?? throw new InvalidDataException("余烬状态损坏。");
        result.Snapshot = Snapshot(request.AdventureId, sender.PlayerId, state);
        if (request.Query) { result.Status = "Snapshot"; return result; }
        if (request.Level < 0 || request.Level > 99) { result.Reason = "余烬数值无效。"; return result; }
        var hash = AuraVersionedReceipt.Hash(request.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var decision = state.Receipt.Decide(request.RequestId, hash, request.ExpectedVersion);
        if (decision == AuraMutationDecision.Duplicate) { result.Status = "Duplicate"; return result; }
        if (decision != AuraMutationDecision.Apply)
        { result.Status = "Conflict"; result.Reason = "余烬版本冲突，已返回房主确认状态。"; return result; }
        var next = new EmberPersistentValue { Level = request.Level, Receipt = state.Receipt.Commit(request.RequestId, hash) };
        var legacyKey = OwnerGameVarKey(TerriasIds.PersistentEmber, sender.PlayerId);
        var oldLegacy = save.GameVars.TryGetValue(legacyKey, out var legacy) ? legacy : null;
        try
        {
            save.GameVars[key] = JsonConvert.SerializeObject(next);
            save.GameVars[legacyKey] = next.Level.ToString();
            AuraNativeSaveStore.Commit(save);
        }
        catch (Exception ex)
        {
            if (previous == null) save.GameVars.Remove(key); else save.GameVars[key] = previous;
            if (oldLegacy == null) save.GameVars.Remove(legacyKey); else save.GameVars[legacyKey] = oldLegacy;
            result.Status = "Retry"; result.Reason = ex.Message; return result;
        }
        result.Snapshot = Snapshot(request.AdventureId, sender.PlayerId, next);
        result.Status = "Applied";
        return result;
    }

    internal static void Receive(EmberSyncResult result)
    {
        if (local == null || result?.Snapshot == null || result.Snapshot.OwnerPlayerId != local.PlayerId
            || result.Snapshot.AdventureId != local.AdventureId) return;
        if (result.RequestId == queryToken && result.Status == "Snapshot")
        {
            queryToken = ""; baselineReady = true;
            local.Confirmed = result.Snapshot;
            attempts = 0; retryAt = DateTime.MinValue;
            SaveOutbox();
            if (!local.HasDesired) ApplyToStatus(FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor, FightPlayer.Instance?.Status, result.Snapshot.Level, "network restore");
            return;
        }
        var pending = local.Pending;
        if (pending == null || result.RequestId != pending.RequestId) return;
        if (result.Status == "Retry") return;
        if (result.Status != "Applied" && result.Status != "Duplicate" && result.Status != "Conflict" && result.Status != "Rejected") return;
        local.Confirmed = result.Snapshot; baselineReady = true;
        local.Pending = null; attempts = 0; retryAt = DateTime.MinValue;
        if (result.Status == "Conflict" || result.Status == "Rejected")
        {
            local.Status = result.Status; local.HasDesired = false;
            PlayerApi.ShowCaption(result.Reason);
        }
        else if (local.DesiredLevel == pending.Level) local.HasDesired = false;
        SaveOutbox();
        if (!local.HasDesired)
            ApplyToStatus(FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor, FightPlayer.Instance?.Status, result.Snapshot.Level, "network confirmed");
    }

    private static void RefreshScope()
    {
        var next = AuraNetworkIdentityRuntime.AdventureId + ":" + AuraNetworkIdentityRuntime.LocalPlayerId;
        if (scope == next && room == AuraNetworkIdentityRuntime.RoomGeneration) return;
        scope = next; room = AuraNetworkIdentityRuntime.RoomGeneration;
        local = null; baselineReady = false; queryToken = ""; attempts = 0; retryAt = DateTime.MinValue;
        if (!AuraNetworkIdentityState.ValidId(AuraNetworkIdentityRuntime.AdventureId)) return;
        if (File.Exists(OutboxPath()))
        {
            try { local = JsonConvert.DeserializeObject<EmberClientOutbox>(File.ReadAllText(OutboxPath())); }
            catch (Exception ex) { TerriasLog.Warn("[Ember] retained unreadable outbox: " + ex.Message); }
        }
        if (local == null || local.AdventureId != AuraNetworkIdentityRuntime.AdventureId || local.PlayerId != AuraNetworkIdentityRuntime.LocalPlayerId)
            local = new EmberClientOutbox { AdventureId = AuraNetworkIdentityRuntime.AdventureId, PlayerId = AuraNetworkIdentityRuntime.LocalPlayerId };
    }

    private static void Tick()
    {
        RefreshScope();
        if (local == null || RoleTable.Instance?.Id != local.PlayerId || DateTime.UtcNow < retryAt || attempts >= 8) return;
        EmberUpdateRequest request;
        if (!baselineReady)
        {
            if (queryToken.Length == 0) queryToken = Guid.NewGuid().ToString("N");
            request = new EmberUpdateRequest { AdventureId = local.AdventureId, PlayerId = local.PlayerId, RequestId = queryToken, Query = true };
        }
        else
        {
            if (!local.HasDesired && local.Pending == null) return;
            if (local.Pending == null)
            {
                local.Pending = new EmberUpdateRequest { AdventureId = local.AdventureId, PlayerId = local.PlayerId,
                    RequestId = Guid.NewGuid().ToString("N"), ExpectedVersion = local.Confirmed.Version, Level = local.DesiredLevel };
                SaveOutbox();
            }
            request = local.Pending;
        }
        attempts++; retryAt = DateTime.UtcNow.AddSeconds(Math.Min(attempts, 8));
        if (AuraNetworkIdentityRuntime.IsAuthority)
            Receive(Resolve(request, TerriasRpcSender.FromAura(AuraRpcAuthorityRuntime.CreateLocalServerSender("Ember.Commit"))));
        else Send?.Invoke(request);
        if (attempts >= 8 && local != null)
        {
            local.Status = "Unknown"; SaveOutbox();
            PlayerApi.ShowCaption("余烬更新尚未确认，已保留待恢复记录。");
        }
    }

    public static int RestoreForLocalPlayer(string source)
    {
        Initialize(); RefreshScope();
        attempts = 0; retryAt = DateTime.MinValue; Tick();
        var status = FightPlayer.Instance?.Status;
        var value = GetStored(status);
        ApplyToStatus(status?.MirrorSc as ScriptExecutor, status, value, source);
        return value;
    }

    public static void ApplyToStatus(ScriptExecutor? executor, IStatusManager? status, int level, string source)
    {
        if (status == null) return;
        applyingConfirmed++;
        try
        {
        var safe = Clamp(level);
        if (safe <= 0)
        {
            BuffApi.ClearEmberDamageBonus(executor, status);
            if (BuffApi.Level(status, TerriasIds.Ember) > 0) status.RemoveBuff(TerriasIds.Ember);
        }
        else
        {
            BuffApi.SetExactLevel(status, TerriasIds.Ember, safe);
            BuffApi.SyncEmberDamageBonus(executor, status);
        }
        }
        finally { applyingConfirmed--; }
    }

    private static EmberAdventureStateSnapshot Snapshot(string adventure, string owner, EmberPersistentValue state) =>
        new() { AdventureId = adventure, OwnerPlayerId = owner, OwnerStatusId = owner, Level = state.Level, Version = state.Receipt.Version };
    private static int ReadLegacy(string player, string status)
    {
        foreach (var id in new[] { player, status })
        foreach (var key in new[] { TerriasIds.PersistentEmber, TerriasIds.WunaPersistentEmber })
        {
            var value = PlayerApi.GetGameVar(OwnerGameVarKey(key, id), "");
            if (int.TryParse(value, out var parsed)) return Clamp(parsed);
        }
        // Unscoped historical values are only meaningful in a proven solo lobby.
        if (GameServer.Instance?.LobbyInfo?.AddedPlayers?.Count == 1 && AuraNetworkIdentityRuntime.IsAuthority)
            return Clamp(DictionaryUtil.ParseInt(PlayerApi.GetScopedGameVar(TerriasIds.WunaPersistentEmber,
                FightPlayer.Instance?.Status, "0", migrateLegacyWhenSolo: true)));
        return 0;
    }
    private static string StateKey(string player) => "Terrias.Ember.Versioned." + AuraVersionedReceipt.Hash(player);
    private static string OwnerGameVarKey(string key, string owner) =>
        key + "_Owner_" + new string((owner ?? "").ToCharArray().Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
    private static int Clamp(int level) => Math.Max(0, Math.Min(99, level));
    private static string OutboxPath() => Path.Combine(AuraSharedPaths.OwnerDataRootDirectory, TerriasIds.ModId, "EmberUpdates", AuraVersionedReceipt.Hash(scope) + ".json");
    private static void SaveOutbox()
    {
        if (local != null) AuraSharedFileStore.WriteAllText(TerriasIds.ModId, OutboxPath(), JsonConvert.SerializeObject(local));
    }
}
