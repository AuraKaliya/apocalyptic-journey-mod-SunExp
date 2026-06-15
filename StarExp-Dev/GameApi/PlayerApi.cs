using System;
using System.Reflection;
using StarExp.Dll.Infrastructure;

namespace StarExp.Dll.GameApi;

public static class PlayerApi
{
    private static object? PlayerInfo => typeof(ScriptExecutor).GetNestedType("PlayerInfo", BindingFlags.Public | BindingFlags.NonPublic);

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

    public static void ShowCaption(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            InvokeStaticPlayerInfo("ShowCaption", text);
        }
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
            StarExpLog.Warn("PlayerInfo." + methodName + " failed: " + ex.Message);
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
            StarExpLog.Debug("PlayerInfo." + name + " read skipped: " + ex.Message);
            return null;
        }
    }
}
