using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryBossTransitionCoordinator
{
    private const string HookOwner = "SolarMemoryBossTransition";
    private static bool solarMemoryStorySettlementPending;
    private static bool solarMemorySaintWunaBossTransitioning;

    internal static bool IsSettlementPending => solarMemoryStorySettlementPending;

    public static void Initialize(ModConfig modConfig)
    {
        TerriasBattleLifecycleRouter.Register(HookOwner, new TerriasBattleLifecycleSubscription
        {
            OutcomeEnded = context =>
            {
                if (context.Outcome == AuraShared.Core.AuraBattleOutcome.Win)
                {
                    SettleSolarMemoryBossAfterWin(context.NativeContext);
                }
            }
        });
    }

    public static void CompleteSolarMemoryRunForSettlementFromDialogue(string source)
    {
        solarMemoryStorySettlementPending = false;
        SolarMemorySettlementCoordinator.CompleteSolarMemoryRunForSettlement(source);
    }

    public static void ContinueSaintWunaBossFromPreludeDialogue(string source)
    {
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemorySaintWunaBossPendingKey, true);
        TerriasLog.Info("[SolarMemoryStory] saint-wuna prelude complete; pending saint-wuna boss flow. source=" + source);
        if (!TryContinuePendingSaintWunaBoss(source))
        {
            TerriasLog.Warn("[SolarMemoryStory] saint-wuna boss flow pending after "
                + source
                + "; waiting for map runtime recovery.");
        }
    }

    internal static bool TryContinuePendingSaintWunaBoss(string source)
    {
        if (!SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemorySaintWunaBossPendingKey))
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
            TerriasLog.Debug("[SolarMemoryStory] saint-wuna boss flow pending on client-only player; host will advance it. source=" + source);
            return false;
        }

        var mapManager = MapManager.Instance;
        var manager = mapManager?.ModeMapManager as NormalMapManager;
        var tree = mapManager?.MapTree;
        if (mapManager == null || manager == null || tree == null || RoleTable.Instance == null)
        {
            TerriasLog.Warn("[SolarMemoryStory] saint-wuna boss flow cannot advance yet from "
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
            TerriasLog.Info("[SolarMemoryStory] saint-wuna boss transition requested from "
                + source
                + "; level="
                + manager.Level
                + "; nodeId="
                + TerriasIds.SolarBossSaintWunaLevelId
                + ".");
            mapManager.CmdNextMap();
            return true;
        }
        catch (Exception ex)
        {
            SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemorySaintWunaBossPendingKey, true);
            TerriasLog.Warn("[SolarMemoryStory] saint-wuna boss transition failed from "
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
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemorySaintWunaBossPendingKey, false);
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
            if (string.Equals(levelId, TerriasIds.SolarBossSecondSunLevelId, StringComparison.Ordinal))
            {
                if (RoleDeckHasCard(TerriasIds.BlazingCrownCollapseCardId))
                {
                    if (TryStartSolarMemoryBossDialogue(
                        SolarMemoryFlowApi.StartSaintWunaPreludeDialogue,
                        "Fight_Win.ResetStates:second_sun_with_key_card",
                        false))
                    {
                        return;
                    }

                    TerriasLog.Info("[SolarMemoryBoss] second sun defeated; blazing crown collapse found, continuing memory.");
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

            if (string.Equals(levelId, TerriasIds.SolarBossSaintWunaLevelId, StringComparison.Ordinal))
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
            TerriasLog.Error("Solar memory boss win settlement failed", ex);
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
            TerriasLog.Info("[SolarMemoryStory] deferred boss flow from " + source + " until dialogue completion.");
            return true;
        }

        if (settlementPending)
        {
            solarMemoryStorySettlementPending = false;
        }

        TerriasLog.Warn("[SolarMemoryStory] dialogue failed from " + source + "; falling back to immediate boss flow.");
        return false;
    }

    private static MapTree.Node CreateSaintWunaBossTransitionNode(MapTree tree, string source)
    {
        var node = SolarMemoryMapNodePoolFactory.CreateFixedBossNode(tree, TerriasIds.SolarBossSaintWunaMapId);
        node.data ??= new Dictionary<string, string>();
        node.data["Id"] = TerriasIds.SolarBossSaintWunaMapId;
        node.data["Type"] = "Fight";
        node.data["NodeId"] = TerriasIds.SolarBossSaintWunaLevelId;
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
        maps[SolarMemoryMapNodePoolFactory.EndingSlotIndex] = TerriasIds.SolarBossSaintWunaMapId;
        mapData[SolarMemoryMapNodePoolFactory.EndingSlotIndex] = TerriasIds.SolarBossSaintWunaLevelId;
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
            TerriasLog.Warn("[SolarMemoryStory] saint-wuna transition UI cleanup failed from "
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
        return TerriasContentIdCompatibility.LocalId(id);
    }
}
