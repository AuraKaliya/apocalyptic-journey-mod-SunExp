using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterAvailabilityRuntime
{
    internal static bool Available { get; private set; }
    internal static bool PreparationUiActive { get; set; }

    internal static void SetAvailable(bool available, string reason)
    {
        if (Available == available)
        {
            AuraToolsDamageMeterUi.SetAvailable(available && AuraToolsDamageMeterRuntime.Enabled);
            return;
        }

        Available = available;
        if (available)
        {
            AuraToolsDamageMeterRuntime.SetVisibleFromAvailability(false);
        }
        else
        {
            AuraToolsDamageMeterRuntime.SetVisibleFromAvailability(false);
            PreparationUiActive = false;
            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterUi.CloseHistory();
        }

        AuraToolsDamageMeterUi.SetAvailable(available && AuraToolsDamageMeterRuntime.Enabled);
        AuraToolsDamageMeterUi.SetVisible(available && AuraToolsDamageMeterRuntime.Enabled && AuraToolsDamageMeterRuntime.Visible);
        AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        AuraToolsLog.Info("[DamageMeter] floating UI availability=" + available + "; reason=" + reason + ".");
    }

    internal static void HideForEntryUi(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("entry UI hidden scope", () =>
        {
            DamageSettlementCgRuntime.BeginAdventure();
            SetAvailable(false, GetHookName(context));
        });
    }

    internal static void ShowForPreparationUi(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("preparation UI scope", () =>
        {
            if (!IsSupportedDamageMeterLobby())
            {
                SetAvailable(false, GetHookName(context) + ":unsupported-mode");
                return;
            }

            PreparationUiActive = true;
            SetAvailable(true, GetHookName(context));
        });
    }

    internal static void ShowForStartGame(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("start game UI scope", () =>
        {
            if (IsSupportedDamageMeterContext(context, allowMapManagerFallback: false))
            {
                DamageMeterNetworkRuntime.BeginAdventure();
                DamageMeterSettlementRuntime.CaptureAdventureTeamMembers();
                DamageSettlementCgRuntime.BeginAdventure();
                DamageMeterSettlementRuntime.BeginAdventure();

                AuraToolsDamageMeterUi.CloseHistory();
                PreparationUiActive = true;
                SetAvailable(true, "GameEntryUI.StartGame");
            }
        });
    }

    internal static void ShowForAdventureUi(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("adventure UI scope", () =>
        {
            if (!IsSupportedDamageMeterContext(context, allowMapManagerFallback: true))
            {
                return;
            }

            PreparationUiActive = false;
            DamageMeterSettlementRuntime.RestoreAdventureHistoryOnce();
            SetAvailable(true, GetHookName(context));
            DamageMeterSettlementRuntime.PrepareSettlementCgAssets(GetHookName(context));
        });
    }

    internal static void ReconcileAvailabilitySafe()
    {
        try
        {
            if (Available && !IsActiveDamageMeterContext())
            {
                SetAvailable(false, "context-lost");
            }
        }
        catch (Exception ex)
        {
            AuraToolsDamageMeterRuntime.LogUiFailure("availability reconcile", ex);
        }
    }

    internal static bool IsActiveDamageMeterContext()
    {
        return PreparationUiActive && IsSupportedDamageMeterLobby()
               || IsSupportedDamageMeterAdventureContext();
    }

    internal static bool IsSupportedDamageMeterContext(ModHookContext context, bool allowMapManagerFallback)
    {
        var modeType = ReadLobbyModeType();
        if (!string.IsNullOrWhiteSpace(modeType))
        {
            return IsSupportedModeType(modeType);
        }

        if (IsSupportedModeManager(context.Target))
        {
            return true;
        }

        if (IsSupportedDamageMeterAdventureContext())
        {
            return true;
        }

        return allowMapManagerFallback && IsSupportedModeManager(MapManager.Instance?.ModeMapManager);
    }

    internal static bool IsSupportedDamageMeterLobby()
    {
        return IsSupportedModeType(ReadLobbyModeType());
    }

    internal static bool IsSupportedDamageMeterAdventureContext()
    {
        try
        {
            return IsSupportedModeManager(MapManager.Instance?.ModeMapManager);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsSupportedModeType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "Normal", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "Sublimation", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "Slot", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSupportedModeManager(object? value)
    {
        var name = value?.GetType().Name ?? "";
        return string.Equals(name, "NormalMapManager", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "SublimationManager", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "SlotMachineManager", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ReadLobbyModeType()
    {
        try
        {
            return LobbyManager.Instance?.CurrentLobbyModeType ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static string GetHookName(ModHookContext context)
    {
        try
        {
            return context.Target?.GetType().Name ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
