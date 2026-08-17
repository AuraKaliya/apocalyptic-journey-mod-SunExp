using System;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayChatUiHookAdapter
{
    private static IDisposable? createChatPanelHook;
    private static IDisposable? addMessageHook;

    internal static void Initialize(ModConfig modConfig)
    {
        if (createChatPanelHook != null || addMessageHook != null)
        {
            return;
        }

        createChatPanelHook = AuraToolsHookRegistry.AfterRouted(
            modConfig,
            "PlayerManager.CreateChatPanel",
            _ => MatchReplayChatUiLeaseRuntime.OnNativeChatPanelReady("PlayerManager.CreateChatPanel"),
            "MatchRecords.Replay.ChatUI");
        addMessageHook = AuraToolsHookRegistry.AfterRouted(
            modConfig,
            "ChatUI.AddMessage",
            _ => MatchReplayChatUiLeaseRuntime.ReassertQuarantine("ChatUI.AddMessage"),
            "MatchRecords.Replay.ChatUI");
        AuraToolsLog.Info("[MatchRecords] replay ChatUI lifecycle hooks enabled.");
    }
}
