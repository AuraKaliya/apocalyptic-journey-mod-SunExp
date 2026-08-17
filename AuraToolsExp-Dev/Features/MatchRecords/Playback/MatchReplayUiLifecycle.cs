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
/// instance or tool overlay into the local-host view, and never hands one back
/// to the menu after teardown.
/// </summary>
internal static class MatchReplayUiLifecycle
{
    private static readonly MatchReplayManagedUiOwnership ManagedUiOwnership = new();

    internal static List<GameObject> SnapshotTransitionRoots()
    {
        return SnapshotOriginTransitionRoots()
            .Concat(FindReplayOwnedPresentationUis().Select(ui => ui.gameObject))
            .Where(root => root != null)
            .Distinct()
            .ToList();
    }

    internal static List<GameObject> SnapshotOriginTransitionRoots()
    {
        return FindSettings()
            .Select(setting => setting.gameObject)
            .Concat(Object.FindObjectsByType<AuraToolsOwnedOverlay>(
                    FindObjectsInactive.Include,
                FindObjectsSortMode.None)
                .Where(marker => marker != null && marker.gameObject != null)
                .Select(marker => marker.gameObject))
            .Where(root => root != null)
            .Distinct()
            .ToList();
    }

    internal static void PrepareForReplayHost()
    {
        // This only removes leftovers from an already completed replay. Native UI
        // created for the current replay is captured below and closed after the
        // network host has fully stopped.
        ForceCloseReplayOwnedPresentationUis("Match replay host prepare");

        var manager = WitchUiManager.Instance
                      ?? throw new InvalidOperationException("UIManager is unavailable at replay host preparation.");
        var baselineIds = manager.GetAllUI()
            .Where(ui => ui != null && ui.gameObject != null)
            .Select(ui => ui.gameObject.GetInstanceID())
            .ToArray();
        ManagedUiOwnership.Capture(baselineIds);
        MatchReplayChatUiLeaseRuntime.BeginReplay("Match replay host prepare");
        AuraToolsLog.Debug("[MatchRecords] replay managed UI baseline captured: count="
                           + ManagedUiOwnership.BaselineCount
                           + ", chatLease=" + MatchReplayChatUiLeaseRuntime.Describe() + ".");
    }

    internal static List<GameObject> SnapshotReplayOwnedPresentationRoots()
    {
        return FindReplayOwnedPresentationUis()
            .Where(ui => ui != null && ui.gameObject != null)
            .Select(ui => ui.gameObject)
            .Distinct()
            .ToList();
    }

    internal static int ReplayOwnedPresentationUiCount => FindReplayOwnedPresentationUis().Count;

    internal static int SettingUiCount => NativeSettingUiCacheApi.FindInstances().Count;

    internal static void RequestCloseReplayOwnedPresentationUis(string source)
    {
        var replayOwned = FindReplayOwnedPresentationUis();
        var selfClosing = replayOwned
            .Where(ui => MatchReplayManagedUiOwnership.IsSelfClosingPresentation(ui.gameObject.name))
            .ToList();
        var requested = replayOwned.Except(selfClosing).ToList();
        foreach (var ui in requested)
        {
            RequestCloseNativeUi(ui, source);
        }

        AuraToolsLog.Debug("[MatchRecords] replay-owned presentation UI close requested: source=" + source
                           + ", count=" + requested.Count
                           + ", names=" + (requested.Count == 0
                               ? "none"
                               : string.Join("|", requested.Select(ui => ui.gameObject.name)))
                           + ", selfClosingAwaited=" + (selfClosing.Count == 0
                               ? "none"
                               : string.Join("|", selfClosing.Select(ui => ui.gameObject.name)))
                           + ".");
    }

    internal static void ForceCloseReplayOwnedPresentationUis(string source)
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

