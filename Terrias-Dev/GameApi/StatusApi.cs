using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class StatusApi
{
    private const string NativeHealDamageType = "Heal";
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

    public static int Defence(IStatusManager? status)
    {
        return Math.Max(0, status?.Defend ?? ReadInt(status, "Defend"));
    }

    public static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    public static string RoleId(IStatusManager? status)
    {
        if (status == null)
        {
            return "";
        }

        try
        {
            var roleId = status.fatherObject?.Id;
            if (!string.IsNullOrWhiteSpace(roleId))
            {
                return roleId ?? "";
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[StatusApi] status role id fallback used: " + ex.Message);
        }

        var local = FightPlayer.Instance?.Status;
        return local != null
            && (ReferenceEquals(status, local)
                || string.Equals(status.InstanceId, local.InstanceId, StringComparison.Ordinal))
            ? PlayerApi.GetCurrentCareerId()
            : "";
    }

    public static bool TryHeal(IStatusManager? target, int amount)
    {
        if (!IsAlive(target) || amount <= 0)
        {
            return false;
        }

        try
        {
            target!.Heal(amount, NativeHealDamageType);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[StatusApi] native heal failed: target="
                + (target!.InstanceId ?? "")
                + ", amount="
                + amount
                + ", error="
                + ex.Message);
            return false;
        }
    }

    public static bool TryIncreaseMaxHp(IStatusManager? target, int amount)
    {
        if (target == null || amount <= 0)
        {
            return false;
        }

        try
        {
            target.MaxHp = Math.Max(1, target.MaxHp + amount);
            target.UpdateStatus(true);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[StatusApi] max hp increase failed: " + ex.Message);
            return false;
        }
    }

    public static float DynamicFloat(IStatusManager? status, string key, float fallback = 0f)
    {
        if (status == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var value = ReadDynamicFloat(status, key, float.NaN);
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    public static float DynamicMultiplier(IStatusManager? status, string key)
    {
        return Math.Max(0f, DynamicFloat(status, key, 1f));
    }

    public static IStatusManager? FindById(string? statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
    }

    public static bool SetDynamicFloat(IStatusManager? status, string key, float value)
    {
        if (status == null || string.IsNullOrWhiteSpace(key) || float.IsNaN(value) || float.IsInfinity(value))
        {
            return false;
        }

        status.dynamicVariables ??= new Dictionary<string, float>();
        status.dynamicVariables[key] = value;
        return true;
    }

    public static bool TryAddShield(IStatusManager? target, int amount)
    {
        if (!IsAlive(target) || amount <= 0)
        {
            return false;
        }

        try
        {
            target!.Defend = Math.Max(0, target.Defend + amount);
            target.UpdateStatus(true);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[StatusApi] shield grant failed: target="
                + (target!.InstanceId ?? "")
                + ", amount="
                + amount
                + ", error="
                + ex.Message);
            return false;
        }
    }

    public static bool AddDynamicPercent(IStatusManager? status, string key, int percent)
    {
        return AddDynamicFloat(status, key, percent / 100f, enqueue: true);
    }

    public static bool AddDynamicFloat(IStatusManager? status, string key, float delta, bool enqueue = true)
    {
        if (status == null || string.IsNullOrWhiteSpace(key) || Math.Abs(delta) <= float.Epsilon)
        {
            return false;
        }

        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = status.GetType().GetMethod("AddDynamicVariable", flags, null, new[] { typeof(string), typeof(float), typeof(bool) }, null);
            if (method != null)
            {
                method.Invoke(status, new object[] { key, delta, enqueue });
                return true;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Status AddDynamicVariable fallback used: " + ex.Message);
        }

        status.dynamicVariables ??= new Dictionary<string, float>();
        status.dynamicVariables[key] = status.dynamicVariables.TryGetValue(key, out var current)
            ? current + delta
            : delta;
        return true;
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
            TerriasLog.Debug("Native resurrection fallback used by Star Clay Doll: " + ex.Message);
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
            TerriasLog.Debug("Native status.Resurrection unavailable: " + ex.Message);
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
            TerriasLog.Error("Star Clay Doll manual resurrection failed", ex);
            return false;
        }
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

    private static float ReadDynamicFloat(IStatusManager status, string key, float fallback = 0f)
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
                return fallback;
            }
        }

        return fallback;
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
