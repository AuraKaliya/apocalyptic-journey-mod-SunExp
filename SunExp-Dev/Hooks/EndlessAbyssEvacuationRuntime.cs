using System;
using AuraMode.Shared;
using Data.Save;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Network;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;
using GameUIManager = Witch.UI.UIManager;

namespace SunExp.Dll.Hooks;

public static class EndlessAbyssEvacuationRuntime
{
    private static string lastPresentedToken = "";
    private static bool finalizationArmed;

    public static void Initialize(ModConfig modConfig)
    {
        EndlessAbyssEvacuationButtonRuntime.Initialize(modConfig);
        RegisterAfter(modConfig, "MapSelectUI.Start", ResumePendingSettlement);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ResumePendingSettlement);
        RegisterBefore(modConfig, "GameExitUI.ReturnAsync", ArmFinalization);
        RegisterBefore(modConfig, "GameApp.ReturnToMenu", FinalizeBeforeMenuReturn);
    }

    public static void RequestFromToolbar()
    {
        try
        {
            var canLocalInitiate = CanLocalInitiate();
            var phase = EndlessSeaRunStateStore.CurrentPhase();
            var floor = EndlessSeaModeRuntime.CurrentFloor();
            var level = Math.Max(0, MapManager.Instance?.Level ?? 0);
            SunExpLog.Info("[EndlessAbyssEvacuation] request entered: canLocalInitiate="
                           + canLocalInitiate
                           + ", phase="
                           + phase
                           + ", floor="
                           + floor
                           + ", level="
                           + level
                           + ".");
            if (!canLocalInitiate)
            {
                GameUIManager.Instance?.ShowTip("\u4ec5\u623f\u4e3b\u53ef\u4ee5\u53d1\u8d77\u64a4\u79bb");
                SunExpLog.Warn("[EndlessAbyssEvacuation] request blocked: local peer is not authoritative.");
                return;
            }

            var blockReason = GetBlockReason();
            if (blockReason != "ok")
            {
                GameUIManager.Instance?.ShowTip("\u5f53\u524d\u72b6\u6001\u4e0d\u80fd\u4e3b\u52a8\u64a4\u79bb");
                SunExpLog.Warn("[EndlessAbyssEvacuation] request blocked: " + blockReason);
                return;
            }

            var depth = EndlessAbyssEvacuationService.CalculateSettlementDepth(
                floor,
                level);
            var message = "\u64a4\u79bb\u540e\uff0c\u672c\u6b21\u6311\u6218\u5c06\u6309\u7b2c "
                          + floor
                          + " \u5c42\u3001\u5df2\u63a8\u8fdb "
                          + depth
                          + " \u4e2a\u8282\u70b9\u7ed3\u7b97\uff0c\u5e76\u8bb0\u5f55\u4e3a\u6a21\u5f0f\u901a\u5173\u3002\n\u7ed3\u7b97\u540e\u65e0\u6cd5\u7ee7\u7eed\u672c\u6b21\u6311\u6218\u3002";
            SunExpLog.Info("[EndlessAbyssEvacuation] confirmation presenting: floor="
                           + floor
                           + ", depth="
                           + depth
                           + ".");
            GameUIManager.Instance?.ShowModalWindow(
                "\u4e3b\u52a8\u64a4\u79bb",
                message,
                ConfirmFromModal,
                0f,
                null,
                true,
                true,
                "\u64a4\u79bb",
                "\u53d6\u6d88",
                true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Abyss evacuation request failed", ex);
        }
    }

    public static void ReceiveAuthoritative(EndlessAbyssEvacuationResolution? resolution, string source)
    {
        if (resolution?.IsValid != true || !EndlessAbyssEvacuationService.MatchesCurrentRun(resolution))
        {
            SunExpLog.Warn("[EndlessAbyssEvacuation] authoritative result ignored: source="
                           + source
                           + ", valid="
                           + (resolution?.IsValid == true)
                           + ", runMatches="
                           + (resolution != null && EndlessAbyssEvacuationService.MatchesCurrentRun(resolution))
                           + ".");
            return;
        }

        if (string.Equals(lastPresentedToken, resolution.Token, StringComparison.Ordinal)
            && IsActiveUi<GameExitUI>("GameExitUI"))
        {
            return;
        }

        lastPresentedToken = resolution.Token;
        finalizationArmed = false;
        SunExpLog.Info("[EndlessAbyssEvacuation] authoritative result accepted: source="
                       + source
                       + ", runId="
                       + resolution.RunId
                       + ", token="
                       + resolution.Token
                       + ".");
        SunExpFrameDispatcher.RunOnceNextFrame(
            "EndlessAbyssEvacuation.Settlement." + resolution.Token,
            () => ShowSettlement(resolution, source + ":next-frame"));
    }

    private static void ConfirmFromModal()
    {
        SunExpLog.Info("[EndlessAbyssEvacuation] confirmation accepted; scheduling authoritative commit.");
        SunExpFrameDispatcher.RunOnceNextFrame(
            "EndlessAbyssEvacuation.Confirm",
            () => TryBegin("EndlessAbyssEvacuation.Confirm"));
    }

    private static void TryBegin(string source)
    {
        var blockReason = GetBlockReason(allowConfirmationWindow: true);
        var canLocalInitiate = CanLocalInitiate();
        SunExpLog.Info("[EndlessAbyssEvacuation] commit entered: source="
                       + source
                       + ", canLocalInitiate="
                       + canLocalInitiate
                       + ", blockReason="
                       + blockReason
                       + ".");
        if (!canLocalInitiate || blockReason != "ok")
        {
            GameUIManager.Instance?.ShowTip("\u5f53\u524d\u72b6\u6001\u4e0d\u80fd\u4e3b\u52a8\u64a4\u79bb");
            SunExpLog.Warn("[EndlessAbyssEvacuation] confirmation blocked: " + blockReason);
            return;
        }

        if (!EndlessAbyssEvacuationService.TryBegin(source, out var resolution, out var rejection))
        {
            GameUIManager.Instance?.ShowTip("\u64a4\u79bb\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5");
            SunExpLog.Warn("[EndlessAbyssEvacuation] begin rejected: " + rejection);
            return;
        }

        EndlessAbyssEvacuationNetworkSync.Broadcast(resolution, source);
        SunExpLog.Info("[EndlessAbyssEvacuation] authoritative commit stored: runId="
                       + resolution.RunId
                       + ", floor="
                       + resolution.Floor
                       + ", depth="
                       + resolution.SettlementDepth
                       + ", token="
                       + resolution.Token
                       + ".");
        ReceiveAuthoritative(resolution, source + ":local");
    }

    private static string GetBlockReason(bool allowConfirmationWindow = false)
    {
        if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
        {
            return "mode-inactive";
        }

        if (!string.Equals(EndlessSeaRunStateStore.CurrentPhase(), EndlessSeaRunPhase.MapPlanning, StringComparison.Ordinal))
        {
            return "invalid-phase";
        }

        if (GameSaveManager.GetValue<string>(SunExpIds.EndlessSeaStarterDeckAppliedKey) != "1")
        {
            return "intro-incomplete";
        }

        if (MapManager.Instance?.ModeMapManager == null || RoleTable.Instance == null)
        {
            return "adventure-state-missing";
        }

        if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None)
        {
            return "fight-active";
        }

        if (!IsActiveUi<MapSelectUI>("MapSelectUI")
            || IsActiveUi<FightUI>("FightUI")
            || IsActiveUi<EventUI>("EventUI")
            || IsActiveUi<DialogueUI>("DialogueUI")
            || IsActiveUi<BattleRewardsUI>("BattleRewardsUI")
            || EndlessAbyssShockPanel.IsOpen
            || EndlessAbyssMilestoneRewardPanel.IsOpen)
        {
            return "blocking-ui-active";
        }

        if (EndlessAbyssShockService.PendingRequest() != null)
        {
            return "shock-pending";
        }

        if (EndlessAbyssMilestoneRewardService.CanClaimCurrentFloor())
        {
            return "milestone-pending";
        }

        if ((!allowConfirmationWindow && GameUIManager.Instance?.WindowObj != null)
            || GameUIManager.Instance?.InputObj != null)
        {
            return "modal-active";
        }

        return "ok";
    }

    private static bool CanLocalInitiate()
    {
        return !SunExpNetworkRuntime.IsMultiplayerSession() || !SunExpNetworkRuntime.IsClientOnly();
    }

    private static void ShowSettlement(EndlessAbyssEvacuationResolution resolution, string source)
    {
        try
        {
            SunExpLog.Info("[EndlessAbyssEvacuation] settlement presentation entered: source="
                           + source
                           + ", runId="
                           + resolution.RunId
                           + ", token="
                           + resolution.Token
                           + ".");
            if (!EndlessAbyssEvacuationService.MatchesCurrentRun(resolution)
                || !EndlessSeaRunStateStore.IsEvacuating())
            {
                SunExpLog.Warn("[EndlessAbyssEvacuation] settlement presentation skipped: runMatches="
                               + EndlessAbyssEvacuationService.MatchesCurrentRun(resolution)
                               + ", isEvacuating="
                               + EndlessSeaRunStateStore.IsEvacuating()
                               + ".");
                return;
            }

            MapManager.Instance?.SetLevel(Math.Max(0, resolution.SettlementDepth));
            GameExitUI.loss = false;
            var outcomePublished = AuraModeOutcomeRuntime.Publish(new AuraModeOutcomeSnapshot
            {
                OwnerModId = SunExpIds.ModId,
                ModeId = SunExpIds.EndlessAbyssSemanticModeId,
                RunId = resolution.RunId,
                OutcomeId = resolution.Token,
                Status = AuraModeOutcomeStates.Completed,
                Source = source
            });
            if (!outcomePublished)
            {
                SunExpLog.Warn("[EndlessAbyssEvacuation] shared completed-outcome publish failed: runId="
                               + resolution.RunId
                               + ", token="
                               + resolution.Token
                               + ".");
            }
            SunExpTransientUiRegistry.CloseAll("EndlessAbyssEvacuation.ShowSettlement");
            GameUIManager.Instance?.CloseUI("MapSelectUI");
            GameUIManager.Instance?.CloseUI("EventUI");
            GameUIManager.Instance?.CloseUI("DialogueUI");
            EndlessAbyssEvacuationButtonRuntime.Refresh();
            if (!IsActiveUi<GameExitUI>("GameExitUI"))
            {
                GameUIManager.Instance?.ShowUI<GameExitUI>("GameExitUI", true);
            }

            SunExpLog.Info("[EndlessAbyssEvacuation] settlement shown from "
                + source
                + "; floor="
                + resolution.Floor
                + "; depth="
                + resolution.SettlementDepth
                + "; token="
                + resolution.Token
                + "; sharedOutcome="
                + outcomePublished
                + ".");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Abyss evacuation settlement failed", ex);
        }
    }

    private static void ResumePendingSettlement(ModHookContext context)
    {
        if (!EndlessSeaRunStateStore.IsEvacuating())
        {
            return;
        }

        var resolution = EndlessAbyssEvacuationService.CaptureStored();
        ReceiveAuthoritative(resolution, "EndlessAbyssEvacuation.Resume");
    }

    private static void ArmFinalization(ModHookContext context)
    {
        if (EndlessSeaRunStateStore.IsEvacuating()
            && IsActiveUi<GameExitUI>("GameExitUI"))
        {
            finalizationArmed = true;
        }
    }

    private static void FinalizeBeforeMenuReturn(ModHookContext context)
    {
        if (!finalizationArmed || !EndlessSeaRunStateStore.IsEvacuating())
        {
            return;
        }

        finalizationArmed = false;
        var resolution = EndlessAbyssEvacuationService.CaptureStored();
        EndlessSeaRunStateStore.MarkEnded("EndlessAbyssEvacuation.GameApp.ReturnToMenu");
        EndlessAbyssEvacuationService.PersistCurrentSave("EndlessAbyssEvacuation.GameApp.ReturnToMenu");
        EndlessSeaNetworkSync.BroadcastSnapshot("EndlessAbyssEvacuation.GameApp.ReturnToMenu");
        AuraModeOutcomeRuntime.Clear(
            SunExpIds.ModId,
            SunExpIds.EndlessAbyssSemanticModeId,
            resolution.RunId);
        SunExpLog.Info("[EndlessAbyssEvacuation] finalized before menu return: runId="
                       + resolution.RunId
                       + ", token="
                       + resolution.Token
                       + ".");
        lastPresentedToken = "";
    }

    private static bool IsActiveUi<T>(string name) where T : UIBase
    {
        try
        {
            var ui = GameUIManager.Instance?.GetUI<T>(name);
            return ui != null && ui.gameObject.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "EndlessAbyssEvacuation");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "EndlessAbyssEvacuation");
    }
}
