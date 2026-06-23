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
        RegisterAfter(modConfig, "PlayerManager.CreateChatPanel", _ => AuraChatUi.EnsureAvailable("PlayerManager.CreateChatPanel"));
        RegisterAfter(modConfig, "GameEntryUI.Init", _ => AuraChatUi.SetAvailable(false, "GameEntryUI.Init"));
        RegisterAfter(modConfig, "GameEntryUI.ShowCareer", _ => AuraChatUi.SetAvailable(false, "GameEntryUI.ShowCareer"));
        RegisterAfter(modConfig, "GameEntryUI.ShowDetail", _ => AuraChatUi.SetAvailable(false, "GameEntryUI.ShowDetail"));
        RegisterAfter(modConfig, "GameEntryUI.UpdateLobby", UpdateModSyncStatus);
        RegisterBefore(modConfig, "UIManager.CloseUI", HideOnUiManagerClose);
        RegisterBefore(modConfig, "UIBase.Close", context => HideOnUiBaseClose(context, "UIBase.Close"));
        RegisterBefore(modConfig, "UIBase.OnDestroy", context => HideOnUiBaseClose(context, "UIBase.OnDestroy"));
    }

    private static void UpdateModSyncStatus(ModHookContext context)
    {
        var players = GetPlayersFromContext(context);
        if (players.Count > 0)
        {
            var localPlayerId = PlayerManager.Instance?.PlayerId ?? "";
            var state = AuraChatModSyncSnapshot.BuildState(players, ChatExpIds.ModId, localPlayerId);
            AuraChatRuntime.SetModSyncStatus(AuraChatModSyncSnapshot.FormatStatus(state), state);
            AuraChatUi.EnsureAvailable("GameEntryUI.UpdateLobby");
            return;
        }

        AuraChatRuntime.SetModSyncStatus(AuraChatModSyncSnapshot.FormatStatus(null), null);
        AuraChatUi.SetAvailable(false, "GameEntryUI.UpdateLobby:no-players");
    }

    private static void HideOnUiManagerClose(ModHookContext context)
    {
        var uiName = GetArgumentString(context, 0);
        HideIfAdventureUiIsLeaving(uiName, "UIManager.CloseUI");
    }

    private static void HideOnUiBaseClose(ModHookContext context, string source)
    {
        var uiName = GetTargetName(context);
        HideIfAdventureUiIsLeaving(uiName, source);
    }

    private static void HideIfAdventureUiIsLeaving(string uiName, string source)
    {
        if (!string.Equals(uiName, "GameEntryUI", StringComparison.Ordinal)
            && !string.Equals(uiName, "GameExitUI", StringComparison.Ordinal))
        {
            return;
        }

        AuraChatUi.SetAvailable(false, source + ":" + uiName);
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

    private static string GetArgumentString(ModHookContext context, int index)
    {
        try
        {
            if (context.Arguments == null || context.Arguments.Length <= index)
            {
                return "";
            }

            return context.Arguments[index]?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string GetTargetName(ModHookContext context)
    {
        try
        {
            if (context.Target is UnityEngine.Component component)
            {
                return component.gameObject.name;
            }

            return context.Target?.GetType().Name ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, ChatExpLog.Info, ChatExpLog.Warn, safeInvoke: true);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, ChatExpLog.Info, ChatExpLog.Warn, safeInvoke: true);
    }
}
