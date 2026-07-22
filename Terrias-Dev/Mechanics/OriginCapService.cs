using System;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class OriginCapState
{
    public int Main { get; set; }
    public int Secondary { get; set; }
    public int Other { get; set; }
}

public static class OriginCapService
{
    public const int FateStarIncrease = 10;

    public static OriginCapState Capture(RoleTable? role)
    {
        return role == null
            ? new OriginCapState()
            : new OriginCapState
            {
                Main = Math.Max(0, role.MainVarUpperBound),
                Secondary = Math.Max(0, role.SecondaryVarUpperBound),
                Other = Math.Max(0, role.OtherVarUpperBound)
            };
    }

    public static bool TryIncreaseCurrent(int amount, string source, out OriginCapState state)
    {
        return TryIncrease(RoleTable.Instance, amount, source, out state);
    }

    public static bool TryIncrease(RoleTable? role, int amount, string source, out OriginCapState state)
    {
        state = Capture(role);
        if (role == null || amount <= 0)
        {
            return false;
        }

        try
        {
            role.MainVarUpperBound = AddClamped(role.MainVarUpperBound, amount);
            role.SecondaryVarUpperBound = AddClamped(role.SecondaryVarUpperBound, amount);
            role.OtherVarUpperBound = AddClamped(role.OtherVarUpperBound, amount);
            state = Capture(role);
            Persist(role, source);
            TerriasLog.Info("[OriginCap] all origin caps +"
                + amount
                + " from "
                + source
                + "; main="
                + state.Main
                + "; secondary="
                + state.Secondary
                + "; other="
                + state.Other
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[OriginCap] increase failed from " + source + ": " + ex.Message);
            state = Capture(role);
            return false;
        }
    }

    public static bool ApplyAuthoritativeCurrent(OriginCapState? state, string source)
    {
        var role = RoleTable.Instance;
        if (role == null || state == null || state.Main <= 0 || state.Secondary <= 0 || state.Other <= 0)
        {
            return false;
        }

        try
        {
            var nextMain = Math.Max(role.MainVarUpperBound, state.Main);
            var nextSecondary = Math.Max(role.SecondaryVarUpperBound, state.Secondary);
            var nextOther = Math.Max(role.OtherVarUpperBound, state.Other);
            var changed = role.MainVarUpperBound != nextMain
                || role.SecondaryVarUpperBound != nextSecondary
                || role.OtherVarUpperBound != nextOther;
            role.MainVarUpperBound = nextMain;
            role.SecondaryVarUpperBound = nextSecondary;
            role.OtherVarUpperBound = nextOther;
            if (changed)
            {
                Persist(role, source);
            }

            return changed;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[OriginCap] authoritative apply failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static RoleTable? ResolveAuthoritativeRole(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return RoleTable.Instance;
        }

        try
        {
            var serverRoles = global::GameServer.Instance?.RoleTables;
            if (serverRoles != null && serverRoles.TryGetValue(playerId, out var serverRole) && serverRole != null)
            {
                return serverRole;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[OriginCap] server role lookup failed: " + ex.Message);
        }

        try
        {
            var roles = GameSaveManager.GetRoleTables();
            if (roles != null && roles.TryGetValue(playerId, out var savedRole) && savedRole != null)
            {
                return savedRole;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[OriginCap] saved role lookup failed: " + ex.Message);
        }

        var current = RoleTable.Instance;
        return current != null && string.Equals(current.Id, playerId, StringComparison.Ordinal)
            ? current
            : null;
    }

    public static void ShowIncreaseCaption(OriginCapState state)
    {
        PlayerApi.ShowCaption("四大本源上限 +10 · 主"
            + state.Main
            + " / 次"
            + state.Secondary
            + " / 其他"
            + state.Other);
    }

    private static int AddClamped(int current, int amount)
    {
        return (int)Math.Min(int.MaxValue, Math.Max(0L, (long)current) + amount);
    }

    private static void Persist(RoleTable role, string source)
    {
        GameSaveManager.UpdateRoles(role);
        if (ReferenceEquals(role, RoleTable.Instance))
        {
            AdventureRoleRewardApi.RefreshRoleUi(role, "OriginCap:" + source);
        }
    }
}
