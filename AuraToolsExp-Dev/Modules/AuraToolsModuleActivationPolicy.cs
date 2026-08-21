using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Features.AutoBattle;
using AuraToolsExp.Dll.Features.AdventureArchive;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Features.LobbyStatus;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Features.Skin;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Modules;

internal static class AuraToolsModuleActivationPolicy
{
    private static readonly IReadOnlyDictionary<string, string[]> HookOwners =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [AuraToolModuleIds.FileLogging] = new[] { "FileLogging" },
            [AuraToolModuleIds.Skin] = new[] { "Skin" },
            [AuraToolModuleIds.StarterDeck] = new[] { "StarterDeck" },
            [AuraToolModuleIds.Feast] = new[] { "Feast" },
            [AuraToolModuleIds.SafeBox] = new[] { "SafeBox" },
            [AuraToolModuleIds.CardRefresh] = new[] { "CardRefresh" },
            [AuraToolModuleIds.PixelEmoji] = new[] { "PixelEmoji" },
            [AuraToolModuleIds.AutoBattle] = new[]
            {
                "AutoBattle",
                "AutoBattleJourneyTraining"
            },
            [AuraToolModuleIds.ModSync] = new[] { "ModSync" },
            [AuraToolModuleIds.LobbyStatus] = new[] { "LobbyStatus" },
            [AuraToolModuleIds.AdventureArchive] = new[]
            {
                "AdventureArchive"
            }
        };

    internal static IDisposable Activate(string moduleId)
    {
        Apply(moduleId, true);
        return new ActivationLease(moduleId);
    }

    internal static void Deactivate(string moduleId)
    {
        Apply(moduleId, false);
    }

    private static void Apply(string moduleId, bool enabled)
    {
        if (HookOwners.TryGetValue(moduleId ?? "", out var owners))
        {
            for (var i = 0; i < owners.Length; i++)
            {
                AuraToolsHookRegistry.SetOwnerActive(owners[i], enabled);
            }
        }

        switch (moduleId)
        {
            case AuraToolModuleIds.Skin:
                AuraToolsSkinRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.AutoBattle:
                AuraToolsAutoBattleRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.BattleReplay:
            case AuraToolModuleIds.DamageStatistics:
                AuraToolsMatchRecordsRuntime.ApplyModuleActivation();
                break;
            case AuraToolModuleIds.ModSync:
                AuraToolsModSyncRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.LobbyStatus:
                LobbyStatusRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.PixelEmoji:
                AuraToolsPixelEmojiRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.StarterDeck:
                AuraToolsStarterDeckRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.CardRefresh:
                AuraToolsCardRefreshRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.Feast:
                AuraToolsFeastRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.SafeBox:
                AuraToolsSafeBoxRuntime.ApplyModuleActivation(enabled);
                break;
            case AuraToolModuleIds.AdventureArchive:
                AdventureArchiveRuntime.ApplyModuleActivation(enabled);
                break;
        }
    }

    private sealed class ActivationLease : IDisposable
    {
        private string moduleId;

        public ActivationLease(string moduleId)
        {
            this.moduleId = moduleId ?? "";
        }

        public void Dispose()
        {
            if (moduleId.Length == 0) return;
            var current = moduleId;
            moduleId = "";
            Apply(current, false);
        }
    }
}
