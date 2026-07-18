using System;
using System.Collections.Generic;
using System.Linq;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckApplicationCoordinator
{
    private const string AppliedKey = "AuraTools.StarterDeckApplied";
    private const string AppliedRoleKey = AppliedKey + ".Role";
    private const string AppliedProfileKey = AppliedKey + ".Profile";
    private const string AppliedRoleSourceKey = AppliedKey + ".RoleSource";
    private const string AppliedRoleTableRoleKey = AppliedKey + ".RoleTableRole";
    private const string AppliedSelectedRoleKey = AppliedKey + ".SelectedRole";
    private const string Owner = "AuraTools.StarterDeck";
    private const string Scope = "AuraTools.WorldSimulation";
    private const string Mode = "AuraTools.WorldSimulation";
    private const string LegacyMode = "aura-world-simulation";
    private static int lastForeignRoleTableSkipLogFrame = -100000;

    internal static void Apply(
        RoleTable? roleTable,
        ModHookContext context,
        string source)
    {
        if (!AuraToolsConfigService.Root.MatchExperience.Enabled
            || !AuraToolsConfigService.MatchExperience.StarterDeck.Enabled
            || roleTable == null)
        {
            return;
        }

        if (!IsWorldSimulationRun())
        {
            AuraToolsLog.Info("[StarterDeck] skipped: not a confirmed world-simulation run. source=" + source + ".");
            return;
        }

        if (!IsLocalPlayerRoleTable(roleTable, source))
        {
            return;
        }

        if (ShouldSkipForExternalOwner(roleTable))
        {
            return;
        }

        var role = ResolveRuntimeRole(roleTable);
        if (string.IsNullOrWhiteSpace(role.RoleId))
        {
            AuraToolsLog.Warn("[StarterDeck] skipped: local role table has no career. source="
                              + source
                              + ", roleTable="
                              + ReadRoleTableId(roleTable)
                              + ".");
            return;
        }

        if (IsApplied(roleTable, role))
        {
            return;
        }

        var selection = StarterDeckProfileResolver.ResolveEffectiveProfile(role.RoleId);
        if (selection == null)
        {
            AuraToolsLog.Warn("[StarterDeck] skipped: no complete profile for role=" + role.RoleId + ".");
            return;
        }

        var deck = StarterDeckProfileResolver.BuildDeckFromProfile(selection.Profile);
        if (deck.Count != selection.Profile.DeckSize)
        {
            AuraToolsLog.Warn("[StarterDeck] skipped: profile is incomplete. profile="
                              + selection.Profile.QualifiedProfileId
                              + ", role=" + role.RoleId
                              + ", deck=" + deck.Count + "/" + selection.Profile.DeckSize);
            return;
        }

        var originalDeckCount = roleTable.cardList.Count;
        if (!StarterDeckArbiterRuntime.ApplyDeck(roleTable, deck, CreateClaim(selection.Profile), sync: false))
        {
            return;
        }

        WriteAppliedRoleMetadata(roleTable, role, selection.Profile);

        AuraToolsLog.Info("[StarterDeck] applied local role-table profile; role="
                          + role.RoleId
                          + ", roleSource=" + role.Source
                          + ", roleTableRole=" + role.RoleTableRoleId
                          + ", selectedRole=" + role.SelectedRoleId
                          + ", profile=" + selection.Profile.QualifiedProfileId
                          + ", reason=" + selection.Reason
                          + ", originalDeck="
                          + originalDeckCount
                          + ", deck=" + roleTable.cardList.Count
                          + ", cards=" + string.Join("|", deck));
    }

    private static bool IsWorldSimulationRun()
    {
        var modeType = ReadLobbyModeType();
        if (!string.IsNullOrWhiteSpace(modeType))
        {
            return string.Equals(modeType, "Normal", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            if (IsNormalMapManager(MapManager.Instance?.ModeMapManager))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsLocalPlayerRoleTable(RoleTable roleTable, string source)
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            if (playerManager == null)
            {
                return true;
            }

            var localPlayerId = (playerManager.PlayerId ?? "").Trim();
            var roleTableId = ReadRoleTableId(roleTable);
            if (string.IsNullOrWhiteSpace(localPlayerId) || string.IsNullOrWhiteSpace(roleTableId))
            {
                return true;
            }

            if (string.Equals(localPlayerId, roleTableId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            LogForeignRoleTableSkipped(source, localPlayerId, roleTableId);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static void LogForeignRoleTableSkipped(string source, string localPlayerId, string roleTableId)
    {
        var frame = SafeFrameCount();
        if (frame - lastForeignRoleTableSkipLogFrame < 300)
        {
            return;
        }

        lastForeignRoleTableSkipLogFrame = frame;
        AuraToolsLog.Info("[StarterDeck] skipped: role table belongs to another player; local="
                          + localPlayerId
                          + ", roleTable="
                          + roleTableId
                          + ", source="
                          + source + ".");
    }

    private static int SafeFrameCount()
    {
        try
        {
            return Time.frameCount;
        }
        catch
        {
            return int.MaxValue;
        }
    }

    private static bool IsNormalMapManager(object? value)
    {
        return string.Equals(value?.GetType().Name, "NormalMapManager", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRoleTableId(RoleTable roleTable)
    {
        return (ReflectionUtil.ReadString(roleTable, "Id", "id") ?? "").Trim();
    }

    private static bool ShouldSkipForExternalOwner(RoleTable roleTable)
    {
        var activeMode = AuraModeRuntime.Current(AuraToolsIds.ModId);
        var policyDecision = AuraModeRuntime.EvaluateStarterDeckMutation(activeMode, AuraToolsIds.ModId);
        if (!policyDecision.Allowed)
        {
            AuraToolsLog.Info("[StarterDeck] skipped: active mode policy owns starter-deck mutation; mode="
                              + activeMode?.ModeId
                              + ", provider="
                              + policyDecision.AuthorityProviderId
                              + ", policy="
                              + policyDecision.PolicyId
                              + ".");
            return true;
        }

        if (roleTable.SpecialVarMap == null)
        {
            return false;
        }

        if (StarterDeckArbiterRuntime.IsOwnedByOther(roleTable, Owner, out var owner))
        {
            if (IsAuraToolsApplied(roleTable))
            {
                return false;
            }

            AuraToolsLog.Info("[StarterDeck] skipped: starter deck owner=" + owner + ".");
            return true;
        }

        return false;
    }

    private static StarterDeckRuntimeRole ResolveRuntimeRole(RoleTable roleTable)
    {
        var roleTableRole = RoleCatalog.NormalizeRoleId(ReadDataId(roleTable.Career));
        return new StarterDeckRuntimeRole(
            roleTableRole,
            roleTableRole,
            "",
            string.IsNullOrWhiteSpace(roleTableRole) ? "missing-role-table-career" : "RoleTable.Career");
    }

    private static string ReadLobbyModeType()
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

    private static string ReadDataId(IDataConfig? dataConfig)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id))
            {
                return id ?? "";
            }

            return dataConfig?.InstanceID ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsApplied(RoleTable roleTable, StarterDeckRuntimeRole role)
    {
        if (!IsAuraToolsApplied(roleTable))
        {
            return false;
        }

        var appliedRole = ReadSpecialVar(roleTable, AppliedRoleKey);
        if (string.IsNullOrWhiteSpace(appliedRole))
        {
            if (role.HasSelectedRoleConflict)
            {
                AuraToolsLog.Info("[StarterDeck] correcting legacy starter deck without role marker; roleTableRole="
                                  + role.RoleTableRoleId
                                  + ", selectedRole=" + role.SelectedRoleId
                                  + ".");
                return false;
            }

            return true;
        }

        if (string.Equals(RoleCatalog.NormalizeRoleId(appliedRole), role.RoleId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        AuraToolsLog.Info("[StarterDeck] correcting stale starter deck; appliedRole="
                          + appliedRole
                          + ", resolvedRole=" + role.RoleId
                          + ", roleSource=" + role.Source + ".");
        return false;
    }

    private static bool IsAuraToolsApplied(RoleTable roleTable)
    {
        if (StarterDeckArbiterRuntime.HasApplied(roleTable, AppliedKey, Owner))
        {
            return true;
        }

        return roleTable.SpecialVarMap != null
               && roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey, out var oldValue)
               && oldValue == "1"
               && roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey + ".Mode", out var legacyMode)
               && legacyMode.StartsWith("aura-", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSpecialVar(RoleTable roleTable, string key)
    {
        return roleTable.SpecialVarMap != null && roleTable.SpecialVarMap.TryGetValue(key, out var value)
            ? value ?? ""
            : "";
    }

    private static void WriteAppliedRoleMetadata(RoleTable roleTable, StarterDeckRuntimeRole role, StarterDeckProfile profile)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[AppliedRoleKey] = role.RoleId;
        roleTable.SpecialVarMap[AppliedProfileKey] = profile.QualifiedProfileId;
        roleTable.SpecialVarMap[AppliedRoleSourceKey] = role.Source;
        roleTable.SpecialVarMap[AppliedRoleTableRoleKey] = role.RoleTableRoleId;
        roleTable.SpecialVarMap[AppliedSelectedRoleKey] = role.SelectedRoleId;
    }

    private static StarterDeckClaim CreateClaim(StarterDeckProfile profile)
    {
        var registered = profile.SourceKind == StarterDeckProfileSourceKind.Registered;
        return new StarterDeckClaim
        {
            // The profile remains owned by its registering content mod.  This
            // claim records the AuraTools effective overlay that applies it.
            Owner = Owner,
            Scope = Scope,
            ModeId = Mode,
            Source = (registered ? "registered:" : "local:") + profile.QualifiedProfileId,
            State = StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = AppliedKey,
            AppliedModeKey = AppliedKey + ".Mode",
            AppliedMode = LegacyMode,
            LegacyMode = LegacyMode,
            DeckSize = profile.DeckSize,
            SourceName = "AuraTools.WorldSimulation.StarterDeck"
        };
    }

}
