using System;
using System.Reflection;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll.GameApi;

public static class PlayerApi
{
    private static object? PlayerInfo => typeof(ScriptExecutor).GetNestedType("PlayerInfo", BindingFlags.Public | BindingFlags.NonPublic);

    public static int GetMoney()
    {
        return TryGetMoney(out var money) ? money : 0;
    }

    public static bool TryGetMoney(out int money)
    {
        return TryReadStaticInt(PlayerInfo, "Money", out money);
    }

    public static bool SetMoney(int value)
    {
        return SetStaticMember(PlayerInfo, "Money", Math.Max(0, value));
    }

    public static bool AddMoneyRaw(int amount)
    {
        return SetMoney(GetMoney() + amount);
    }

    public static int GetSkillTime(string key)
    {
        var skillTime = GetStaticMember(PlayerInfo, "SkillTime");
        if (skillTime == null || string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        var contains = skillTime.GetType().GetMethod("ContainsKey")?.Invoke(skillTime, new object[] { key }) as bool?;
        if (contains != true)
        {
            skillTime.GetType().GetMethod("set_Item")?.Invoke(skillTime, new object[] { key, 0 });
            return 0;
        }

        var value = skillTime.GetType().GetMethod("get_Item")?.Invoke(skillTime, new object[] { key });
        return value is int intValue ? intValue : DictionaryUtil.ParseInt(Convert.ToString(value));
    }

    public static void SetSkillTime(string key, int value)
    {
        var skillTime = GetStaticMember(PlayerInfo, "SkillTime");
        if (skillTime == null || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        skillTime.GetType().GetMethod("set_Item")?.Invoke(skillTime, new object[] { key, Math.Max(0, value) });
    }

    public static void SetGameVar(string key, string value)
    {
        InvokeStaticPlayerInfo("SetGameVar", key, value);
    }

    public static string GetGameVar(string key, string fallback = "")
    {
        var value = InvokeStaticPlayerInfo("GetGameVar", key);
        var text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    public static void ShowCaption(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            InvokeStaticPlayerInfo("ShowCaption", text);
        }
    }

    private static int ReadStaticInt(object? typeObject, string name)
    {
        return TryReadStaticInt(typeObject, name, out var value) ? value : 0;
    }

    private static bool TryReadStaticInt(object? typeObject, string name, out int result)
    {
        var value = GetStaticMember(typeObject, name);
        if (value == null)
        {
            result = 0;
            return false;
        }

        if (value is int intValue)
        {
            result = intValue;
            return true;
        }

        result = DictionaryUtil.ParseInt(Convert.ToString(value));
        return true;
    }

    private static object? InvokeStaticPlayerInfo(string methodName, params object[] args)
    {
        var playerInfo = PlayerInfo as Type;
        if (playerInfo == null)
        {
            return null;
        }

        try
        {
            return playerInfo.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, args);
        }
        catch (Exception ex)
        {
            GoldExpLog.Warn("PlayerInfo." + methodName + " failed: " + ex.Message);
            return null;
        }
    }

    private static object? GetStaticMember(object? typeObject, string name)
    {
        var type = typeObject as Type;
        if (type == null)
        {
            return null;
        }

        try
        {
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }
        catch (Exception ex)
        {
            GoldExpLog.Debug("PlayerInfo." + name + " read skipped: " + ex.Message);
            return null;
        }
    }

    private static bool SetStaticMember(object? typeObject, string name, object value)
    {
        var type = typeObject as Type;
        if (type == null)
        {
            return false;
        }

        try
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                property.SetValue(null, Convert.ChangeType(value, property.PropertyType));
                return true;
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null)
            {
                field.SetValue(null, Convert.ChangeType(value, field.FieldType));
                return true;
            }
        }
        catch (Exception ex)
        {
            GoldExpLog.Warn("PlayerInfo." + name + " set failed: " + ex.Message);
        }

        return false;
    }
}
