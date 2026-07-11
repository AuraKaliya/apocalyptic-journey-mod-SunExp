using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class ProjectionSummonService
{
    private const int PartyCap = CompanionSlotService.MaxFriendlySlots;
    private static readonly object NetworkSync = new();
    private static readonly HashSet<string> ResolvedTokens = new(StringComparer.Ordinal);

    public static void ResetBattleSynchronization()
    {
        lock (NetworkSync)
        {
            ResolvedTokens.Clear();
        }
    }

    public static bool TrySummon(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self?.Self == null || role == null)
        {
            PlayerApi.ShowCaption("拜托了：召唤失败。");
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            PlayerApi.ShowCaption("拜托了：只能在战斗中召唤。");
            return false;
        }

        if (SunExpNetworkRuntime.IsMultiplayerSession() && !SunExpNetworkRuntime.IsServer())
        {
            var token = Guid.NewGuid().ToString("N");
            SunExpNetworkRuntime.Send(
                new RpcProjectionSummonRequest(role.Id, self.Self.InstanceId, token),
                "ProjectionSummonService.TrySummon");
            PlayerApi.ShowCaption("拜托了：正在同步投影。");
            return true;
        }

        return TrySummonLocal(
            self.Self.InstanceId,
            role,
            "ProjectionSummonService.TrySummon",
            broadcast: SunExpNetworkRuntime.IsMultiplayerSession());
    }

    public static void ResolveNetworkSummon(
        string roleId,
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

        var role = PolymorphRoleRegistry.Find(roleId);
        var rejection = ValidateNetworkSender(sender, ownerStatusId);
        if (protocolVersion != 2)
        {
            rejection = "projection protocol mismatch";
        }
        else if (battleEpoch != CompanionAuthorityService.BattleEpoch)
        {
            rejection = "projection battle epoch mismatch";
        }
        else if (!string.Equals(registryHash, CompanionIntentRegistry.RegistryHash, StringComparison.Ordinal))
        {
            rejection = "projection intent registry mismatch";
        }
        if (role == null)
        {
            rejection = "unknown role: " + roleId;
        }

        if (!string.IsNullOrWhiteSpace(rejection))
        {
            BroadcastNetworkState(new ProjectionCompanionSnapshot
            {
                Token = token ?? "",
                RoleId = roleId ?? "",
                OwnerStatusId = ownerStatusId ?? "",
                Accepted = false,
                RejectionReason = rejection
            }, "ProjectionSummonService.ResolveNetworkSummon.Reject");
            return;
        }

        TrySummonLocal(
            ownerStatusId,
            role!,
            "ProjectionSummonService.ResolveNetworkSummon",
            broadcast: true,
            token: token,
            preferredOwnerPlayerId: sender.PlayerId);
    }

    public static void ApplyNetworkState(ProjectionCompanionSnapshot? snapshot, string source)
    {
        if (snapshot == null)
        {
            return;
        }

        if (!snapshot.Accepted)
        {
            if (SenderOwnsStatus(SunExpNetworkRuntime.LocalPlayerId(), snapshot.OwnerStatusId))
            {
                PlayerApi.ShowCaption("拜托了：" + snapshot.RejectionReason);
            }

            return;
        }

        var role = PolymorphRoleRegistry.Find(snapshot.RoleId);
        if (role == null || string.IsNullOrWhiteSpace(snapshot.StatusId))
        {
            return;
        }

        if (snapshot.ProtocolVersion != 2
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !string.Equals(snapshot.RegistryHash, CompanionIntentRegistry.RegistryHash, StringComparison.Ordinal))
        {
            SunExpLog.Warn("[Projection] ignored incompatible snapshot: protocol=" + snapshot.ProtocolVersion
                + ", epoch=" + snapshot.BattleEpoch + ", localEpoch=" + CompanionAuthorityService.BattleEpoch);
            return;
        }

        var existing = ProjectionStateStore.Find(snapshot.StatusId);
        if (existing != null)
        {
            ApplySnapshot(existing.Projection, snapshot, source);
            return;
        }

        SpawnProjection(role, snapshot.OwnerStatusId, snapshot.SlotIndex, snapshot.StatusId, source, snapshot);
    }

    public static DataConfig CreateProjectionDataConfig(PolymorphRoleSpec role, CompanionStats? stats = null)
    {
        var activeStats = stats ?? CompanionStatsService.ProjectionStats(role);
        var data = new Dictionary<string, string>(new DataConfig(role.Id, DataType.Career).data);
        var vars = new Dictionary<string, string>();
        var name = role.DisplayName + "的投影";
        data["Id"] = role.Id;
        data["Name"] = name;
        data["Name_zh-Hant"] = role.DisplayName + "的投影";
        data["Name_en"] = role.DisplayName + " Projection";
        data["Name_ja"] = role.DisplayName + "の投影";
        data["Attack"] = activeStats.Attack.ToString();
        data["Defend"] = activeStats.Armor.ToString();
        data["Hp"] = activeStats.MaxHp.ToString();
        data["ActionCount"] = "1";
        data["CardList"] = SunExpIds.ProjectionActionStaffTapCardId + "," + SunExpIds.ProjectionActionShieldBlessingCardId;
        return new DataConfig(data, vars);
    }

    public static void RegisterFightState(ProjectionOtherObj projection)
    {
        var status = projection.Status as StatusManager;
        var manager = FightManager.Instance;
        if (status == null || manager == null)
        {
            return;
        }

        manager.statuses[projection.InstanceId] = status;
        if (manager.netIdentity != null && manager.isServer)
        {
            manager.statusData[projection.InstanceId] = new StatusDataTransfer(status);
        }

        if (!manager.ActionQueue.Contains(projection))
        {
            manager.ActionQueue.Add(projection);
        }

        var roleId = RoleTable.Instance?.Id ?? "";
        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        if (!string.IsNullOrWhiteSpace(roleId) && map != null)
        {
            if (!map.ContainsKey(roleId))
            {
                map.Add(roleId, new List<string>());
            }

            if (!map[roleId].Contains(projection.InstanceId))
            {
                map[roleId].Add(projection.InstanceId);
            }
        }

        try
        {
            var ui = UIManager.Instance?.GetUI<FightUI>("FightUI");
            if (ui?.StatusList != null && !ui.StatusList.Contains(status))
            {
                ui.StatusList.Add(status);
            }
        }
        catch
        {
            // Fight UI can be absent during fake combat initialization.
        }
    }

    public static void PositionProjection(ProjectionOtherObj projection, int slotIndex)
    {
        CompanionSlotService.PositionInPlayerSlot(projection, slotIndex);
    }

    private static bool TrySummonLocal(
        string ownerStatusId,
        PolymorphRoleSpec role,
        string source,
        bool broadcast,
        string token = "",
        string preferredOwnerPlayerId = "")
    {
        var currentCount = RealPlayerCount() + ProjectionStateStore.ActiveCount();
        if (currentCount >= PartyCap)
        {
            PlayerApi.ShowCaption("拜托了：场上友方单位已达到上限。");
            BroadcastRejectIfNeeded(role.Id, ownerStatusId, token, "no open friendly slot", broadcast, source);
            return false;
        }

        var slotIndex = CompanionSlotService.FindOpenPlayerSlot();
        if (slotIndex == null)
        {
            PlayerApi.ShowCaption("拜托了：没有可用的友方站位。");
            BroadcastRejectIfNeeded(role.Id, ownerStatusId, token, "no open friendly slot", broadcast, source);
            return false;
        }

        var statusId = ProjectionStateStore.NextStatusId();
        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, preferredOwnerPlayerId);
        var owner = StatusById(ownerStatusId);
        var buffs = ProjectionBuffCopyService.Capture(owner);
        var spawned = SpawnProjection(role, ownerStatusId, slotIndex.Value, statusId, source, null, ownerPlayerId, buffs);
        if (spawned && broadcast)
        {
            var projection = ProjectionStateStore.Find(statusId)?.Projection;
            if (projection != null)
            {
                var snapshot = BuildSnapshot(projection);
                snapshot.Token = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token;
                BroadcastNetworkState(snapshot, source);
            }
        }

        return spawned;
    }

    private static bool SpawnProjection(
        PolymorphRoleSpec role,
        string ownerStatusId,
        int slotIndex,
        string statusId,
        string source,
        ProjectionCompanionSnapshot? snapshot = null,
        string ownerPlayerId = "",
        IReadOnlyList<ProjectionBuffSnapshot>? initialBuffs = null)
    {
        try
        {
            var prefab = SunExpResourceCache.Load<GameObject>("Model/player", true, "projection");
            if (prefab == null)
            {
                PlayerApi.ShowCaption("拜托了：投影模型加载失败。");
                return false;
            }

            var gameObject = UnityEngine.Object.Instantiate(prefab);
            if (gameObject == null)
            {
                PlayerApi.ShowCaption("拜托了：投影模型加载失败。");
                return false;
            }

            var stats = snapshot != null && snapshot.MaxHp > 0
                ? new CompanionStats(snapshot.MaxHp, snapshot.MaxMagic, snapshot.Attack, snapshot.Armor)
                : CompanionStatsService.ProjectionStats(role);
            if (snapshot != null)
            {
                stats.SetCurrentMagic(snapshot.CurrentMagic);
                ownerPlayerId = snapshot.OwnerPlayerId;
            }
            var projection = gameObject.AddComponent<ProjectionOtherObj>();
            if (!projection.InitProjection(role, ownerStatusId, slotIndex, stats, statusId, ownerPlayerId))
            {
                UnityEngine.Object.Destroy(gameObject);
                PlayerApi.ShowCaption("拜托了：投影初始化失败。");
                return false;
            }

            ProjectionStateStore.Register(new ProjectionState(
                projection.InstanceId,
                ownerStatusId,
                role.Id,
                role.DisplayName,
                projection,
                slotIndex,
                projection.OwnerPlayerId));
            if (snapshot == null)
            {
                ProjectionBuffCopyService.ApplyInitial(projection.Status, initialBuffs);
                projection.Status.UpdateStatus(true);
                CompanionThreatService.Register(
                    CompanionBattleStateStore.Find(projection.InstanceId)!,
                    projection.Status.MaxHp,
                    projection.Status.Defend,
                    projection.Attack,
                    stats.MaxMagic);
                projection.ActivateAfterHydration(null, source + ".AuthoritativeInit");
            }
            else
            {
                ApplySnapshot(projection, snapshot, source + ".Hydrate");
            }
            PlayerApi.ShowCaption("拜托了：" + role.DisplayName + "的投影加入战斗。");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[Projection] summon failed from " + source, ex);
            PlayerApi.ShowCaption("拜托了：召唤失败。");
            return false;
        }
    }

    private static void BroadcastRejectIfNeeded(string roleId, string ownerStatusId, string token, string reason, bool broadcast, string source)
    {
        if (!broadcast)
        {
            return;
        }

        BroadcastNetworkState(new ProjectionCompanionSnapshot
        {
            Token = token ?? "",
            RoleId = roleId ?? "",
            OwnerStatusId = ownerStatusId ?? "",
            Accepted = false,
            RejectionReason = reason ?? ""
        }, source + ".Reject");
    }

    private static void BroadcastNetworkState(ProjectionCompanionSnapshot snapshot, string source)
    {
        SunExpNetworkRuntime.Send(new RpcProjectionCompanionState(snapshot), source);
    }

    public static void BroadcastRuntimeState(ProjectionOtherObj projection, string source)
    {
        if (projection == null || !SunExpNetworkRuntime.IsMultiplayerSession() || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        BroadcastNetworkState(BuildSnapshot(projection), "ProjectionRuntime." + source);
    }

    private static ProjectionCompanionSnapshot BuildSnapshot(ProjectionOtherObj projection)
    {
        var state = CompanionBattleStateStore.Find(projection.InstanceId);
        return new ProjectionCompanionSnapshot
        {
            ProtocolVersion = 2,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            RegistryHash = CompanionIntentRegistry.RegistryHash,
            Revision = state?.Revision ?? 0,
            Accepted = true,
            RoleId = projection.RoleId,
            OwnerPlayerId = projection.OwnerPlayerId,
            OwnerStatusId = projection.OwnerStatusId,
            StatusId = projection.InstanceId,
            SlotIndex = state?.SlotIndex ?? -1,
            MaxHp = projection.Status?.MaxHp ?? projection.MaxHp,
            CurrentHp = projection.Status?.CurHp ?? projection.CurHp,
            Attack = projection.Attack,
            Armor = projection.Status?.Defend ?? projection.Defend,
            MaxMagic = state?.Stats.MaxMagic ?? 1,
            CurrentMagic = state?.Stats.CurrentMagic ?? 0,
            TurnIndex = state?.TurnIndex ?? 0,
            ReadyOnTurn = state == null
                ? new Dictionary<string, int>()
                : state.ReadyOnTurnSnapshot().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            Buffs = ProjectionBuffCopyService.ReadActual(projection.Status).ToList(),
            Threat = CompanionThreatService.Export(projection.InstanceId),
            IntentPlan = state?.CurrentPlan
        };
    }

    private static void ApplySnapshot(ProjectionOtherObj projection, ProjectionCompanionSnapshot snapshot, string source)
    {
        var state = CompanionBattleStateStore.Find(projection.InstanceId);
        if (state == null || snapshot.Revision < state.Revision)
        {
            return;
        }

        ProjectionBuffCopyService.HydrateExact(projection.Status, snapshot.Buffs, snapshot.Revision);
        state.Stats.SetCurrentMagic(snapshot.CurrentMagic);
        state.ApplyReadyOnTurn(snapshot.ReadyOnTurn);
        state.ApplyRemoteProgress(snapshot.TurnIndex, snapshot.Revision);
        if (projection.Status != null)
        {
            projection.Status.CurHp = Math.Max(0, snapshot.CurrentHp);
            projection.Status.UpdateStatus(true);
        }
        CompanionThreatService.ApplyAuthoritative(snapshot.Threat);
        projection.ActivateAfterHydration(snapshot.IntentPlan, source);
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
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

    private static string ValidateNetworkSender(SunExpRpcSender sender, string ownerStatusId)
    {
        if (!SunExpNetworkRuntime.IsMultiplayerSession())
        {
            return "";
        }

        if (!sender.IsAvailable)
        {
            return "missing sender";
        }

        if (!sender.IsLobbyMember)
        {
            return "sender outside lobby";
        }

        return SenderOwnsStatus(sender.PlayerId, ownerStatusId) ? "" : "owner mismatch";
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

    private static int RealPlayerCount()
    {
        try
        {
            var count = FightManager.Instance?.roleQueue?.Count ?? 0;
            if (count > 0)
            {
                return count;
            }
        }
        catch
        {
            // Fall back to GameEntryUI.
        }

        try
        {
            return Math.Max(1, GameEntryUI.playerCount);
        }
        catch
        {
            return 1;
        }
    }
}
