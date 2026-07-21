using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryBossTransitionCoordinator
{
    private const string HookOwner = "SolarMemoryBossTransition";
    private static bool solarMemoryStorySettlementPending;
    private static bool solarMemorySaintWunaBossTransitioning;

    internal static bool IsSettlementPending => solarMemoryStorySettlementPending;

    public static void Initialize(ModConfig modConfig)
    {
        SunExpHookRegistry.After(
            modConfig,
            SunExpHookTargets.FightWinResetStates,
            SettleSolarMemoryBossAfterWin,
            HookOwner);
    }

    public static void CompleteSolarMemoryRunForSettlementFromDialogue(string source)
    {
        solarMemoryStorySettlementPending = false;
        SolarMemorySettlementCoordinator.CompleteSolarMemoryRunForSettlement(source);
    }

    public static void ContinueSaintWunaBossFromPreludeDialogue(string source)
    {
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemorySaintWunaBossPendingKey, true);
        SunExpLog.Info("[SolarMemoryStory] saint-wuna prelude complete; pending saint-wuna boss flow. source=" + source);
        if (!TryContinuePendingSaintWunaBoss(source))
        {
            SunExpLog.Warn("[SolarMemoryStory] saint-wuna boss flow pending after "
                + source
                + "; waiting for map runtime recovery.");
        }
    }

    internal static bool TryContinuePendingSaintWunaBoss(string source)
    {
        if (!SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemorySaintWunaBossPendingKey))
        {
            return false;
        }

        if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
        {
            ClearPendingSaintWunaBossFlow();
            return false;
        }

        if (solarMemorySaintWunaBossTransitioning)
        {
            return true;
        }

        if (SolarMemoryMapLifecycleCoordinator.IsClientOnlyPlayer())
        {
            SunExpLog.Debug("[SolarMemoryStory] saint-wuna boss flow pending on client-only player; host will advance it. source=" + source);
            return false;
        }

        var mapManager = MapManager.Instance;
        var manager = mapManager?.ModeMapManager as NormalMapManager;
        var tree = mapManager?.MapTree;
        if (mapManager == null || manager == null || tree == null || RoleTable.Instance == null)
        {
            SunExpLog.Warn("[SolarMemoryStory] saint-wuna boss flow cannot advance yet from "
                + source
                + "; map runtime is unavailable.");
            return false;
        }

        solarMemorySaintWunaBossTransitioning = true;
        try
        {
            var bossNode = CreateSaintWunaBossTransitionNode(tree, source);
            tree.currentNode = bossNode;
            GameSaveManager.UpdateNode(bossNode);
            RepairSaintWunaBossSyncArrays(mapManager);
            CloseSolarMemoryBossTransitionUi(source);
            ClearPendingSaintWunaBossFlow();
            SunExpLog.Info("[SolarMemoryStory] saint-wuna boss transition requested from "
                + source
                + "; level="
                + manager.Level
                + "; nodeId="
                + SunExpIds.SolarBossSaintWunaLevelId
                + ".");
            mapManager.CmdNextMap();
            return true;
        }
        catch (Exception ex)
        {
            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemorySaintWunaBossPendingKey, true);
            SunExpLog.Warn("[SolarMemoryStory] saint-wuna boss transition failed from "
                + source
                + ": "
                + ex.Message);
            return false;
        }
        finally
        {
            solarMemorySaintWunaBossTransitioning = false;
        }
    }

    internal static void ClearPendingSaintWunaBossFlow()
    {
        solarMemorySaintWunaBossTransitioning = false;
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemorySaintWunaBossPendingKey, false);
    }

    private static void SettleSolarMemoryBossAfterWin(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            var levelId = FightManager.Instance?.level ?? "";
            if (string.Equals(levelId, SunExpIds.SolarBossSecondSunLevelId, StringComparison.Ordinal))
            {
                if (RoleDeckHasCard(SunExpIds.BlazingCrownCollapseCardId))
                {
                    if (TryStartSolarMemoryBossDialogue(
                        SolarMemoryFlowApi.StartSaintWunaPreludeDialogue,
                        "Fight_Win.ResetStates:second_sun_with_key_card",
                        false))
                    {
                        return;
                    }

                    SunExpLog.Info("[SolarMemoryBoss] second sun defeated; blazing crown collapse found, continuing memory.");
                    ContinueSaintWunaBossFromPreludeDialogue("Fight_Win.ResetStates:second_sun_with_key_card:fallback");
                    return;
                }

                if (TryStartSolarMemoryBossDialogue(
                    SolarMemoryFlowApi.StartSecondSunEndingDialogue,
                    "Fight_Win.ResetStates:second_sun_without_key_card",
                    true))
                {
                    return;
                }

                SolarMemorySettlementCoordinator.CompleteSolarMemoryRunForSettlement("Fight_Win.ResetStates:second_sun_without_key_card");
                return;
            }

            if (string.Equals(levelId, SunExpIds.SolarBossSaintWunaLevelId, StringComparison.Ordinal))
            {
                if (TryStartSolarMemoryBossDialogue(
                    SolarMemoryFlowApi.StartSaintWunaEndingDialogue,
                    "Fight_Win.ResetStates:saint_wuna",
                    true))
                {
                    return;
                }

                SolarMemorySettlementCoordinator.CompleteSolarMemoryRunForSettlement("Fight_Win.ResetStates:saint_wuna");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory boss win settlement failed", ex);
        }
    }

    private static bool TryStartSolarMemoryBossDialogue(Func<bool> startDialogue, string source, bool settlementPending)
    {
        if (settlementPending)
        {
            solarMemoryStorySettlementPending = true;
        }

        UIManager.Instance?.CloseUI("FightUI");
        if (startDialogue())
        {
            SunExpLog.Info("[SolarMemoryStory] deferred boss flow from " + source + " until dialogue completion.");
            return true;
        }

        if (settlementPending)
        {
            solarMemoryStorySettlementPending = false;
        }

        SunExpLog.Warn("[SolarMemoryStory] dialogue failed from " + source + "; falling back to immediate boss flow.");
        return false;
    }

    private static MapTree.Node CreateSaintWunaBossTransitionNode(MapTree tree, string source)
    {
        var node = SolarMemoryMapNodePoolFactory.CreateFixedBossNode(tree, SunExpIds.SolarBossSaintWunaMapId);
        node.data ??= new Dictionary<string, string>();
        node.data["Id"] = SunExpIds.SolarBossSaintWunaMapId;
        node.data["Type"] = "Fight";
        node.data["NodeId"] = SunExpIds.SolarBossSaintWunaLevelId;
        node.data["Level"] = "-1";
        MapNodeSafetyService.EnsureNodeDice(tree, node, source);
        node.SetChild(0, CreateSolarMemoryTerminalNode(tree, source));
        return node;
    }

    private static MapTree.Node CreateSolarMemoryTerminalNode(MapTree tree, string source)
    {
        var node = new MapTree.Node("null")
        {
            NodeDice = tree.treedice ?? Dice.Default
        };
        MapNodeSafetyService.EnsureNodeDice(tree, node, source);
        return node;
    }

    private static void RepairSaintWunaBossSyncArrays(MapManager mapManager)
    {
        var maps = mapManager.mapList;
        var mapData = mapManager.mapData;
        if (maps == null
            || mapData == null
            || maps.Length <= SolarMemoryMapNodePoolFactory.EndingSlotIndex
            || mapData.Length <= SolarMemoryMapNodePoolFactory.EndingSlotIndex)
        {
            return;
        }

        SolarMemoryMapLifecycleCoordinator.RepairSolarMemoryMapArrays(maps, mapData);
        maps[SolarMemoryMapNodePoolFactory.EndingSlotIndex] = SunExpIds.SolarBossSaintWunaMapId;
        mapData[SolarMemoryMapNodePoolFactory.EndingSlotIndex] = SunExpIds.SolarBossSaintWunaLevelId;
    }

    private static void CloseSolarMemoryBossTransitionUi(string source)
    {
        try
        {
            SolarMemoryBattleExitCoordinator.CloseTransientUi(source);
            UIManager.Instance?.CloseUI("DialogueUI");
            UIManager.Instance?.CloseUI("FightUI");
            UIManager.Instance?.CloseUI("BattleRewardsUI");
            UIManager.Instance?.CloseUI("MapSelectUI");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryStory] saint-wuna transition UI cleanup failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    private static bool RoleDeckHasCard(string cardId)
    {
        var role = RoleTable.Instance;
        if (role == null || string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        return role.cardList.Any(card => IsCardId(card, cardId));
    }

    private static bool IsCardId(DataConfig? card, string expectedFullId)
    {
        var id = CardId(card);
        return string.Equals(id, expectedFullId, StringComparison.Ordinal)
            || string.Equals(id, ShortModId(expectedFullId), StringComparison.Ordinal);
    }

    private static string CardId(DataConfig? card)
    {
        return card?.data != null && card.data.TryGetValue("Id", out var id) ? id ?? "" : "";
    }

    private static string ShortModId(string id)
    {
        const string prefix = "SunExp_sunexp_";
        return id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : id;
    }
}
