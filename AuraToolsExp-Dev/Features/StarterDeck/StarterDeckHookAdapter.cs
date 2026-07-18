using System;
using System.Linq;
using Data.Save;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckHookAdapter
{
    internal static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "GameEntryUI.Init", _ =>
        {
            StarterDeckCardCatalog.Invalidate("GameEntryUI.Init");
            StarterDeckCardCatalog.Warm("GameEntryUI.Init");
        });
        RegisterAfter(modConfig, "GameEntryUI.ShowCareer", _ => StarterDeckCardCatalog.Warm("GameEntryUI.ShowCareer"));
        RegisterBefore(modConfig, "GameEntryUI.StartGame", ApplyStarterDeckBeforeGameStart);
        // Each client mutates its owned RoleTable immediately before native submission.
        RegisterBefore(modConfig, "PlayerManager.CmdSyncRoleTable", ApplyStarterDeckBeforeRoleSubmit);
    }

    private static void ApplyStarterDeckBeforeGameStart(ModHookContext context)
    {
        try
        {
            StarterDeckApplicationCoordinator.Apply(RoleTable.Instance, context, "GameEntryUI.StartGame");
        }
        catch (Exception ex)
        {
            Infrastructure.AuraToolsLog.Error("[StarterDeck] failed to reconcile preset before start", ex);
        }
    }

    private static void ApplyStarterDeckBeforeRoleSubmit(ModHookContext context)
    {
        try
        {
            var roleTable = context.Arguments?.OfType<RoleTable>().FirstOrDefault() ?? RoleTable.Instance;
            StarterDeckApplicationCoordinator.Apply(roleTable, context, "PlayerManager.CmdSyncRoleTable");
        }
        catch (Exception ex)
        {
            Infrastructure.AuraToolsLog.Error("[StarterDeck] failed to reconcile preset before local role submission", ex);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action) =>
        Infrastructure.AuraToolsHookRegistry.After(config, target, action, "StarterDeck");

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action) =>
        Infrastructure.AuraToolsHookRegistry.Before(config, target, action, "StarterDeck");
}
