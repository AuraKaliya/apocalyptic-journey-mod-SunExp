using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using AuraMode.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckApplicationCoordinator
{
    private const string CardsAppliedKey = "AuraTools.StarterDeckApplied";
    private const string CardsAppliedRoleKey = CardsAppliedKey + ".Role";
    private const string RelicsAppliedKey = "AuraTools.CustomStart.RelicsApplied";
    private const string RelicsAppliedRoleKey = RelicsAppliedKey + ".Role";
    private const string RelicsAppliedIdsKey = RelicsAppliedKey + ".Ids";
    private const string RelicsAppliedModeKey = RelicsAppliedKey + ".Mode";
    private const string Owner = "AuraTools.StarterDeck";
    private static int lastForeignRoleTableSkipLogFrame = -100000;

    internal static void Apply(RoleTable? roleTable, ModHookContext context, string source)
    {
        if (roleTable == null)
        {
            return;
        }

        if (!IsLocalPlayerRoleTable(roleTable, source))
        {
            return;
        }

        if (!IsWorldSimulationRun())
        {
            ReconcileIneligibleLoadout(roleTable, source);
            AuraToolsLog.Info("[CustomStart] skipped: not a confirmed world-simulation run. source=" + source + ".");
            return;
        }

        if (!AuraToolsConfigService.MatchExperience.StarterDeck.Enabled)
        {
            return;
        }

        var roleId = RoleCatalog.NormalizeRoleId(ReadDataId(roleTable.Career));
        if (string.IsNullOrWhiteSpace(roleId))
        {
            AuraToolsLog.Warn("[CustomStart] skipped: local role table has no career. source=" + source + ".");
            return;
        }

        var loadout = StarterDeckProfileResolver.ResolveEffectiveLoadout(roleId);
        try
        {
            ApplyCards(roleTable, roleId, loadout.CardIds, source);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[CustomStart] card replacement failed", ex);
        }

        try
        {
            ApplyRelics(roleTable, roleId, loadout.RelicIds, source);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[CustomStart] relic replacement failed", ex);
        }
    }

    private static void ApplyCards(RoleTable roleTable, string roleId, IReadOnlyCollection<string> configured, string source)
    {
        if (IsAppliedForRole(roleTable, CardsAppliedKey, CardsAppliedRoleKey, roleId))
        {
            return;
        }

        if (ShouldSkipCardsForExternalOwner(roleTable))
        {
            return;
        }

        if (configured == null || configured.Count == 0)
        {
            AuraToolsLog.Info("[CustomStart] cards use the game default; role=" + roleId + ", source=" + source + ".");
            return;
        }

        var deck = StarterDeckDeckBuilder.Build(
            configured,
            StarterDeckSettings.MaximumCardCount,
            StarterDeckCardCatalog.IsValidCard,
            StarterDeckCardCatalog.IsStarterDeckExcludedCard,
            Array.Empty<string>(),
            cardId => StarterDeckCardCatalog.ResolveCardId(cardId));
        if (deck.Count == 0)
        {
            AuraToolsLog.Warn("[CustomStart] no configured cards are currently registered; keeping the game default. role=" + roleId + ".");
            return;
        }

        var originalDeckCount = roleTable.cardList.Count;
        var claim = new StarterDeckClaim
        {
            Owner = Owner,
            Scope = AuraModeRunIdentity.NativeWorldSimulationModeId,
            ModeId = AuraModeRunIdentity.NativeWorldSimulationModeId,
            Source = "local:custom-start",
            State = StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = CardsAppliedKey,
            AppliedModeKey = CardsAppliedKey + ".Mode",
            AppliedMode = AuraModeRunIdentity.NativeWorldSimulationModeId,
            LegacyMode = "",
            DeckSize = deck.Count,
            SourceName = "AuraTools.WitchWorldSimulation.CustomStart.Cards",
            MarkLegacyCardPackApplied = false
        };
        if (!StarterDeckArbiterRuntime.ApplyDeck(roleTable, deck, claim, sync: false))
        {
            return;
        }

        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[CardsAppliedRoleKey] = roleId;
        AuraToolsLog.Info("[CustomStart] applied cards; role=" + roleId
                          + ", original=" + originalDeckCount
                          + ", applied=" + deck.Count
                          + ", cards=" + string.Join("|", deck) + ".");
    }

    private static void ApplyRelics(RoleTable roleTable, string roleId, IReadOnlyCollection<string> configured, string source)
    {
        if (IsAppliedForRole(roleTable, RelicsAppliedKey, RelicsAppliedRoleKey, roleId))
        {
            return;
        }

        var resolvedIds = new List<string>();
        var instances = new List<DataConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declared in configured ?? Array.Empty<string>())
        {
            if (resolvedIds.Count >= StarterDeckSettings.MaximumRelicCount)
            {
                break;
            }

            var relicId = StarterRelicCatalog.ResolveRelicId(declared);
            if (string.IsNullOrWhiteSpace(relicId) || !seen.Add(relicId))
            {
                continue;
            }

            var materialized = AuraGameDataHostApi.Materialize(DataType.Relic, relicId);
            if (!materialized.Success || materialized.Instance is not DataConfig relic)
            {
                AuraToolsLog.Warn("[CustomStart] ignored unavailable relic: " + declared + ".");
                continue;
            }

            resolvedIds.Add(relicId);
            instances.Add(relic);
        }

        var original = roleTable.relicList.ToList();
        try
        {
            roleTable.relicList.Clear();
            foreach (var relic in instances)
            {
                roleTable.relicList.Add(relic);
            }
        }
        catch
        {
            roleTable.relicList.Clear();
            foreach (var relic in original)
            {
                roleTable.relicList.Add(relic);
            }

            throw;
        }

        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[RelicsAppliedKey] = "1";
        roleTable.SpecialVarMap[RelicsAppliedRoleKey] = roleId;
        roleTable.SpecialVarMap[RelicsAppliedIdsKey] = string.Join("|", resolvedIds);
        roleTable.SpecialVarMap[RelicsAppliedModeKey] = AuraModeRunIdentity.NativeWorldSimulationModeId;
        AuraToolsLog.Info("[CustomStart] replaced equipped starter relics; role=" + roleId
                          + ", original=" + original.Count
                          + ", applied=" + resolvedIds.Count
                          + ", source=" + source
                          + ", relics=" + string.Join("|", resolvedIds) + ".");
    }

    private static bool IsAppliedForRole(RoleTable roleTable, string appliedKey, string roleKey, string roleId)
    {
        if (roleTable.SpecialVarMap == null
            || !roleTable.SpecialVarMap.TryGetValue(appliedKey, out var applied)
            || applied != "1")
        {
            return false;
        }

        return !roleTable.SpecialVarMap.TryGetValue(roleKey, out var appliedRole)
               || string.Equals(RoleCatalog.NormalizeRoleId(appliedRole), roleId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipCardsForExternalOwner(RoleTable roleTable)
    {
        var activeMode = AuraModeRuntime.Current(AuraToolsIds.ModId);
        var decision = AuraModeRuntime.EvaluateStarterDeckMutation(activeMode, AuraToolsIds.ModId);
        if (!decision.Allowed)
        {
            AuraToolsLog.Info("[CustomStart] card replacement skipped by mode policy; mode=" + activeMode?.ModeId + ".");
            return true;
        }

        if (StarterDeckArbiterRuntime.IsOwnedByOther(roleTable, Owner, out var owner))
        {
            AuraToolsLog.Info("[CustomStart] card replacement skipped: starter deck owner=" + owner + ".");
            return true;
        }

        return false;
    }

    private static bool IsWorldSimulationRun()
    {
        try
        {
            var saveInfo = GameSaveManager.GetNowSave() ?? GameEntryUI.selectedSave;
            var runIdentity = "";
            if (saveInfo?.GameVars != null)
            {
                saveInfo.GameVars.TryGetValue(AuraModeRunIdentity.RunIdentityKey, out runIdentity);
            }

            return AuraModeRunIdentity.IsNativeWorldSimulation(
                saveInfo?.modeType ?? "",
                runIdentity,
                AuraModeRuntime.Current(AuraToolsIds.ModId, refresh: true),
                saveInfo?.Name ?? "");
        }
        catch
        {
            return false;
        }
    }

    private static void ReconcileIneligibleLoadout(RoleTable roleTable, string source)
    {
        var vars = roleTable.SpecialVarMap;
        if (vars == null)
        {
            return;
        }

        var removedRelics = 0;
        if (vars.TryGetValue(RelicsAppliedKey, out var relicsApplied)
            && relicsApplied == "1"
            && vars.TryGetValue(RelicsAppliedIdsKey, out var appliedRelicIds))
        {
            var ids = new HashSet<string>(
                (appliedRelicIds ?? "")
                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => StarterRelicCatalog.ResolveRelicId(id))
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            for (var i = roleTable.relicList.Count - 1; i >= 0; i--)
            {
                var id = StarterRelicCatalog.ResolveRelicId(ReadDataId(roleTable.relicList[i]));
                if (ids.Contains(id))
                {
                    roleTable.relicList.RemoveAt(i);
                    removedRelics++;
                }
            }
        }

        vars.Remove(RelicsAppliedKey);
        vars.Remove(RelicsAppliedRoleKey);
        vars.Remove(RelicsAppliedIdsKey);
        vars.Remove(RelicsAppliedModeKey);

        if (vars.TryGetValue(StarterDeckArbiterRuntime.OwnerKey, out var owner)
            && string.Equals(owner, Owner, StringComparison.OrdinalIgnoreCase))
        {
            vars.Remove(StarterDeckArbiterRuntime.OwnerKey);
            vars.Remove(StarterDeckArbiterRuntime.ScopeKey);
            vars.Remove(StarterDeckArbiterRuntime.StateKey);
            vars.Remove(StarterDeckArbiterRuntime.SourceKey);
            vars.Remove(StarterDeckArbiterRuntime.ModeKey);
            vars.Remove(StarterDeckArbiterRuntime.CardsKey);
            vars.Remove(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey);
            vars.Remove(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey + ".Mode");
        }

        vars.Remove(CardsAppliedKey);
        vars.Remove(CardsAppliedRoleKey);
        vars.Remove(CardsAppliedKey + ".Mode");
        if (removedRelics > 0)
        {
            AuraToolsLog.Info("[CustomStart] reconciled "
                              + removedRelics
                              + " tool-applied relic(s) from an ineligible mode; source="
                              + source
                              + ".");
        }
    }

    private static bool IsLocalPlayerRoleTable(RoleTable roleTable, string source)
    {
        try
        {
            var localPlayerId = (PlayerManager.Instance?.PlayerId ?? "").Trim();
            var roleTableId = (ReflectionUtil.ReadString(roleTable, "Id", "id") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(localPlayerId)
                || string.IsNullOrWhiteSpace(roleTableId)
                || string.Equals(localPlayerId, roleTableId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var frame = SafeFrameCount();
            if (frame - lastForeignRoleTableSkipLogFrame >= 300)
            {
                lastForeignRoleTableSkipLogFrame = frame;
                AuraToolsLog.Info("[CustomStart] skipped foreign role table; local=" + localPlayerId
                                  + ", roleTable=" + roleTableId + ", source=" + source + ".");
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static int SafeFrameCount()
    {
        try { return Time.frameCount; }
        catch { return int.MaxValue; }
    }

    private static string ReadDataId(IDataConfig? dataConfig)
    {
        try
        {
            return dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id)
                ? id ?? ""
                : dataConfig?.InstanceID ?? "";
        }
        catch
        {
            return "";
        }
    }
}
