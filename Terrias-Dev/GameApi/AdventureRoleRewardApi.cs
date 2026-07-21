using System;
using Data.Save;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class AdventureRoleRewardApi
{
    public static bool AddMaxHp(int amount, string source)
    {
        if (amount <= 0)
        {
            return false;
        }

        try
        {
            var role = RoleTable.Instance;
            if (role == null)
            {
                return false;
            }

            var oldMax = Math.Max(1, role.MaxSan);
            role.MaxSan = oldMax + amount;
            role.isDead = false;
            GameSaveManager.UpdateRoles(role);
            RefreshRoleUi(role, "MaxHp:" + source);
            TerriasLog.Info("[AdventureRoleReward] max hp +" + amount + " from " + source + ": " + oldMax + "->" + role.MaxSan + ".");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AdventureRoleReward] max hp failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool AddOrigin(string key, int amount, string source)
    {
        if (string.IsNullOrWhiteSpace(key) || amount == 0)
        {
            return false;
        }

        try
        {
            var role = RoleTable.Instance;
            if (role?.VarsMap == null || !role.VarsMap.ContainsKey(key))
            {
                return false;
            }

            var before = role.VarsMap[key];
            role.UseVarsChanges(key, amount);
            var after = role.VarsMap[key];
            GameSaveManager.UpdateRoles(role);
            RefreshRoleUi(role, "Origin:" + source);
            TerriasLog.Info("[AdventureRoleReward] origin " + key + " +" + amount + " from " + source + ": " + before + "->" + after + ".");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AdventureRoleReward] origin failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static void RefreshRoleUi(RoleTable role, string source)
    {
        try
        {
            var topBar = UIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            topBar?.ChangeSan(role.Id);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[AdventureRoleReward] TopBarUI refresh skipped from " + source + ": " + ex.Message);
        }

        try
        {
            UIManager.Instance?.GetUI<MapSelectUI>("MapSelectUI")?.DataUpdate();
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[AdventureRoleReward] MapSelectUI refresh skipped from " + source + ": " + ex.Message);
        }

        try
        {
            foreach (var statusUi in Resources.FindObjectsOfTypeAll<StatusUI>())
            {
                statusUi?.DataUpdate();
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[AdventureRoleReward] StatusUI refresh skipped from " + source + ": " + ex.Message);
        }
    }
}
