using System;
using System.Collections;
using System.Reflection;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

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

    public static string GetGameVar(string key, string fallback = "")
    {
        var value = InvokeStaticPlayerInfo("GetGameVar", key);
        var text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    public static string ScopedGameVarKey(string key, IStatusManager? status)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        var scopeId = SanitizeGameVarKeyPart(status?.InstanceId);
        return string.IsNullOrWhiteSpace(scopeId) ? key : key + "_" + scopeId;
    }

    public static string GetScopedGameVar(string key, IStatusManager? status, string fallback = "", bool migrateLegacyWhenSolo = false)
    {
        var scopedKey = ScopedGameVarKey(key, status);
        if (string.IsNullOrWhiteSpace(scopedKey))
        {
            return fallback;
        }

        if (scopedKey == key)
        {
            return GetGameVar(key, fallback);
        }

        var scopedValue = GetGameVar(scopedKey, "");
        if (!string.IsNullOrWhiteSpace(scopedValue))
        {
            return scopedValue;
        }

        if (migrateLegacyWhenSolo && !IsMultiplayerSession())
        {
            var legacyValue = GetGameVar(key, "");
            if (!string.IsNullOrWhiteSpace(legacyValue))
            {
                SetGameVar(scopedKey, legacyValue);
                return legacyValue;
            }
        }

        return fallback;
    }

    public static void SetScopedGameVar(string key, IStatusManager? status, string value)
    {
        var scopedKey = ScopedGameVarKey(key, status);
        if (string.IsNullOrWhiteSpace(scopedKey))
        {
            return;
        }

        SetGameVar(scopedKey, value);
    }

    public static void ShowCaption(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        InvokeStaticPlayerInfo("ShowCaption", text);
    }

    public static string GetCurrentCareerId()
    {
        try
        {
            return DictionaryUtil.Get(RoleTable.Instance?.Career?.data, "Id");
        }
        catch
        {
            return "";
        }
    }

    public static bool AddMoney(int amount)
    {
        if (amount == 0)
        {
            return true;
        }

        var current = GetStaticMember(PlayerInfo, "Money");
        var next = DictionaryUtil.ParseInt(Convert.ToString(current)) + amount;
        return SetStaticMember(PlayerInfo, "Money", next);
    }

    public static void AddCard(string cardId)
    {
        InvokeStaticPlayerInfo("AddCard", cardId);
    }

    public static void AddRelic(string relicId)
    {
        InvokeStaticPlayerInfo("AddRelic", relicId);
    }

    public static void AddBless(string blessId)
    {
        InvokeStaticPlayerInfo("AddBless", blessId);
    }

    public static void EndEvent()
    {
        InvokeStaticPlayerInfo("EndEvent");
    }

    public static void EventTryChangeMap()
    {
        InvokeStaticPlayerInfo("EventTryChangeMap");
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
            SunExpLog.Warn("PlayerInfo." + methodName + " failed: " + ex.Message);
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

        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    private static object? GetInstanceMember(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        var type = target.GetType();
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }

    public static bool IsMultiplayerSession()
    {
        try
        {
            var entryType = FindType("GameEntryUI");
            var playerCount = GetStaticMember(entryType, "playerCount");
            if (playerCount is int count && count > 1)
            {
                return true;
            }

            var server = GetStaticMember(FindType("GameServer"), "Instance");
            var lobby = GetInstanceMember(server, "LobbyInfo");
            var players = GetInstanceMember(lobby, "AddedPlayers") as ICollection;
            return players != null && players.Count > 1;
        }
        catch
        {
            return false;
        }
    }

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == name || type.FullName == name)
                    {
                        return type;
                    }
                }
            }
            catch
            {
                // Some runtime assemblies can reject GetTypes; skip them.
            }
        }

        return null;
    }

    private static string SanitizeGameVarKeyPart(string? value)
    {
        if (value == null)
        {
            return "";
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        var chars = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            chars[i] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_';
        }

        return new string(chars);
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
            SunExpLog.Warn("PlayerInfo." + name + " set failed: " + ex.Message);
        }

        return false;
    }
}
