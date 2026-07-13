using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class SpiritSummonService
{
    private static readonly object NetworkSync = new();
    private static readonly HashSet<string> ResolvedTokens = new(StringComparer.Ordinal);

    public static void ResetBattleSynchronization()
    {
        lock (NetworkSync)
        {
            ResolvedTokens.Clear();
        }
    }

    public static bool TrySummon(ScriptExecutor self)
    {
        if (!CanSummon(self?.dataConfig, self?.Self, out var reason, out var snapshot))
        {
            PlayerApi.ShowCaption("精灵：" + reason);
            return false;
        }

        var ownerStatusId = self!.Self.InstanceId;
        if (SunExpNetworkRuntime.IsMultiplayerSession() && SunExpNetworkRuntime.IsClientOnly())
        {
            var token = Guid.NewGuid().ToString("N");
            SunExpNetworkRuntime.Send(
                new RpcSpiritSummonRequest(snapshot!, ownerStatusId, token),
                "SpiritSummonService.TrySummon");
            PlayerApi.ShowCaption("精灵：正在同步召唤。");
            return true;
        }

        return TrySummonLocal(
            snapshot!,
            ownerStatusId,
            "SpiritSummonService.TrySummon",
            SunExpNetworkRuntime.IsMultiplayerSession());
    }

    public static void ResolveNetworkSummon(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string token,
        SunExpRpcSender sender,
        int protocolVersion,
        int battleEpoch,
        string registryHash)
    {
        if (!ClaimToken(token))
        {
            return;
        }

        var rejection = ValidateNetworkRequest(snapshot, ownerStatusId, sender, protocolVersion, battleEpoch, registryHash);
        if (rejection.Length > 0)
        {
            Broadcast(new SpiritCompanionSnapshot
            {
                Token = token ?? "",
                CapturedEnemy = snapshot ?? new CapturedEnemySnapshot(),
                OwnerStatusId = ownerStatusId ?? "",
                Accepted = false,
                RejectionReason = rejection
            }, "SpiritSummonService.ResolveNetworkSummon.Reject");
            return;
        }

        TrySummonLocal(snapshot, ownerStatusId, "SpiritSummonService.ResolveNetworkSummon", true, token, sender.PlayerId);
    }

    public static void ApplyNetworkState(SpiritCompanionSnapshot? snapshot, string source)
    {
        if (snapshot == null)
        {
            return;
        }

        if (!snapshot.Accepted)
        {
            if (SenderOwnsStatus(SunExpNetworkRuntime.LocalPlayerId(), snapshot.OwnerStatusId))
            {
                PlayerApi.ShowCaption("精灵：" + RejectionMessage(snapshot.RejectionReason));
            }
            return;
        }

        if (snapshot.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !string.Equals(snapshot.RegistryHash, SpiritIntentRegistry.RegistryHash, StringComparison.Ordinal))
        {
            SunExpLog.Warn("[Spirit] ignored incompatible companion snapshot from " + source + ".");
            return;
        }

        var existing = SpiritStateStore.Find(snapshot.StatusId)
            ?? SpiritStateStore.FindByOwner(snapshot.OwnerPlayerId, snapshot.OwnerStatusId);
        if (existing != null)
        {
            ApplySnapshot(existing.Spirit, snapshot, source);
            return;
        }

        Spawn(snapshot.CapturedEnemy, snapshot.OwnerStatusId, snapshot.OwnerPlayerId, snapshot.StatusId, source, snapshot);
    }

    public static bool CanSummon(IDataConfig? card, IStatusManager? owner, out string reason)
    {
        return CanSummon(card, owner, out reason, out _);
    }

    private static bool CanSummon(IDataConfig? card, IStatusManager? owner, out string reason, out CapturedEnemySnapshot? snapshot)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        var result = CanSummonCore(card, owner, out reason, out snapshot);
        SunExpPerformanceCounters.RecordHotspot(
            "Spirit.Summon.CanSummon",
            started,
            "owner=" + (owner?.InstanceId ?? "<none>")
            + ", allowed=" + result
            + (reason.Length == 0 ? "" : ", reason=" + reason),
            logFirstSample: true);
        return result;
    }

    private static bool CanSummonCore(IDataConfig? card, IStatusManager? owner, out string reason, out CapturedEnemySnapshot? snapshot)
    {
        snapshot = SpiritCardFactory.Read(card);
        if (owner == null || snapshot == null)
        {
            reason = "召唤信息已经失效。";
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            reason = "只能在战斗中召唤。";
            return false;
        }

        if (!HasIdle(snapshot.IdlePath))
        {
            reason = "来源动画暂时不可用。";
            return false;
        }

        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(owner.InstanceId);
        if (CompanionPositionOwnershipService.HasForOwner(ownerPlayerId, owner.InstanceId))
        {
            reason = "投影位置已被占用。";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool TrySummonLocal(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string source,
        bool broadcast,
        string token = "",
        string preferredOwnerPlayerId = "")
    {
        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, preferredOwnerPlayerId);
        if (CompanionPositionOwnershipService.HasForOwner(ownerPlayerId, ownerStatusId))
        {
            BroadcastRejection(snapshot, ownerStatusId, token, "position-occupied", broadcast, source);
            return false;
        }

        var statusId = SpiritStateStore.NextStatusId();
        var spawned = Spawn(snapshot, ownerStatusId, ownerPlayerId, statusId, source, null);
        if (spawned && broadcast)
        {
            var spirit = SpiritStateStore.Find(statusId)?.Spirit;
            if (spirit != null)
            {
                var networkState = BuildSnapshot(spirit);
                networkState.Token = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token;
                Broadcast(networkState, source);
            }
        }

        return spawned;
    }

    private static bool Spawn(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string ownerPlayerId,
        string statusId,
        string source,
        SpiritCompanionSnapshot? networkState)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        var succeeded = false;
        try
        {
            var prefab = SunExpResourceCache.Load<GameObject>("Model/player", true, "spirit");
            if (prefab == null)
            {
                PlayerApi.ShowCaption("精灵：战斗模型加载失败。");
                return false;
            }

            var root = UnityEngine.Object.Instantiate(prefab);
            var spirit = root.AddComponent<SpiritOtherObj>();
            var profile = SpiritIntentRegistry.ProfileFor(snapshot.ProfileKey);
            var stats = networkState == null
                ? CompanionStatsService.SpiritStats(profile)
                : new CompanionStats(1, Math.Max(1, networkState.MaxMagic), Math.Max(1, networkState.Attack), Math.Max(1, networkState.Armor));
            if (networkState != null)
            {
                stats.SetCurrentMagic(networkState.CurrentMagic);
            }

            if (!spirit.InitSpirit(snapshot, ownerStatusId, -1, stats, ownerPlayerId, statusId))
            {
                UnityEngine.Object.Destroy(root);
                PlayerApi.ShowCaption("精灵：初始化失败。");
                return false;
            }

            SpiritStateStore.Register(new SpiritState(snapshot, ownerStatusId, spirit.OwnerPlayerId, spirit, -1));
            spirit.Status.UpdateStatus(true);
            if (networkState == null)
            {
                spirit.Activate(source);
                PlayerApi.ShowCaption("精灵：【" + snapshot.DisplayName + "】加入战斗。");
            }
            else
            {
                ApplySnapshot(spirit, networkState, source);
            }
            succeeded = true;
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[Spirit] summon failed from " + source, ex);
            PlayerApi.ShowCaption("精灵：召唤失败。");
            return false;
        }
        finally
        {
            SunExpPerformanceCounters.RecordHotspot(
                "Spirit.Summon.Spawn",
                started,
                "enemy=" + (snapshot?.EnemyId ?? "<none>")
                + ", network=" + (networkState != null)
                + ", success=" + succeeded
                + ", source=" + source,
                logFirstSample: true);
        }
    }

    public static DataConfig CreateSpiritDataConfig(CapturedEnemySnapshot snapshot, CompanionStats stats)
    {
        IDictionary<string, string> source;
        try
        {
            source = new DataConfig(snapshot.EnemyId, DataType.Enemy).data;
        }
        catch
        {
            source = new Dictionary<string, string>();
        }

        var data = new Dictionary<string, string>(source)
        {
            ["Id"] = snapshot.EnemyId,
            ["Name"] = snapshot.DisplayName,
            ["Animation"] = snapshot.AnimationPath,
            ["Attack"] = stats.Attack.ToString(),
            ["Defend"] = "0",
            ["Hp"] = "1",
            ["ActionCount"] = "1",
            ["CardList"] = string.Join(",", new[]
            {
                SunExpIds.ProjectionActionStaffTapCardId,
                SunExpIds.ProjectionActionShieldBlessingCardId,
                SunExpIds.ProjectionActionStaffComboCardId,
                SunExpIds.ProjectionActionMagicInterferenceCardId,
                SunExpIds.ProjectionActionYouAreEnhancedCardId,
                SunExpIds.ProjectionActionChargeCardId,
                SunExpIds.ProjectionActionHolyHealCardId
            })
        };
        return new DataConfig(data, new Dictionary<string, string>());
    }

    public static void RegisterFightState(SpiritOtherObj spirit)
    {
        var status = spirit.Status as StatusManager;
        var manager = FightManager.Instance;
        if (status == null || manager == null)
        {
            return;
        }

        manager.statuses[spirit.InstanceId] = status;
        if (manager.netIdentity != null && manager.isServer)
        {
            manager.statusData[spirit.InstanceId] = new StatusDataTransfer(status);
        }

        ProjectionTurnCoordinator.RegisterCompanion(spirit, "SpiritSummonService.RegisterFightState");
    }

    public static void BroadcastRuntimeState(SpiritOtherObj spirit, string source)
    {
        if (spirit == null || !SunExpNetworkRuntime.IsMultiplayerSession() || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        Broadcast(BuildSnapshot(spirit), "SpiritRuntime." + source);
    }

    private static SpiritCompanionSnapshot BuildSnapshot(SpiritOtherObj spirit)
    {
        var state = CompanionBattleStateStore.Find(spirit.InstanceId);
        return new SpiritCompanionSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            RegistryHash = SpiritIntentRegistry.RegistryHash,
            Revision = state?.Revision ?? 0,
            Accepted = true,
            CapturedEnemy = spirit.Snapshot,
            OwnerStatusId = spirit.OwnerStatusId,
            OwnerPlayerId = spirit.OwnerPlayerId,
            StatusId = spirit.InstanceId,
            Attack = spirit.Attack,
            Armor = state?.Stats.Armor ?? 1,
            MaxMagic = state?.Stats.MaxMagic ?? 1,
            CurrentMagic = state?.Stats.CurrentMagic ?? 0,
            TurnIndex = state?.TurnIndex ?? 0,
            ReadyOnTurn = state == null
                ? new Dictionary<string, int>()
                : state.ReadyOnTurnSnapshot().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            Threat = CompanionThreatService.Export(spirit.InstanceId),
            IntentPlan = state?.CurrentPlan
        };
    }

    private static void ApplySnapshot(SpiritOtherObj spirit, SpiritCompanionSnapshot snapshot, string source)
    {
        var state = CompanionBattleStateStore.Find(spirit.InstanceId);
        if (state == null || snapshot.Revision < state.Revision)
        {
            return;
        }

        state.Stats.SetCurrentMagic(snapshot.CurrentMagic);
        state.ApplyReadyOnTurn(snapshot.ReadyOnTurn);
        state.ApplyRemoteProgress(snapshot.TurnIndex, snapshot.Revision);
        CompanionThreatService.ApplyAuthoritative(snapshot.Threat);
        spirit.ActivateAfterHydration(snapshot.IntentPlan, source);
    }

    private static string ValidateNetworkRequest(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        SunExpRpcSender sender,
        int protocolVersion,
        int battleEpoch,
        string registryHash)
    {
        if (protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion)
        {
            return "protocol-mismatch";
        }
        if (battleEpoch != CompanionAuthorityService.BattleEpoch)
        {
            return "battle-epoch-mismatch";
        }
        if (!string.Equals(registryHash, SpiritIntentRegistry.RegistryHash, StringComparison.Ordinal))
        {
            return "registry-mismatch";
        }
        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            return "sender-invalid";
        }
        if (!SenderOwnsStatus(sender.PlayerId, ownerStatusId))
        {
            return "owner-mismatch";
        }
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EnemyId) || !HasIdle(snapshot.IdlePath))
        {
            return "snapshot-invalid";
        }
        return "";
    }

    private static bool SenderOwnsStatus(string playerId, string ownerStatusId)
    {
        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return false;
        }
        if (string.Equals(playerId, ownerStatusId, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            return map != null
                && map.TryGetValue(playerId, out var statuses)
                && statuses != null
                && statuses.Contains(ownerStatusId);
        }
        catch
        {
            return false;
        }
    }

    private static bool ClaimToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        lock (NetworkSync)
        {
            return ResolvedTokens.Add(token);
        }
    }

    private static void BroadcastRejection(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string token,
        string reason,
        bool broadcast,
        string source)
    {
        if (broadcast)
        {
            Broadcast(new SpiritCompanionSnapshot
            {
                Token = token ?? "",
                CapturedEnemy = snapshot,
                OwnerStatusId = ownerStatusId ?? "",
                Accepted = false,
                RejectionReason = reason
            }, source + ".Reject");
        }
        else
        {
            PlayerApi.ShowCaption("精灵：" + RejectionMessage(reason));
        }
    }

    private static bool Broadcast(SpiritCompanionSnapshot snapshot, string source)
    {
        return SunExpNetworkRuntime.Send(new RpcSpiritCompanionState(snapshot), source);
    }

    private static string RejectionMessage(string reason)
    {
        return (reason ?? "") switch
        {
            "position-occupied" => "投影位置已被占用。",
            "protocol-mismatch" => "召唤协议版本不一致。",
            "battle-epoch-mismatch" => "当前战斗状态已失效，请重新使用。",
            "registry-mismatch" => "精灵行动配置不一致。",
            "sender-invalid" => "无法确认操作玩家。",
            "owner-mismatch" => "当前角色不属于该玩家。",
            "snapshot-invalid" => "捕获记录或来源动画已经失效。",
            _ => "召唤失败，请稍后重试。"
        };
    }

    private static bool HasIdle(string idlePath)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        var found = false;
        try
        {
            found = !string.IsNullOrWhiteSpace(idlePath)
                && SunExpResourceCache.LoadAll<Sprite>(idlePath, "spirit-idle")?.Length > 0;
            return found;
        }
        catch
        {
            return false;
        }
        finally
        {
            SunExpPerformanceCounters.RecordHotspot(
                "Spirit.Summon.IdleProbe",
                started,
                "found=" + found + ", path=" + (idlePath ?? ""),
                logFirstSample: true);
        }
    }
}
