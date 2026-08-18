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

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckApplicationCoordinator
{
    private const string CardsAppliedKey = "AuraTools.StarterDeckApplied";
    private const string CardsAppliedRoleKey = CardsAppliedKey + ".Role";
    private const string RelicsAppliedKey = "AuraTools.CustomStart.RelicsApplied";
    private const string RelicsAppliedRoleKey = RelicsAppliedKey + ".Role";
    private const string RelicsAppliedIdsKey = RelicsAppliedKey + ".Ids";
    private const string Owner = "AuraTools.StarterDeck";
    private const string Scope = "AuraTools.WorldSimulation";
    private const string Mode = "AuraTools.WorldSimulation";
    private const string LegacyMode = "aura-world-simulation";
    private static int lastForeignRoleTableSkipLogFrame = -100000;

    internal static void Apply(RoleTable? roleTable, ModHookContext context, string source)
    {
        if (!AuraToolsConfigService.MatchExperience.StarterDeck.Enabled || roleTable == null)
        {
            return;
        }

        if (!IsWorldSimulationRun())
        {
            AuraToolsLog.Info("[CustomStart] skipped: not a confirmed world-simulation run. source=" + source + ".");
            return;
        }

        if (!IsLocalPlayerRoleTable(roleTable, source))
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
            Scope = Scope,
            ModeId = Mode,
            Source = "local:custom-start",
            State = StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = CardsAppliedKey,
            AppliedModeKey = CardsAppliedKey + ".Mode",
            AppliedMode = LegacyMode,
            LegacyMode = LegacyMode,
            DeckSize = deck.Count,
            SourceName = "AuraTools.WorldSimulation.CustomStart.Cards"
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
        var modeType = ReadLobbyModeType();
        if (!string.IsNullOrWhiteSpace(modeType))
        {
            return string.Equals(modeType, "Normal", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return string.Equals(MapManager.Instance?.ModeMapManager?.GetType().Name, "NormalMapManager", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
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

    private static string ReadLobbyModeType()
    {
        try { return LobbyManager.Instance?.CurrentLobbyModeType ?? ""; }
        catch { return ""; }
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
