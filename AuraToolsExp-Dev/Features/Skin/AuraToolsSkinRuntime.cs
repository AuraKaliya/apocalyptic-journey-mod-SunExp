using System;
using System.Collections.Generic;
using System.Linq;
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
    private static bool migratingSelection;
    private static string lastInstallStatus = "Skin package not installed yet.";

    public static void Initialize(ModConfig modConfig)
    {
        currentConfig = modConfig;
        AuraSkinRuntime.Initialize(modConfig, AuraToolsIds.ModId);
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

        ConfigureFromSettings();

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
            ConfigureFromSettings();
            BroadcastLocalSelection();
        }

        return reloaded;
    }

    public static string[] StatusLines()
    {
        var candidates = SkinRuntime.GetAllSkinCandidates();
        var semanticGroups = candidates
            .GroupBy(candidate => candidate.SemanticKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var summary = "共享皮肤候选：" + candidates.Count
                      + "，语义分组：" + semanticGroups.Length
                      + "，多提供者分组：" + semanticGroups.Count(group => group.Count() > 1);
        var remote = SkinRuntime.RemoteStatusLines();
        if (remote.Length == 0)
        {
            return new[] { lastInstallStatus, summary, "No remote skin selections received." };
        }

        var lines = new string[remote.Length + 2];
        lines[0] = lastInstallStatus;
        lines[1] = summary;
        Array.Copy(remote, 0, lines, 2, remote.Length);
        return lines;
    }

    public static IReadOnlyList<SkinDefinition> CandidateDefinitions()
    {
        return SkinRuntime.GetAllSkinCandidates();
    }

    public static IReadOnlyList<SkinDefinition> CandidateDefinitions(string careerId)
    {
        return SkinRuntime.GetAllSkins(careerId);
    }

    public static IReadOnlyList<string> CandidateCareerIds()
    {
        return SkinRuntime.GetAllSkinCandidates()
            .Select(candidate => candidate.TargetCareerId)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string SelectedQualifiedSkinId(string careerId)
    {
        return SkinRuntime.GetSelectedQualifiedSkinId(careerId);
    }

    public static void SetCandidateEnabled(string qualifiedSkinId, bool enabled)
    {
        var candidates = SkinRuntime.GetAllSkinCandidates();
        var candidate = candidates.FirstOrDefault(value => string.Equals(
            value.QualifiedSkinId,
            qualifiedSkinId,
            StringComparison.OrdinalIgnoreCase));
        if (candidate == null)
        {
            return;
        }

        var candidateIds = candidates
            .Select(candidate => candidate.QualifiedSkinId)
            .ToArray();
        AuraToolsConfigService.Skin.SetCandidateEnabled(qualifiedSkinId, enabled, candidateIds);
        AuraToolsConfigService.SaveSkin();
        SaveCareerOverrides(candidate.TargetCareerId);
        ConfigureFromSettings();
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
        var candidates = SkinRuntime.GetAllSkinCandidates();
        if (!migratingSelection
            && AuraToolsConfigService.Skin.MigrateLegacyCandidateSelection(
                candidates.Select(candidate => candidate.QualifiedSkinId)))
        {
            migratingSelection = true;
            try
            {
                AuraToolsConfigService.SaveSkin();
                foreach (var careerId in candidates
                             .Select(candidate => candidate.TargetCareerId)
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    SaveCareerOverrides(careerId);
                }
            }
            finally
            {
                migratingSelection = false;
            }
        }

        var effectiveOverrides = new Dictionary<string, bool>(
            AuraToolsConfigService.Skin.ResourceOverrides,
            StringComparer.OrdinalIgnoreCase);
        foreach (var careerId in candidates
                     .Select(candidate => candidate.TargetCareerId)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var pair in ReadCareerOverrides(careerId))
            {
                effectiveOverrides[pair.Key] = pair.Value;
            }
        }

        SkinRuntime.ConfigurePresentation(
            AuraToolsConfigService.Root.Skin.Enabled && AuraToolsConfigService.Skin.Enabled,
            AuraToolsConfigService.Skin.ShowEntrySkinButton);
        SkinRuntime.ConfigureCandidateOverrides(effectiveOverrides);
    }

    private static AuraSharedScopeKey SkinScope(string careerId)
    {
        return new AuraSharedScopeKey
        {
            ModuleId = AuraSharedSystems.Skin,
            FeatureId = "Skin",
            ScopeType = "Role",
            ScopeId = (careerId ?? "").Trim()
        };
    }

    private static Dictionary<string, bool> ReadCareerOverrides(string careerId)
    {
        var document = AuraSharedResourceProtocol.ReadUserOverride(AuraToolsIds.ModId, SkinScope(careerId));
        if (document.Revision <= 0
            || document.Override?.Values == null
            || !document.Override.Values.TryGetValue("resourceOverrides", out var json))
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        var parsed = AuraSharedJson.Deserialize<Dictionary<string, bool>>(json);
        return parsed == null
            ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, bool>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveCareerOverrides(string careerId)
    {
        var normalizedCareerId = (careerId ?? "").Trim();
        if (normalizedCareerId.Length == 0)
        {
            return;
        }

        var candidateIds = SkinRuntime.GetAllSkins(normalizedCareerId)
            .Select(candidate => candidate.QualifiedSkinId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopedOverrides = ReadCareerOverrides(normalizedCareerId);
        foreach (var pair in AuraToolsConfigService.Skin.ResourceOverrides
                     .Where(pair => candidateIds.Contains(pair.Key)))
        {
            scopedOverrides[pair.Key] = pair.Value;
        }
        var scope = SkinScope(normalizedCareerId);
        var current = AuraSharedResourceProtocol.ReadUserOverride(AuraToolsIds.ModId, scope);
        var values = current.Override?.Values == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(current.Override.Values, StringComparer.OrdinalIgnoreCase);
        values["selectionMode"] = "ManualSelection";
        values["resourceOverrides"] = AuraSharedJson.Serialize(scopedOverrides);
        var localOverride = new AuraSharedLocalOverrideV4
        {
            Enabled = current.Override?.Enabled,
            SelectionMode = AuraSharedSelectionModes.Priority,
            ResourceOverrides = scopedOverrides,
            Values = values
        };
        var written = AuraSharedResourceProtocol.WriteUserOverride(
            AuraToolsIds.ModId,
            scope,
            localOverride,
            current.Revision);
        if (written.Conflict)
        {
            current = AuraSharedResourceProtocol.ReadUserOverride(AuraToolsIds.ModId, scope);
            written = AuraSharedResourceProtocol.WriteUserOverride(
                AuraToolsIds.ModId,
                scope,
                localOverride,
                current.Revision);
        }
        if (!written.Success)
        {
            AuraToolsLog.Warn("[Skin] failed to persist role resource overrides for "
                              + normalizedCareerId + ": " + written.Message);
        }
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
