using System;
using System.Collections.Generic;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.GameApi;

public static class GameCompatibilityApi
{
    private static readonly MethodInfo? CurrentGetItemsByPack = typeof(GameConfigManager).GetMethod(
        "GetItemsByPack",
        BindingFlags.Public | BindingFlags.Instance,
        null,
        new[] { typeof(DataType), typeof(string), typeof(bool) },
        null);

    private static readonly MethodInfo? LegacyGetItemsByPack = typeof(GameConfigManager).GetMethod(
        "GetItemsByPack",
        BindingFlags.Public | BindingFlags.Instance,
        null,
        new[] { typeof(DataType), typeof(string) },
        null);

    public static bool ShouldEnableOnlineCardPack()
    {
        try
        {
            var method = typeof(GameConfigManager).GetMethod(
                "ShouldEnableOnlineCardPack",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (method?.ReturnType == typeof(bool))
            {
                return (bool)method.Invoke(null, null);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("ShouldEnableOnlineCardPack reflection fallback used: " + ex.Message);
        }

        return true;
    }

    public static void StartLobby()
    {
        var lobby = LobbyManager.Instance;
        if (lobby == null)
        {
            return;
        }

        var useSteamLobby = ShouldUseSteamLobby();
        if (!useSteamLobby)
        {
            if (!TryInvoke(lobby, "StartLocalHost"))
            {
                TryInvoke(lobby, "StartHost");
            }

            return;
        }

        if (!TryInvoke(lobby, "TryCreateSteamLobby", 4))
        {
            TryInvoke(lobby, "StartHost");
        }
    }

    public static List<Dictionary<string, string>> GetItemsByPack(
        DataType type,
        string packId,
        bool includeLocked = false)
    {
        var manager = Singleton<GameConfigManager>.Instance;
        if (manager == null)
        {
            return new List<Dictionary<string, string>>();
        }

        var current = TryGetItemsByPack(
            manager,
            CurrentGetItemsByPack,
            new object[] { type, packId, includeLocked });
        if (current != null)
        {
            return current;
        }

        if (!includeLocked)
        {
            var legacy = TryGetItemsByPack(
                manager,
                LegacyGetItemsByPack,
                new object[] { type, packId });
            if (legacy != null)
            {
                return legacy;
            }
        }

        return GetItemsByPackFallback(manager, type, packId, includeLocked);
    }

    private static bool ShouldUseSteamLobby()
    {
        try
        {
            var method = typeof(LobbyManager).GetMethod(
                "ShouldUseSteamLobby",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            return method?.ReturnType == typeof(bool) && (bool)method.Invoke(null, null);
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("ShouldUseSteamLobby reflection fallback used: " + ex.Message);
            return false;
        }
    }

    private static List<Dictionary<string, string>>? TryGetItemsByPack(
        GameConfigManager manager,
        MethodInfo? method,
        object[] args)
    {
        if (method == null)
        {
            return null;
        }

        try
        {
            if (method.Invoke(manager, args) is IEnumerable<Dictionary<string, string>> items)
            {
                return new List<Dictionary<string, string>>(items);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("GetItemsByPack compatibility call failed: " + ex.GetBaseException().Message);
        }

        return null;
    }

    private static List<Dictionary<string, string>> GetItemsByPackFallback(
        GameConfigManager manager,
        DataType type,
        string packId,
        bool includeLocked)
    {
        var result = new List<Dictionary<string, string>>();
        var table = manager.GetTable(type);
        if (table == null)
        {
            return result;
        }

        var targetPackId = string.IsNullOrWhiteSpace(packId) ? "cardpack_1" : packId.Trim();
        foreach (var item in table.Getlines())
        {
            if (item == null)
            {
                continue;
            }

            var itemPackId = item.TryGetValue("PackBelong", out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : "cardpack_1";
            if (!string.Equals(itemPackId, targetPackId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!includeLocked
                && item.TryGetValue("Id", out var id)
                && !string.IsNullOrWhiteSpace(id)
                && Singleton<GameRuntimeData>.Instance.IsLocked(id))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static bool TryInvoke(object target, string methodName, params object[] args)
    {
        try
        {
            var types = new Type[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                types[i] = args[i].GetType();
            }

            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                types,
                null);
            if (method == null)
            {
                return false;
            }

            method.Invoke(target, args);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Lobby method failed: " + methodName + " -> " + ex.Message);
            return false;
        }
    }
}
