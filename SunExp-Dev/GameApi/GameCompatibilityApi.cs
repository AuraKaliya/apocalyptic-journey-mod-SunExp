using System;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.GameApi;

public static class GameCompatibilityApi
{
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
