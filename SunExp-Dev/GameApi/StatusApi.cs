using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class StatusApi
{
    private const string NativeRebirthBuff = "buff_rebirth";

    public static bool HasNativeResurrectionAvailable(IStatusManager? status)
    {
        if (status == null)
        {
            return false;
        }

        return ReadDynamicFloat(status, "liveCount") > 0f
            || BuffApi.Level(status, NativeRebirthBuff) >= 30;
    }

    public static int MaxHp(IStatusManager? status)
    {
        return Math.Max(0, status?.MaxHp ?? ReadInt(status, "MaxHp"));
    }

    public static bool TryStarClayResurrection(IStatusManager? status, int nextMaxHp)
    {
        if (status == null)
        {
            return false;
        }

        var nextMax = Math.Max(1, nextMaxHp);
        try
        {
            status.MaxHp = nextMax;
            if (TryInvokeResurrection(status, 100) && IsAlive(status))
            {
                SyncPlayerRoleTable(status, nextMax);
                return true;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Native resurrection fallback used by Star Clay Doll: " + ex.Message);
        }

        return ManualRestore(status, nextMax);
    }

    private static bool TryInvokeResurrection(IStatusManager status, int percent)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = status.GetType().GetMethod("Resurrection", flags, null, new[] { typeof(int) }, null);
            if (method == null)
            {
                return false;
            }

            method.Invoke(status, new object[] { percent });
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Native status.Resurrection unavailable: " + ex.Message);
            return false;
        }
    }

    private static bool ManualRestore(IStatusManager status, int nextMax)
    {
        try
        {
            status.MaxHp = nextMax;
            status.CurHp = nextMax;
            status.state = IStatusManager.State.Default;
            SyncPlayerRoleTable(status, nextMax);
            status.UpdateStatus(true);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star Clay Doll manual resurrection failed", ex);
            return false;
        }
    }

    private static bool IsAlive(IStatusManager status)
    {
        return status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    private static void SyncPlayerRoleTable(IStatusManager status, int nextMax)
    {
        if (!string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal)
            || RoleTable.Instance == null)
        {
            return;
        }

        RoleTable.Instance.maxSan = nextMax;
        RoleTable.Instance.san = Math.Max(1, status.CurHp);
        RoleTable.Instance.isDead = false;
    }

    private static float ReadDynamicFloat(IStatusManager status, string key)
    {
        var map = Member(status, "dynamicVariables");
        if (map is IDictionary<string, float> typed && typed.TryGetValue(key, out var value))
        {
            return value;
        }

        if (map is IDictionary dictionary && dictionary.Contains(key))
        {
            try
            {
                return Convert.ToSingle(dictionary[key]);
            }
            catch
            {
                return 0f;
            }
        }

        return 0f;
    }

    private static int ReadInt(object? target, string name)
    {
        try
        {
            var value = Member(target, name);
            return value is int intValue ? intValue : DictionaryUtil.ParseInt(Convert.ToString(value));
        }
        catch
        {
            return 0;
        }
    }

    private static object? Member(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return target.GetType().GetProperty(name, flags)?.GetValue(target)
            ?? target.GetType().GetField(name, flags)?.GetValue(target);
    }
}