    internal static void RequestCloseOriginUi(string source)
    {
        AuraToolsSettingsRuntime.ReleaseForReplayTransition();
        AuraToolsUi.CloseOwnedOverlays(source);

        var settings = FindSettings();
        foreach (var setting in settings)
        {
            RequestCloseNativeUi(setting, source);
        }

        AuraToolsLog.Debug("[MatchRecords] replay UI boundary close requested: source=" + source
                           + ", settings=" + settings.Count + ".");
    }

    internal static void ForceCloseOriginUi(string source)
    {
        AuraToolsSettingsRuntime.ReleaseForReplayTransition();
        AuraToolsUi.CloseOwnedOverlays(source);
        var settings = FindSettings();
        foreach (var setting in settings)
        {
            ForceDestroyNativeUi(setting, source);
        }

        AuraToolsLog.Warn("[MatchRecords] replay UI boundary force-closed: source=" + source
                          + ", settings=" + settings.Count + ".");
    }

    internal static SettingUI PrewarmNativeSettingUiCache(string source)
    {
        var setting = NativeSettingUiCacheApi.PrewarmAndHideFresh();
        AuraToolsLog.Info("[MatchRecords] native SettingUI cache prewarm requested: source="
                          + source + ", instance=" + setting.GetInstanceID() + ".");
        return setting;
    }

    internal static MatchReplayMenuCacheState CaptureNativeSettingUiCacheState(
        bool inputInfrastructureReady)
    {
        var settings = NativeSettingUiCacheApi.FindInstances();
        var registered = NativeSettingUiCacheApi.GetRegistered();
        var only = settings.Count == 1 ? settings[0] : null;
        var group = only == null ? null : only.GetComponent<CanvasGroup>();
        var manager = WitchUiManager.Instance;
        return new MatchReplayMenuCacheState
        {
            SettingUiCount = settings.Count,
            Registered = registered != null,
            RegisteredMatchesOnlyInstance = only != null
                                            && registered != null
                                            && ReferenceEquals(registered, only),
            ActiveSelf = only != null && only.gameObject.activeSelf,
            BlocksRaycasts = group != null && group.blocksRaycasts,
            ParentIsMainCanvas = only != null
                                 && manager != null
                                 && ReferenceEquals(only.transform.parent, manager.canvasTf),
            InputInfrastructureReady = inputInfrastructureReady
        };
    }

    internal static void ForceNormalizeNativeSettingUiCache(string source)
    {
        var settings = NativeSettingUiCacheApi.FindInstances();
        var registered = NativeSettingUiCacheApi.GetRegistered();
        if (settings.Count != 1
            || registered == null
            || !ReferenceEquals(settings[0], registered))
        {
            return;
        }

        var setting = settings[0];
        ClearSelectionWithin(setting.gameObject);
        var killed = MatchReplayTweenCleanup.KillTree(setting.gameObject);
        var group = setting.GetComponent<CanvasGroup>()
                    ?? setting.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.alpha = 0f;
        setting.transform.SetParent(WitchUiManager.Instance!.canvasTf, false);
        setting.gameObject.SetActive(false);
        AuraToolsLog.Warn("[MatchRecords] native SettingUI cache hide was normalized after timeout: source="
                          + source + ", instance=" + setting.GetInstanceID()
                          + ", tweensKilled=" + killed + ".");
    }

    internal static void RequestCloseNativeUi(UIBase ui, string source)
    {
        if (ui == null || ui.gameObject == null)
        {
            return;
        }

        var root = ui.gameObject;
        ClearSelectionWithin(root);
        UiRaycastSafeDestroyRuntime.DisableRaycasts(root, source, AuraToolsLog.Debug);
        try
        {
            var manager = WitchUiManager.Instance;
            if (manager != null && ReferenceEquals(manager.Find(root.name), ui))
            {
                manager.CloseUI(root.name);
            }
            else
            {
                ui.Close();
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] native UI close degraded; forcing root: ui="
                              + root.name + ", source=" + source + ", error=" + ex.Message);
            ForceDestroyRoot(root, source + " native-close-fallback");
        }
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
                ui.gameObject.GetInstanceID(),
                ui.gameObject.name))
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
