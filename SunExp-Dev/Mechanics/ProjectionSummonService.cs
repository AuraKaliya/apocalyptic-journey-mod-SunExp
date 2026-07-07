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

    public static void ResolveNetworkSummon(string roleId, string ownerStatusId, string token, SunExpRpcSender sender)
    {
        if (!ClaimToken(token))
        {
            return;
        }

        var role = PolymorphRoleRegistry.Find(roleId);
        var rejection = ValidateNetworkSender(sender, ownerStatusId);
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

        TrySummonLocal(ownerStatusId, role!, "ProjectionSummonService.ResolveNetworkSummon", broadcast: true, token: token);
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

        if (ProjectionStateStore.Find(snapshot.StatusId) != null)
        {
            return;
        }

        SpawnProjection(role, snapshot.OwnerStatusId, snapshot.SlotIndex, snapshot.StatusId, source);
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

    private static bool TrySummonLocal(string ownerStatusId, PolymorphRoleSpec role, string source, bool broadcast, string token = "")
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
        var spawned = SpawnProjection(role, ownerStatusId, slotIndex.Value, statusId, source);
        if (spawned && broadcast)
        {
            BroadcastNetworkState(new ProjectionCompanionSnapshot
            {
                Token = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token,
                RoleId = role.Id,
                OwnerStatusId = ownerStatusId ?? "",
                StatusId = statusId,
                SlotIndex = slotIndex.Value,
                Accepted = true
            }, source);
        }

        return spawned;
    }

    private static bool SpawnProjection(PolymorphRoleSpec role, string ownerStatusId, int slotIndex, string statusId, string source)
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

            var stats = CompanionStatsService.ProjectionStats(role);
            var projection = gameObject.AddComponent<ProjectionOtherObj>();
            if (!projection.InitProjection(role, ownerStatusId, slotIndex, stats, statusId))
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
                slotIndex));
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
