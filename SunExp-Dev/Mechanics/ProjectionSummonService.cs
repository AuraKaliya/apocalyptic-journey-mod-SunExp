using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class ProjectionSummonService
{
    private const int PartyCap = CompanionSlotService.MaxFriendlySlots;

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

        var currentCount = RealPlayerCount() + ProjectionStateStore.ActiveCount();
        if (currentCount >= PartyCap)
        {
            PlayerApi.ShowCaption("拜托了：场上友方单位已达到4人上限。");
            return false;
        }

        var slotIndex = CompanionSlotService.FindOpenPlayerSlot();
        if (slotIndex == null)
        {
            PlayerApi.ShowCaption("拜托了：没有可用的友方站位。");
            return false;
        }

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
            if (!projection.InitProjection(role, self.Self.InstanceId, slotIndex.Value, stats))
            {
                UnityEngine.Object.Destroy(gameObject);
                PlayerApi.ShowCaption("拜托了：投影初始化失败。");
                return false;
            }

            ProjectionStateStore.Register(new ProjectionState(
                projection.InstanceId,
                self.Self.InstanceId,
                role.Id,
                role.DisplayName,
                projection,
                slotIndex.Value));
            PlayerApi.ShowCaption("拜托了：" + role.DisplayName + "的投影加入战斗。");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[Projection] summon failed", ex);
            PlayerApi.ShowCaption("拜托了：召唤失败。");
            return false;
        }
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
