using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraOnline.Shared;
using AuraShared.Core;
using ChatExp.Dll.Infrastructure;
using ChatExp.Dll.UI;
using Witch.Core;
using Witch.Mod;

namespace ChatExp.Dll.Hooks;

public static class ChatExpRuntimeHooks
{
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        RegisterAfter(modConfig, "PlayerManager.CreateChatPanel", _ => AuraChatUi.Ensure());
        RegisterAfter(modConfig, "GameEntryUI.UpdateLobby", UpdateModSyncStatus);
    }

    private static void UpdateModSyncStatus(ModHookContext context)
    {
        var players = GetPlayersFromContext(context);
        if (players.Count > 0)
        {
            AuraChatRuntime.SetModSyncStatus(AuraChatModSyncSnapshot.BuildStatus(players, ChatExpIds.ModId));
        }

        AuraChatUi.Ensure();
    }

    private static IReadOnlyList<object> GetPlayersFromContext(ModHookContext context)
    {
        if (context.Arguments == null || context.Arguments.Length == 0)
        {
            return Array.Empty<object>();
        }

        if (context.Arguments[0] is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().ToList();
        }

        return Array.Empty<object>();
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, ChatExpLog.Info, ChatExpLog.Warn, safeInvoke: true);
    }
}
