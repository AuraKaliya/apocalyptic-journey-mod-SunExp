using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Owns the hard UI boundary around replay. A replay never carries a SettingUI
/// instance or tool overlay into the native presentation view, and never hands one back
/// to the menu after teardown.
/// </summary>
internal static class MatchReplayUiLifecycle
{
    private static readonly MatchReplayManagedUiOwnership ManagedUiOwnership = new();

    internal static void PrepareForReplayView()
    {
        // This only removes leftovers from an already completed replay. Native UI
        // created for the current replay is captured below and closed with the
        // replay-owned native view.
        CloseReplayOwnedPresentationUis("Match replay host prepare");

        var manager = WitchUiManager.Instance
                      ?? throw new InvalidOperationException("UIManager is unavailable at replay host preparation.");
        var baselineIds = manager.GetAllUI()
            .Where(ui => ui != null && ui.gameObject != null)
            .Select(ui => ui.gameObject.GetInstanceID())
            .ToArray();
        ManagedUiOwnership.Capture(baselineIds);
        AuraToolsLog.Debug("[MatchRecords] replay managed UI baseline captured: count="
                           + ManagedUiOwnership.BaselineCount + ".");
    }

    internal static int ReplayOwnedPresentationUiCount => FindReplayOwnedPresentationUis().Count;

    internal static int SettingUiCount => NativeSettingUiCacheApi.FindInstances().Count;

    internal static void CloseReplayOwnedPresentationUis(string source)
    {
        var replayOwned = FindReplayOwnedPresentationUis();
        foreach (var ui in replayOwned)
        {
            ForceDestroyNativeUi(ui, source);
        }

        AuraToolsLog.Debug("[MatchRecords] replay-owned presentation UI closed: source=" + source
                           + ", count=" + replayOwned.Count
                           + ", names=" + (replayOwned.Count == 0
                               ? "none"
                               : string.Join("|", replayOwned.Select(ui => ui.gameObject.name)))
                           + ".");
    }

    internal static void ReleaseReplayOwnership()
    {
        ManagedUiOwnership.Reset();
    }

    internal static void CloseOriginUi(string source)
    {
        AuraToolsSettingsRuntime.ReleaseForReplayTransition();
        AuraToolsUi.CloseOwnedOverlays(source);
        var settings = FindSettings();
        foreach (var setting in settings)
        {
            ForceDestroyNativeUi(setting, source);
        }

        AuraToolsLog.Debug("[MatchRecords] replay origin UI closed: source=" + source
                           + ", settings=" + settings.Count + ".");
    }

    internal static void ForceDestroyRoot(GameObject root, string source)
    {
        if (root == null)
        {
            return;
        }

        ClearSelectionWithin(root);
        var ui = root.GetComponent<UIBase>();
        if (ui != null)
        {
            ForceDestroyNativeUi(ui, source);
            return;
        }

        var killed = MatchReplayTweenCleanup.KillTree(root);
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, source, AuraToolsLog.Debug);
        Object.Destroy(root);
        AuraToolsLog.Warn("[MatchRecords] replay root force-destroyed: root=" + root.name
                          + ", source=" + source + ", tweensKilled=" + killed + ".");
    }

    private static List<SettingUI> FindSettings()
    {
        return NativeSettingUiCacheApi.FindInstances().ToList();
    }

    private static List<UIBase> FindReplayOwnedPresentationUis()
    {
        var manager = WitchUiManager.Instance;
        if (manager == null)
        {
            return new List<UIBase>();
        }

        return manager.GetAllUI()
            .Where(ui => ui != null && ui.gameObject != null)
            .Where(ui => ManagedUiOwnership.IsReplayPresentationOwned(
                ui.gameObject.GetInstanceID()))
            .Distinct()
            .ToList();
    }

    private static void ForceDestroyNativeUi(UIBase ui, string source)
    {
        if (ui == null || ui.gameObject == null)
        {
            return;
        }

        var root = ui.gameObject;
        ClearSelectionWithin(root);
        try
        {
            ui.StopAllCoroutines();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay UI coroutine cleanup degraded: ui="
                              + root.name + ", error=" + ex.Message);
        }

        var manager = WitchUiManager.Instance;
        if (manager != null && ReferenceEquals(manager.Find(root.name), ui))
        {
            manager.RemoveUI(root.name);
        }

        var killed = MatchReplayTweenCleanup.KillTree(root);
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, source, AuraToolsLog.Debug);
        Object.Destroy(root);
        AuraToolsLog.Warn("[MatchRecords] replay native UI force-destroyed: ui=" + root.name
                          + ", source=" + source + ", tweensKilled=" + killed + ".");
    }

    private static void ClearSelectionWithin(GameObject root)
    {
        var eventSystem = EventSystem.current;
        var selected = eventSystem?.currentSelectedGameObject;
        if (eventSystem == null || selected == null)
        {
            return;
        }

        if (selected == root || selected.transform.IsChildOf(root.transform))
        {
            eventSystem.SetSelectedGameObject(null);
        }
    }
}
