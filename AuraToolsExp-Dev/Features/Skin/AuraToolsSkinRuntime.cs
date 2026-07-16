using System;
using AuraShared.Core;
using AuraSkin.Shared;
using AuraSkin.Shared.Mechanics;
using AuraSkin.Shared.Models;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.Skin;

public static class AuraToolsSkinRuntime
{
    private const string OfficialCareerId = "career_1";
    private const string LegacyOfficialSummerSkinId = "SkinExp.career_1.summer_cool";
    private const string OfficialSummerSkinId = "AuraToolsExp.career_1.summer_cool";
    private static ModConfig? currentConfig;
    private static bool initialized;
    private static string lastInstallStatus = "Skin package not installed yet.";

    public static void Initialize(ModConfig modConfig)
    {
        currentConfig = modConfig;
        AuraSkinRuntime.Initialize(modConfig, AuraToolsIds.ModId);
        ConfigureFromSettings();
        if (!initialized)
        {
            initialized = true;
            SkinRuntime.LocalSelectionChanged += OnLocalSelectionChanged;
            AuraToolsConfigService.Changed += ConfigureFromSettings;
        }

        RegisterHooks(modConfig);
        RegisterBundledPackage();
    }

    public static void RegisterBundledPackage()
    {
        if (currentConfig == null)
        {
            lastInstallStatus = "Skin package install skipped: ModConfig is unavailable.";
            return;
        }

        if (!AuraToolsConfigService.Root.Skin.Enabled
            || !AuraToolsConfigService.Skin.Enabled)
        {
            lastInstallStatus = "Bundled skin package install is disabled.";
            return;
        }

        var registered = AuraSkinRuntime.RegisterPackage(currentConfig, AuraToolsIds.ModId);
        lastInstallStatus = registered
            ? "Bundled skin package registered."
            : "Bundled skin package was rejected.";
        if (!registered)
        {
            AuraToolsLog.Warn("[Skin] bundled skin package was rejected.");
            return;
        }

        if (SkinRuntime.TryRemapSelection(OfficialCareerId, LegacyOfficialSummerSkinId, OfficialSummerSkinId))
        {
            AuraToolsLog.Info("[Skin] migrated legacy official summer skin selection to AuraToolsExp ownership.");
            BroadcastLocalSelection();
        }
    }

    public static bool Reload()
    {
        if (currentConfig == null)
        {
            return false;
        }

        var reloaded = AuraSkinRuntime.Reload(currentConfig, AuraToolsIds.ModId);
        if (reloaded)
        {
            BroadcastLocalSelection();
        }

        return reloaded;
    }

    public static string[] StatusLines()
    {
        var remote = SkinRuntime.RemoteStatusLines();
        if (remote.Length == 0)
        {
            return new[] { lastInstallStatus, "No remote skin selections received." };
        }

        var lines = new string[remote.Length + 1];
        lines[0] = lastInstallStatus;
        Array.Copy(remote, 0, lines, 1, remote.Length);
        return lines;
    }

    public static void ReceiveRemoteSelection(SkinSelectionSnapshot snapshot)
    {
        if (!AuraToolsConfigService.Root.Skin.Enabled
            || !AuraToolsConfigService.Skin.Enabled
            || !AuraToolsConfigService.Skin.SyncRemote)
        {
            return;
        }

        var localPlayerId = PlayerManager.Instance?.PlayerId ?? "";
        if (!string.IsNullOrWhiteSpace(localPlayerId)
            && string.Equals(localPlayerId, snapshot.PlayerId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SkinRuntime.ApplyRemoteSelection(snapshot);
    }

    private static void RegisterHooks(ModConfig modConfig)
    {
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.UpdateLobby", _ => BroadcastLocalSelection(), "Skin");
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.ChangeRole", _ => BroadcastLocalSelection(), "Skin");
        AuraToolsHookRegistry.After(modConfig, "TopBarUI.ChangeCareer", _ => BroadcastLocalSelection(), "Skin");
        AuraToolsHookRegistry.After(modConfig, "TopBarUI.ChangeCareerAvator", _ => BroadcastLocalSelection(), "Skin");
    }

    private static void ConfigureFromSettings()
    {
        SkinRuntime.ConfigurePresentation(
            AuraToolsConfigService.Root.Skin.Enabled && AuraToolsConfigService.Skin.Enabled,
            AuraToolsConfigService.Skin.ShowEntrySkinButton);
    }

    private static void OnLocalSelectionChanged(SkinSelectionSnapshot snapshot)
    {
        BroadcastLocalSelection();
    }

    private static void BroadcastLocalSelection()
    {
        if (!AuraToolsConfigService.Root.Skin.Enabled
            || !AuraToolsConfigService.Skin.Enabled
            || !AuraToolsConfigService.Skin.SyncRemote)
        {
            return;
        }

        var manager = PlayerManager.Instance;
        if (manager == null)
        {
            return;
        }

        var career = RoleTable.Instance?.Career ?? GameEntryUI.career;
        var snapshot = SkinRuntime.CreateLocalSelectionSnapshot(
            career,
            manager.PlayerId,
            manager.playerInfo?.Name ?? "");
        if (string.IsNullOrWhiteSpace(snapshot.PlayerId) || string.IsNullOrWhiteSpace(snapshot.CareerId))
        {
            return;
        }

        try
        {
            var command = new AuraSkinSelectionCommand(snapshot);
            AuraToolsRpcTransport.Send(manager, command, "Skin.Selection", excludeOwner: true);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[Skin] remote sync failed: " + ex.Message);
        }
    }
}

[Serializable]
public sealed class AuraSkinSelectionCommand : RpcCommandBase
{
    public AuraSkinSelectionCommand()
    {
        Snapshot = new SkinSelectionSnapshot();
    }

    public AuraSkinSelectionCommand(SkinSelectionSnapshot snapshot)
    {
        Snapshot = snapshot ?? new SkinSelectionSnapshot();
    }

    public SkinSelectionSnapshot Snapshot { get; set; }

    public override void RpcExecute()
    {
        AuraToolsSkinRuntime.ReceiveRemoteSelection(Snapshot);
    }
}
