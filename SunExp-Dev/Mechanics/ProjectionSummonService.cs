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
    private const int PartyCap = 4;

    public static bool TrySummon(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self?.Self == null || role == null)
        {
            PlayerApi.ShowCaption("魔女投影：召唤失败。");
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            PlayerApi.ShowCaption("魔女投影：只能在战斗中召唤。");
            return false;
        }

        var currentCount = RealPlayerCount() + ProjectionStateStore.ActiveCount();
        if (currentCount >= PartyCap)
        {
            PlayerApi.ShowCaption("魔女投影：场上友方单位已达到4人上限。");
            return false;
        }

        try
        {
            var prefab = SunExpResourceCache.Load<GameObject>("Model/player", true, "projection");
            if (prefab == null)
            {
                PlayerApi.ShowCaption("魔女投影：投影模型加载失败。");
                return false;
            }

            var gameObject = UnityEngine.Object.Instantiate(prefab);
            if (gameObject == null)
            {
                PlayerApi.ShowCaption("魔女投影：投影模型加载失败。");
                return false;
            }

            var projection = gameObject.AddComponent<ProjectionOtherObj>();
            if (!projection.InitProjection(role, self.Self.InstanceId, ProjectionStateStore.ActiveCount()))
            {
                UnityEngine.Object.Destroy(gameObject);
                PlayerApi.ShowCaption("魔女投影：投影初始化失败。");
                return false;
            }

            ProjectionStateStore.Register(new ProjectionState(
                projection.InstanceId,
                self.Self.InstanceId,
                role.Id,
                role.DisplayName,
                projection));
            PlayerApi.ShowCaption("魔女投影：" + role.DisplayName + "的投影加入战斗。");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[Projection] summon failed", ex);
            PlayerApi.ShowCaption("魔女投影：召唤失败。");
            return false;
        }
    }

    public static DataConfig CreateProjectionDataConfig(PolymorphRoleSpec role)
    {
        var data = new Dictionary<string, string>(new DataConfig(role.Id, DataType.Career).data);
        var vars = new Dictionary<string, string>();
        var name = role.DisplayName + "的投影";
        data["Id"] = role.Id;
        data["Name"] = name;
        data["Name_zh-Hant"] = role.DisplayName + "的投影";
        data["Name_en"] = role.DisplayName + " Projection";
        data["Name_ja"] = role.DisplayName + "の投影";
        data["Attack"] = "0";
        data["Defend"] = "0";
        data["Hp"] = ProjectionStrategyService.ProjectionMaxHp(role).ToString();
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

    public static void PositionProjection(ProjectionOtherObj projection, int index)
    {
        var status = projection.Status;
        if (status == null)
        {
            return;
        }

        var owner = FightPlayer.Instance?.Status;
        var ownerX = owner?.transform?.position.x ?? -3.5f;
        var x = ownerX - 1.15f - Math.Max(0, index) * 0.65f;
        var groundY = 0f;
        try
        {
            groundY = GameApp.Instance.NowBackground.transform.Find("com").GetComponent<SceneInfo>().ground_y;
        }
        catch
        {
            groundY = owner?.transform?.position.y ?? 0f;
        }

        var bottom = projection.gameObject.transform.Find("bottom");
        var bottomOffset = bottom == null ? 0f : bottom.localPosition.y;
        status.SetPosition(new Vector3(x, groundY - bottomOffset, 0f));
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
