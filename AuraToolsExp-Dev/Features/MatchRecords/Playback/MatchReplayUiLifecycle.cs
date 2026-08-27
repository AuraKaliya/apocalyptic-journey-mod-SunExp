using System;
using System.Linq;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI;
using Object = UnityEngine.Object;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>Owns only the menu-to-replay UI boundary; combat UI is never initialized.</summary>
internal static class MatchReplayUiLifecycle
{
    internal static void PrepareForReplayView()
    {
        var stale = GameObject.Find("AuraToolsReplaySceneV12");
        if (stale != null) ForceDestroyRoot(stale, "Match replay stale scene cleanup");
    }

    internal static int SettingUiCount => NativeSettingUiCacheApi.FindInstances().Count;

    internal static void CloseOriginUi(string source)
    {
        AuraToolsSettingsRuntime.ReleaseForReplayTransition();
        AuraToolsUi.CloseOwnedOverlays(source);
        foreach (var setting in NativeSettingUiCacheApi.FindInstances().ToList())
            ForceDestroyNativeUi(setting, source);
    }

    internal static void ForceDestroyRoot(GameObject root, string source)
    {
        if (root == null) return;
        ClearSelectionWithin(root);
        var ui = root.GetComponent<UIBase>();
        if (ui != null)
        {
            ForceDestroyNativeUi(ui, source);
            return;
        }
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, source, AuraToolsLog.Debug);
        Object.Destroy(root);
    }

    private static void ForceDestroyNativeUi(UIBase ui, string source)
    {
        if (ui == null || ui.gameObject == null) return;
        var root = ui.gameObject;
        ClearSelectionWithin(root);
        try { ui.StopAllCoroutines(); } catch (Exception ex) { AuraToolsLog.Warn("[MatchRecords] UI cleanup degraded: " + ex.Message); }
        var manager = WitchUiManager.Instance;
        if (manager != null && ReferenceEquals(manager.Find(root.name), ui)) manager.RemoveUI(root.name);
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, source, AuraToolsLog.Debug);
        Object.Destroy(root);
    }

    private static void ClearSelectionWithin(GameObject root)
    {
        var system = EventSystem.current;
        var selected = system?.currentSelectedGameObject;
        if (system != null && selected != null && (selected == root || selected.transform.IsChildOf(root.transform)))
            system.SetSelectedGameObject(null);
    }
}
