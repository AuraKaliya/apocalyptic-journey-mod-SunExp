using System;
using System.Collections.Generic;
using System.IO;
using AuraShared.Core;
using Newtonsoft.Json;
using Terrias.Dll.Contracts;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.GameApi;
using Data.Save;

namespace Terrias.Dll.Application;

public static class SolarMemoryRoleCommitApplication
{
    private static SolarMemoryCommitRecord? pending;
    private static RoleTable? callbackRole;
    private static Action<bool, string>? completion;
    private static string scope = "";
    private static long room;
    private static int attempts;
    private static DateTime retryAt;
    private static bool queryFirst;
    private static bool initialized;
    public static Func<SolarMemoryCommitRecord, bool, bool>? Send { private get; set; }

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        AuraNetworkIdentityRuntime.Changed += RefreshScope;
        AuraNetworkIdentityRuntime.Updating += Tick;
        RefreshScope();
    }

    public static bool IsConfirmedForCurrentRole => RoleTable.Instance?.SpecialVarMap != null
        && RoleTable.Instance.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var token)
        && !string.IsNullOrWhiteSpace(token)
        && RoleTable.Instance.SpecialVarMap.TryGetValue(SolarMemoryCommitRecord.ConfirmedTokenKey, out var confirmed)
        && confirmed == token
        && RoleTable.Instance.SpecialVarMap.TryGetValue(SolarMemoryCommitRecord.ConfirmedAdventureKey, out var adventure)
        && adventure == AuraNetworkIdentityRuntime.AdventureId;

    public static bool CommitFinal(RoleTable? role, string source) => SubmitFinal(role, source, null) != SolarMemoryRoleCommitSubmission.Rejected;

    public static SolarMemoryRoleCommitSubmission SubmitFinal(RoleTable? role, string source, Action<bool, string>? callback)
    {
        Initialize();
        if (role == null || !AuraNetworkIdentityRuntime.EnsureCurrentAdventure()) return SolarMemoryRoleCommitSubmission.Rejected;
        RefreshScope();
        role.SpecialVarMap ??= new Dictionary<string, string>();
        if (IsConfirmedForCurrentRole) return SolarMemoryRoleCommitSubmission.Accepted;
        try
        {
            if (AuraNetworkIdentityRuntime.IsAuthority) MigrateLegacyCommits();
            if (pending == null || pending.Status == "Rejected")
            {
                var token = role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var oldToken)
                    && AuraNetworkIdentityState.ValidId(oldToken) ? oldToken : Guid.NewGuid().ToString("N");
                role.SpecialVarMap[TerriasIds.SolarMemorySetupCommitTokenKey] = token;
                var json = JsonConvert.SerializeObject(role);
                var created = new SolarMemoryCommitRecord
                {
                    AdventureId = AuraNetworkIdentityRuntime.AdventureId, PlayerId = role.Id, Token = token,
                    PayloadHash = AuraVersionedReceipt.Hash(json), Payload = AuraBoundedCompressedJson.Encode(json, 40000)
                };
                Save(created);
                pending = created;
            }
            else role.SpecialVarMap[TerriasIds.SolarMemorySetupCommitTokenKey] = pending.Token;
            callbackRole = role; completion = callback; room = AuraNetworkIdentityRuntime.RoomGeneration;
            attempts = 0; retryAt = DateTime.MinValue; queryFirst = true;
            if (AuraNetworkIdentityRuntime.IsAuthority)
            {
                var record = pending;
                var result = Resolve(record,
                    TerriasRpcSender.FromAura(AuraRpcAuthorityRuntime.CreateLocalServerSender(source)), false, 2, out var reason);
                // Synchronous callers perform their own completion transition.
                completion = null;
                ReceiveAuthoritativeResult(record.AdventureId, record.PlayerId, record.Token, record.PayloadHash, result, reason);
                return result == "Accepted" ? SolarMemoryRoleCommitSubmission.Accepted : result == "Retry"
                    ? SolarMemoryRoleCommitSubmission.Pending : SolarMemoryRoleCommitSubmission.Rejected;
            }
            Tick();
            return SolarMemoryRoleCommitSubmission.Pending;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar Memory durable submission failed", ex);
            return SolarMemoryRoleCommitSubmission.Rejected;
        }
    }

    private static void RefreshScope()
    {
        var next = AuraNetworkIdentityRuntime.AdventureId + ":" + AuraNetworkIdentityRuntime.LocalPlayerId;
        if (next == scope && room == AuraNetworkIdentityRuntime.RoomGeneration) return;
        completion = null; callbackRole = null; pending = null; attempts = 0; queryFirst = true;
        room = AuraNetworkIdentityRuntime.RoomGeneration; scope = next;
        if (!AuraNetworkIdentityState.ValidId(AuraNetworkIdentityRuntime.AdventureId)) return;
        var path = PendingPath();
        if (!File.Exists(path)) return;
        try
        {
            var record = JsonConvert.DeserializeObject<SolarMemoryCommitRecord>(File.ReadAllText(path));
            if (record?.IsValid == true && record.AdventureId == AuraNetworkIdentityRuntime.AdventureId
                && record.PlayerId == AuraNetworkIdentityRuntime.LocalPlayerId && record.Status != "Rejected")
            { pending = record; pending.Status = "Pending"; retryAt = DateTime.MinValue; }
        }
        catch (Exception ex) { TerriasLog.Warn("[SolarMemoryCommit] recovery retained invalid record: " + ex.Message); }
    }

    private static void Tick()
    {
        RefreshScope();
        if (pending == null || pending.Status == "Rejected" || attempts >= 8 || DateTime.UtcNow < retryAt) return;
        if (RoleTable.Instance?.Id != pending.PlayerId || !AuraNetworkIdentityRuntime.MatchesAdventure(pending.AdventureId)) return;
        attempts++; retryAt = DateTime.UtcNow.AddSeconds(Math.Min(8, attempts));
        if (AuraNetworkIdentityRuntime.IsAuthority)
        {
            var record = pending;
            var result = Resolve(record,
                TerriasRpcSender.FromAura(AuraRpcAuthorityRuntime.CreateLocalServerSender("SolarMemory.Recovery")), queryFirst, 2, out var reason);
            ReceiveAuthoritativeResult(record.AdventureId, record.PlayerId, record.Token, record.PayloadHash, result, reason);
        }
        else
        {
            var record = JsonConvert.DeserializeObject<SolarMemoryCommitRecord>(JsonConvert.SerializeObject(pending))!;
            if (queryFirst) record.Payload = "";
            Send?.Invoke(record, queryFirst);
        }
        if (pending != null && attempts >= 8)
        {
            pending.Status = "Unknown"; pending.Diagnostic = "提交结果尚未确认，重新进入整备可继续确认。";
            Save(pending); PlayerApi.ShowCaption(pending.Diagnostic);
        }
    }

    internal static void ReceiveAuthoritativeResult(string adventureId, string playerId, string token, string hash, string result, string reason)
    {
        var record = pending;
        if (record == null || record.AdventureId != adventureId || record.PlayerId != playerId
            || record.Token != token || record.PayloadHash != hash || !AuraNetworkIdentityRuntime.MatchesAdventure(adventureId)) return;
        if (result == "NotFound") { queryFirst = false; retryAt = DateTime.MinValue; return; }
        if (result == "Retry") { record.Diagnostic = reason; Save(record); return; }
        if (result != "Accepted" && result != "Rejected") return;
        record.Status = result; record.Diagnostic = reason;
        Save(record);
        var role = RoleTable.Instance;
        if (result == "Accepted" && role?.Id == playerId)
        {
            role.SpecialVarMap ??= new Dictionary<string, string>();
            role.SpecialVarMap[TerriasIds.SolarMemorySetupFinishedKey] = "1";
            role.SpecialVarMap[TerriasIds.SolarMemorySetupCommitTokenKey] = token;
            role.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedTokenKey] = token;
            role.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedAdventureKey] = adventureId;
            AuraSharedFileStore.DeleteFile(TerriasIds.ModId, PendingPath());
        }
        var callback = ReferenceEquals(callbackRole, role) && room == AuraNetworkIdentityRuntime.RoomGeneration ? completion : null;
        completion = null; callbackRole = null;
        if (result == "Accepted") pending = null;
        callback?.Invoke(result == "Accepted", reason);
    }

    private static string PendingPath() => Path.Combine(AuraSharedPaths.OwnerDataRootDirectory, TerriasIds.ModId,
        "SolarCommits", AuraVersionedReceipt.Hash(scope) + ".json");
    private static void Save(SolarMemoryCommitRecord record) =>
        AuraSharedFileStore.WriteAllText(TerriasIds.ModId, PendingPath(), JsonConvert.SerializeObject(record), createBackup: true);
    internal static string Resolve(SolarMemoryCommitRecord commit, TerriasRpcSender sender, bool queryOnly, int protocol, out string reason)
    {
        reason = "";
        if (protocol != 2 || !sender.IsAvailable || !sender.IsLobbyMember || commit == null
            || commit.PlayerId != sender.PlayerId || !AuraNetworkIdentityRuntime.MatchesAdventure(commit.AdventureId))
        { reason = "会话或玩家身份不匹配。"; return "Rejected"; }
        var save = GameSaveManager.GetNowSave();
        if (save == null || GameSaveManager.GetValue<string>(TerriasIds.SolarMemoryModeKey) != "1")
        { reason = "当前不是日耀回忆冒险。"; return "Rejected"; }
        try
        {
            MigrateLegacyCommits();
            var key = ReceiptKey(commit.PlayerId);
            var previousReceipt = save.GameVars.TryGetValue(key, out var receiptJson) ? receiptJson : null;
            var receipt = string.IsNullOrWhiteSpace(previousReceipt) ? new AuraVersionedReceipt()
                : JsonConvert.DeserializeObject<AuraVersionedReceipt>(previousReceipt!) ?? throw new InvalidOperationException("整备提交记录损坏。");
            var decision = receipt.Decide(commit.Token, commit.PayloadHash, 0);
            if (receipt.Recent.Exists(item => item.RequestId == commit.Token && item.PayloadHash == ""))
            { reason = "旧存档中的整备已经保存，未重新应用提交内容。"; return "Accepted"; }
            if (decision == AuraMutationDecision.Duplicate) return "Accepted";
            if (decision != AuraMutationDecision.Apply)
            { reason = "该整备已完成或提交内容发生冲突。"; return "Rejected"; }
            if (queryOnly) return "NotFound";
            if (!commit.IsValid) { reason = "整备提交数据不完整。"; return "Rejected"; }
            var json = AuraBoundedCompressedJson.Decode(commit.Payload, 2 * 1024 * 1024);
            if (AuraVersionedReceipt.Hash(json) != commit.PayloadHash) throw new InvalidOperationException("整备内容校验失败。");
            var role = JsonConvert.DeserializeObject<RoleTable>(json);
            if (role == null || role.Id != sender.PlayerId || role.SpecialVarMap == null
                || !role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupFinishedKey, out var finished) || finished != "1"
                || !role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var token) || token != commit.Token)
                throw new InvalidOperationException("整备尚未完成或角色不匹配。");
            var server = GameServer.Instance ?? throw new InvalidOperationException("主机不可用。");
            var oldServerRole = server.RoleTables.TryGetValue(role.Id, out var old) ? old : null;
            var oldSavedRole = save.roleTable.TryGetValue(role.Id, out var saved) ? saved : null;
            role.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedTokenKey] = commit.Token;
            role.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedAdventureKey] = commit.AdventureId;
            try
            {
                server.RoleTables[role.Id] = role;
                GameSaveManager.UpdateRoles(role);
                save.GameVars[key] = JsonConvert.SerializeObject(receipt.Commit(commit.Token, commit.PayloadHash));
                AuraNativeSaveStore.Commit(save);
            }
            catch
            {
                if (oldServerRole == null) server.RoleTables.Remove(role.Id); else server.RoleTables[role.Id] = oldServerRole;
                if (oldSavedRole == null) save.roleTable.Remove(role.Id); else save.roleTable[role.Id] = oldSavedRole;
                if (previousReceipt == null) save.GameVars.Remove(key); else save.GameVars[key] = previousReceipt;
                throw;
            }
            return "Accepted";
        }
        catch (System.IO.IOException ex) { reason = ex.Message; return "Retry"; }
        catch (Exception ex) { reason = ex.Message; return "Rejected"; }
    }

    private static string ReceiptKey(string playerId) => "Terrias.SolarMemory.Commit." + AuraVersionedReceipt.Hash(playerId);

    private static void MigrateLegacyCommits()
    {
        const string schemaKey = "Terrias.SolarMemory.CommitSchema";
        var save = GameSaveManager.GetNowSave();
        if (save == null || !AuraNetworkIdentityRuntime.IsAuthority
            || save.GameVars.TryGetValue(schemaKey, out var schema) && schema == "2") return;
        var previousVars = new Dictionary<string, string>(save.GameVars);
        var changedRoles = new List<Tuple<RoleTable, string?, string?>>();
        try
        {
            foreach (var pair in save.roleTable)
            {
                var role = pair.Value;
                if (role?.SpecialVarMap == null || role.Id != pair.Key
                    || !role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupFinishedKey, out var finished) || finished != "1"
                    || !role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var token)
                    || !AuraNetworkIdentityState.ValidId(token) || save.GameVars.ContainsKey(ReceiptKey(role.Id))) continue;
                changedRoles.Add(Tuple.Create(role, role.SpecialVarMap.TryGetValue(SolarMemoryCommitRecord.ConfirmedTokenKey, out var prior) ? prior : null,
                    role.SpecialVarMap.TryGetValue(SolarMemoryCommitRecord.ConfirmedAdventureKey, out var priorAdventure) ? priorAdventure : null));
                role.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedTokenKey] = token;
                role.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedAdventureKey] = AuraNetworkIdentityRuntime.AdventureId;
                save.GameVars[ReceiptKey(role.Id)] = JsonConvert.SerializeObject(new AuraVersionedReceipt
                {
                    Version = 1,
                    Recent = new List<AuraMutationReceipt> { new() { RequestId = token, PayloadHash = "", Version = 1 } }
                });
            }
            save.GameVars[schemaKey] = "2";
            AuraNativeSaveStore.Commit(save);
        }
        catch
        {
            save.GameVars.Clear(); foreach (var pair in previousVars) save.GameVars[pair.Key] = pair.Value;
            foreach (var item in changedRoles)
            {
                if (item.Item2 == null) item.Item1.SpecialVarMap.Remove(SolarMemoryCommitRecord.ConfirmedTokenKey);
                else item.Item1.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedTokenKey] = item.Item2;
                if (item.Item3 == null) item.Item1.SpecialVarMap.Remove(SolarMemoryCommitRecord.ConfirmedAdventureKey);
                else item.Item1.SpecialVarMap[SolarMemoryCommitRecord.ConfirmedAdventureKey] = item.Item3;
            }
            throw;
        }
    }
}
