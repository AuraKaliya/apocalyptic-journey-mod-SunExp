using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraUi.Shared;
using AuraToolsExp.Dll.Infrastructure;
using Michsky.MUIP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UiTransitionGuardShared;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.Settings;

public static class AuraToolsSettingsRuntime
{
    private const string AuraTabButtonName = "AuraToolsSettingsTabButton";
    private const string AuraPanelName = "AuraToolsSettingsPanel";
    private const float AuraTabHeight = 60f;
    private const float AuraTabTextSize = 20f;
    private const float AuraTabMinimumTextSize = 18f;
    private const int PanelSortingMargin = 20;
    private static GameObject? activePanel;
    private static Transform? activePanelHost;
    private static Transform? activeTabParent;
    private static SettingUI? activeSetting;
    private static readonly AuraToolsPanelBuildState PanelBuildState = new();
    private static bool loggedHookRegistration;
    private static bool loggedInjectionSuccess;
    private static bool loggedNoTabParent;
    private static bool loggedNativeTabCloneFallback;
    private static bool loggedPanelHostProbe;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "SettingUI.Start", InjectSettings);
        RegisterAfter(modConfig, "SettingUI.OnEnable", InjectSettings);
        RegisterAfter(modConfig, "SettingUI.Load", InjectSettings);
        RegisterAfter(modConfig, "SettingUI.Close", ClosePanel);
        RegisterAfter(modConfig, "SettingUI.Hide", ClosePanel);
        RegisterAfter(modConfig, "SettingUI.OnDestroy", ClearPanel);
        if (!loggedHookRegistration)
        {
            loggedHookRegistration = true;
            AuraToolsLog.Info("[Settings] hooks registered.");
        }
    }

    internal static void HideActivePanel()
    {
        PanelBuildState.CancelBuild();
        if (activePanel != null)
        {
            SetPanelVisible(activePanel, false);
            AuraToolsLog.Debug("[Settings] AuraTools panel hidden; native page state left untouched.");
        }
    }

    internal static void ReleaseForReplayTransition()
    {
        HideActivePanel();
        activePanel = null;
        activePanelHost = null;
        activeTabParent = null;
        activeSetting = null;
        PanelBuildState.Reset();
    }

    internal static Transform OpenForReplayReturn(SettingUI setting)
    {
        if (setting == null || setting.gameObject == null)
            throw new InvalidOperationException("SettingUI is unavailable while returning from replay.");

        InjectSettings(setting, "replay-return");
        var panel = activePanel ?? throw new InvalidOperationException(
            "AuraTools settings panel was not created for replay return.");
        ShowAuraPanel();
        return panel.transform;
    }
    private static void InjectSettings(ModHookContext context)
    {
        try
        {
            if (context.Target is not SettingUI setting)
            {
                return;
            }

            InjectSettings(setting, "dynamic");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[Settings] inject failed", ex);
        }
    }

    private static void InjectSettings(SettingUI setting, string source)
    {
        try
        {
            activeSetting = setting;
            var parent = ResolveTabParent(setting);
            activeTabParent = parent;
            var panelHost = ResolvePanelHost(setting, parent);
            EnsureTabButton(setting, parent);
            BindNativeTabsToHide(parent);
            EnsurePanel(setting, parent, panelHost);
            LogPanelHostProbe(panelHost, parent);
            if (!loggedInjectionSuccess)
            {
                loggedInjectionSuccess = true;
                AuraToolsLog.Info("[Settings] injected from " + source
                                  + "; tabParent=" + DescribeTransform(parent)
                                  + "; panelHost=" + DescribeTransform(panelHost)
                                  + "; hostRect=" + DescribeRect(panelHost)
                                  + "; keyButtonParent=" + DescribeTransform(setting.KeyButton == null ? null : setting.KeyButton.transform.parent)
                                  + "; buttonParent=" + DescribeTransform(setting.ButtonParent)
                                  + "; buttonParentRect=" + DescribeRect(setting.ButtonParent));
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[Settings] inject failed from " + source, ex);
        }
    }

    private static void ClearPanel(ModHookContext context)
    {
        if (!OwnsLifecycle(context))
        {
            return;
        }

        ClosePanel(context);
        activePanel = null;
        activePanelHost = null;
        activeTabParent = null;
        activeSetting = null;
        PanelBuildState.Reset();
    }

    private static void ClosePanel(ModHookContext context)
    {
        if (!OwnsLifecycle(context))
        {
            return;
        }

        HideActivePanel();
        AuraToolsUi.CloseOwnedOverlays("SettingUI disabled");
        UiTransitionGuardRuntime.BeginTransition(
            null,
            AuraToolsIds.ModId,
            "SettingUI disabled",
            6);
        UiTransitionGuardRuntime.ScrubNow(
            null,
            AuraToolsIds.ModId,
            "SettingUI disabled");
    }

    private static bool OwnsLifecycle(ModHookContext context)
    {
        return context.Target is SettingUI setting && activeSetting != null
               && ReferenceEquals(setting, activeSetting);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraToolsHookRegistry.After(config, target, action, "Settings");
    }

    private static Transform? ResolveTabParent(SettingUI setting)
    {
        if (setting.KeyButton != null && setting.KeyButton.transform.parent != null)
        {
            return setting.KeyButton.transform.parent;
        }

        if (setting.ButtonParent != null)
        {
            return setting.ButtonParent;
        }

        var found = FindLikelyButtonRow(setting.transform);
        if (found == null && !loggedNoTabParent)
        {
            loggedNoTabParent = true;
            AuraToolsLog.Warn("[Settings] could not resolve tab parent; fallback tab will be created under SettingUI root.");
        }

        return found ?? setting.transform;
    }

    private static void EnsureTabButton(SettingUI setting, Transform? tabParent)
    {
        if (tabParent == null)
        {
            return;
        }

        var template = setting.KeyButton;
        var existing = tabParent.Find(AuraTabButtonName);
        if (existing != null && template != null && AuraUiNativeButtonCloneAdapter.IsOwnedClone(template, existing.gameObject))
        {
            var configured = AuraUiNativeButtonCloneAdapter.TryConfigureClone(
                template,
                existing.gameObject,
                AuraToolsIds.SettingsTabName,
                ShowAuraPanel,
                AuraTabTextSize,
                AuraTabMinimumTextSize);
            if (configured.Success)
            {
                existing.SetAsLastSibling();
                AdjustTabSize(existing.gameObject);
                existing.gameObject.SetActive(true);
                return;
            }

            RejectUnsafeTabClone(existing.gameObject, configured.FailureReason);
            existing = null;
        }

        if (existing != null && existing.GetComponent<ButtonManager>() != null)
        {
            RejectUnsafeTabClone(existing.gameObject, "existing native-style button has no matching ownership marker");
            existing = null;
        }

        GameObject buttonObject;
        if (existing != null)
        {
            buttonObject = existing.gameObject;
            ConfigureTabButton(buttonObject);
        }
        else
        {
            AuraUiNativeButtonCloneResult? cloneResult = null;
            if (template != null)
            {
                cloneResult = AuraUiNativeButtonCloneAdapter.TryClone(new AuraUiNativeButtonCloneRequest
                {
                    Template = template,
                    Parent = tabParent,
                    CloneName = AuraTabButtonName,
                    Label = AuraToolsIds.SettingsTabName,
                    OnClick = ShowAuraPanel,
                    TextSizeOverride = AuraTabTextSize,
                    MinimumTextSizeOverride = AuraTabMinimumTextSize
                });
            }

            if (cloneResult != null && cloneResult.Success && cloneResult.Root != null)
            {
                buttonObject = cloneResult.Root;
            }
            else
            {
                LogNativeTabCloneFallback(cloneResult?.FailureReason ?? "SettingUI.KeyButton is unavailable");
                buttonObject = CreatePlainTabButton(tabParent);
                ConfigureTabButton(buttonObject);
            }
        }

        buttonObject.transform.SetAsLastSibling();
        AdjustTabSize(buttonObject);
        buttonObject.SetActive(true);
    }

    private static void ConfigureTabButton(GameObject buttonObject)
    {
        var button = buttonObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(ShowAuraPanel);
        }
        else
        {
            var image = buttonObject.GetComponent<Image>() ?? buttonObject.AddComponent<Image>();
            button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(ShowAuraPanel);
        }

        if (button.targetGraphic != null)
        {
            AuraUiButtonFeedback.Apply(button, button.targetGraphic, AuraToolsUi.Accent);
        }

        RemoveTextChildren(buttonObject.transform);
        AuraToolsUi.AddFillText(buttonObject.transform, AuraToolsIds.SettingsTabName, AuraToolsUi.TabFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Accent);
    }

    private static void RejectUnsafeTabClone(GameObject buttonObject, string reason)
    {
        LogNativeTabCloneFallback(reason);
        buttonObject.SetActive(false);
        buttonObject.name = AuraTabButtonName + "-Rejected";
        Object.Destroy(buttonObject);
    }

    private static void LogNativeTabCloneFallback(string reason)
    {
        if (loggedNativeTabCloneFallback)
        {
            return;
        }

        loggedNativeTabCloneFallback = true;
        AuraToolsLog.Warn("[Settings] native KeyButton style clone rejected; using Aura fallback. reason=" + reason);
    }

    private static void BindNativeTabsToHide(Transform? tabParent)
    {
        if (tabParent == null)
        {
            return;
        }

        foreach (Transform child in tabParent)
        {
            if (child == null || child.name == AuraTabButtonName || child.GetComponent<AuraToolsNativeTabRelay>() != null)
            {
                continue;
            }

            // Native page visibility belongs to the game's tab state machine. The relay only
            // removes our opaque overlay and must never deactivate or restore native objects.
            child.gameObject.AddComponent<AuraToolsNativeTabRelay>();
        }
    }

    private static Transform? FindLikelyButtonRow(Transform root)
    {
        foreach (var button in root.GetComponentsInChildren<ButtonManager>(true))
        {
            var parent = button.transform.parent;
            if (parent != null && parent.childCount >= 3)
            {
                return parent;
            }
        }

        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            var parent = button.transform.parent;
            if (parent != null && parent.childCount >= 3)
            {
                return parent;
            }
        }

        return null;
    }

    private static GameObject CreatePlainTabButton(Transform parent)
    {
        var go = AuraToolsUi.CreateRect(
            AuraTabButtonName,
            parent,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(124f, AuraTabHeight));
        AuraToolsUi.AddButtonImage(go, new Color(0.08f, 0.07f, 0.16f, 0.98f));
        go.AddComponent<Button>();
        AuraToolsUi.AddFillText(go.transform, AuraToolsIds.SettingsTabName, AuraToolsUi.TabFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Accent);
        return go;
    }

    private static void AdjustTabSize(GameObject buttonObject)
    {
        if (buttonObject.transform is RectTransform rect)
        {
            rect.sizeDelta = new Vector2(Mathf.Max(rect.sizeDelta.x, 118f), Mathf.Max(rect.sizeDelta.y, AuraTabHeight));
        }

        var layout = buttonObject.GetComponent<LayoutElement>() ?? buttonObject.AddComponent<LayoutElement>();
        layout.minWidth = Mathf.Max(layout.minWidth, 112f);
        layout.preferredWidth = Mathf.Max(layout.preferredWidth, 118f);
        layout.minHeight = Mathf.Max(layout.minHeight, AuraTabHeight);
        layout.preferredHeight = Mathf.Max(layout.preferredHeight, AuraTabHeight);
        layout.flexibleHeight = 0f;
    }

    private static void RemoveTextChildren(Transform root)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (text.transform != root)
            {
                Object.Destroy(text);
            }
        }

        foreach (var component in root.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            if (component == null || !component.GetType().FullName.Contains("TMPro"))
            {
                continue;
            }

            var property = component.GetType().GetProperty("text");
            if (property != null && property.CanWrite)
            {
                try
                {
                    Object.Destroy(component);
                }
                catch
                {
                    // Text component compatibility fallback only.
                }
            }
        }
    }

    private static string DescribeTransform(Transform? transform)
    {
        return transform == null ? "<null>" : transform.name + " children=" + transform.childCount;
    }

    private static string DescribeRect(Transform? transform)
    {
        if (transform is not RectTransform rect)
        {
            return "<no-rect>";
        }

        return rect.name
               + " size=" + Mathf.RoundToInt(rect.rect.width) + "x" + Mathf.RoundToInt(rect.rect.height)
               + " anchor=" + FormatVector(rect.anchorMin) + "-" + FormatVector(rect.anchorMax)
               + " offset=" + FormatVector(rect.offsetMin) + "/" + FormatVector(rect.offsetMax);
    }

    private static string FormatVector(Vector2 vector)
    {
        return "(" + vector.x.ToString("0.##") + "," + vector.y.ToString("0.##") + ")";
    }

    private static void LogPanelHostProbe(Transform panelHost, Transform? tabParent)
    {
        if (loggedPanelHostProbe)
        {
            return;
        }

        loggedPanelHostProbe = true;
        var report = new StringBuilder();
        report.Append("[Settings] panel host probe: host=")
            .Append(DescribeTransform(panelHost))
            .Append(", rect=")
            .Append(DescribeRect(panelHost))
            .Append(", tab=")
            .Append(DescribeTransform(tabParent));
        var count = Mathf.Min(panelHost.childCount, 64);
        for (var i = 0; i < count; i++)
        {
            var child = panelHost.GetChild(i);
            var canvas = child.GetComponent<Canvas>();
            report.Append(" | ")
                .Append(i)
                .Append(':')
                .Append(child.name)
                .Append(" active=")
                .Append(child.gameObject.activeSelf)
                .Append(" rect=")
                .Append(DescribeRect(child))
                .Append(" canvas=")
                .Append(canvas == null
                    ? "none"
                    : canvas.sortingOrder.ToString());
        }
        AuraToolsLog.Debug(report.ToString());
    }

    private static void EnsurePanel(SettingUI setting, Transform? tabParent, Transform panelHost)
    {
        activePanelHost = panelHost;
        activeTabParent = tabParent;
        var existing = panelHost.Find(AuraPanelName);
        if (existing != null)
        {
            var changedPanel = activePanel != existing.gameObject;
            activePanel = existing.gameObject;
            if (changedPanel)
            {
                PanelBuildState.Adopt(
                    existing.GetComponent<AuraToolsPanelBuildMarker>()?.Completed == true);
            }
            PositionPanelInHost(activePanel, panelHost, tabParent);
            EnsurePanelPresentation(activePanel, panelHost);
            return;
        }

        PanelBuildState.Reset();
        activePanel = AuraToolsUi.CreateRect(AuraPanelName, panelHost, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        activePanel.AddComponent<AuraToolsPanelBuildMarker>();
        PositionPanelInHost(activePanel, panelHost, tabParent);
        EnsurePanelPresentation(activePanel, panelHost);
        SetPanelVisible(activePanel, false);
    }

    private static Transform ResolvePanelHost(SettingUI setting, Transform? tabParent)
    {
        var common = FindNearestCommonAncestor(tabParent, setting.ButtonParent);
        var contentHost = FindContentHostUnderCommonAncestor(common, setting.ButtonParent, tabParent);
        if (contentHost != null)
        {
            return contentHost;
        }

        if (setting.ButtonParent?.parent != null)
        {
            return setting.ButtonParent.parent;
        }

        return setting.transform;
    }

    private static Transform? FindContentHostUnderCommonAncestor(Transform? common, Transform? contentDescendant, Transform? tabParent)
    {
        if (common == null || contentDescendant == null)
        {
            return null;
        }

        Transform? best = null;
        foreach (Transform child in common)
        {
            if (child == null
                || child == tabParent
                || child == contentDescendant
                || IsAncestorOrSelf(child, tabParent)
                || !IsAncestorOrSelf(child, contentDescendant))
            {
                continue;
            }

            if (IsReasonablePanelHost(child))
            {
                best = child;
                break;
            }
        }

        return best;
    }

    private static bool IsReasonablePanelHost(Transform candidate)
    {
        if (candidate.name == "setting" || candidate.name == "Setting" || candidate.name == "Content")
        {
            return true;
        }

        if (candidate is not RectTransform rect)
        {
            return false;
        }

        return Mathf.Abs(rect.rect.width) >= 360f && Mathf.Abs(rect.rect.height) >= 260f;
    }

    private static Transform? FindNearestCommonAncestor(Transform? first, Transform? second)
    {
        if (first == null || second == null)
        {
            return null;
        }

        var ancestors = new List<Transform>();
        var current = first;
        while (current != null)
        {
            ancestors.Add(current);
            current = current.parent;
        }

        current = second;
        while (current != null)
        {
            if (ancestors.Contains(current))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private static bool IsAncestorOrSelf(Transform? ancestor, Transform? item)
    {
        if (ancestor == null || item == null)
        {
            return false;
        }

        var current = item;
        while (current != null)
        {
            if (current == ancestor)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void PositionPanelInHost(GameObject panel, Transform panelHost, Transform? tabParent)
    {
        if (panel.transform is not RectTransform rect)
        {
            return;
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.offsetMin = new Vector2(20f, 18f);
        rect.offsetMax = new Vector2(-20f, -ResolveTopInset(panelHost, tabParent));
    }

    private static float ResolveTopInset(Transform panelHost, Transform? tabParent)
    {
        if (!IsAncestorOrSelf(panelHost, tabParent))
        {
            return 18f;
        }

        if (panelHost is RectTransform hostRect && tabParent is RectTransform)
        {
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(hostRect, tabParent);
            var topInset = hostRect.rect.yMax - bounds.min.y + 6f;
            if (!float.IsNaN(topInset) && !float.IsInfinity(topInset) && topInset > 24f && topInset < hostRect.rect.height * 0.5f)
            {
                return Mathf.Clamp(topInset, 44f, 92f);
            }
        }

        return 58f;
    }

    private static void ShowAuraPanel()
    {
        if (activePanel == null)
        {
            AuraToolsLog.Warn("[Settings] AuraTools tab ignored because its active panel reference is missing.");
            return;
        }

        AuraToolsLog.Debug("[Settings] AuraTools tab selected: panel="
                           + activePanel.GetInstanceID()
                           + ", parent=" + (activePanel.transform.parent == null
                               ? "none"
                               : activePanel.transform.parent.name)
                           + ", built=" + PanelBuildState.IsBuilt + ".");
        if (activePanelHost != null)
        {
            EnsurePanelPresentation(activePanel, activePanelHost);
        }
        SetPanelVisible(activePanel, true);
        activePanel.transform.SetAsLastSibling();
        activePanel.GetComponent<ToolboxSettingsShell>()?.Activate();
        if (!PanelBuildState.IsBuilt)
        {
            BeginInitialPanelBuild(activePanel);
        }
        LogPanelRenderState(activePanel, "immediate");
        if (!AuraSharedFrameScheduler.StartCoroutine(
                "AuraTools.Settings.VisibilityProbe",
                LogPanelRenderStateAfterLayout(activePanel)))
        {
            AuraToolsLog.Warn("[Settings] panel visibility probe scheduler rejected the request.");
        }
    }

    private static void EnsurePanelPresentation(GameObject panel, Transform panelHost)
    {
        var background = panel.GetComponent<Image>() ?? AuraToolsUi.AddImage(panel, ToolboxVisualSpec.Workspace);
        background.color = ToolboxVisualSpec.Workspace;
        background.raycastTarget = true;

        var group = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();
        group.alpha = panel.activeSelf ? 1f : 0f;
        group.interactable = panel.activeSelf;
        group.blocksRaycasts = panel.activeSelf;

        var canvas = panel.GetComponent<Canvas>() ?? panel.AddComponent<Canvas>();
        var parentCanvas = panelHost.GetComponentInParent<Canvas>();
        canvas.overrideSorting = true;
        PlacePanelAboveNativeCanvases(panel, panelHost, canvas, parentCanvas);
        if (panel.GetComponent<GraphicRaycaster>() == null)
        {
            panel.AddComponent<GraphicRaycaster>();
        }
    }

    private static void PlacePanelAboveNativeCanvases(
        GameObject panel,
        Transform panelHost,
        Canvas panelCanvas,
        Canvas? parentCanvas)
    {
        var highestLayerId = parentCanvas == null ? 0 : parentCanvas.sortingLayerID;
        var highestLayerValue = SortingLayer.GetLayerValueFromID(highestLayerId);
        var highestOrder = parentCanvas == null ? 0 : parentCanvas.sortingOrder;
        foreach (var candidate in panelHost.GetComponentsInChildren<Canvas>(true))
        {
            if (candidate == null
                || candidate == panelCanvas
                || IsAncestorOrSelf(panel.transform, candidate.transform))
            {
                continue;
            }

            var candidateLayerValue = SortingLayer.GetLayerValueFromID(candidate.sortingLayerID);
            if (candidateLayerValue < highestLayerValue
                || candidateLayerValue == highestLayerValue
                && candidate.sortingOrder <= highestOrder)
            {
                continue;
            }

            highestLayerId = candidate.sortingLayerID;
            highestLayerValue = candidateLayerValue;
            highestOrder = candidate.sortingOrder;
        }

        panelCanvas.sortingLayerID = highestLayerId;
        panelCanvas.sortingOrder = Mathf.Clamp(
            highestOrder + PanelSortingMargin,
            short.MinValue,
            short.MaxValue);
    }

    private static void SetPanelVisible(GameObject panel, bool visible)
    {
        var group = panel.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        panel.SetActive(visible);
    }

    private static IEnumerator LogPanelRenderStateAfterLayout(GameObject panel)
    {
        yield return null;
        if (panel == null)
        {
            yield break;
        }

        Canvas.ForceUpdateCanvases();
        if (panel.transform is RectTransform panelRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }
        LogPanelRenderState(panel, "frame+1");
    }

    private static void LogPanelRenderState(GameObject panel, string phase)
    {
        if (panel == null)
        {
            return;
        }

        var group = panel.GetComponent<CanvasGroup>();
        var canvas = panel.GetComponent<Canvas>();
        var rootCanvas = canvas == null ? null : canvas.rootCanvas;
        var renderCamera = rootCanvas == null ? null : rootCanvas.worldCamera;
        var workspace = panel.transform.Find("ToolboxWorkspace");
        var activeGraphics = panel.GetComponentsInChildren<Graphic>(true)
            .Count(graphic => graphic != null
                              && graphic.enabled
                              && graphic.gameObject.activeInHierarchy);
        AuraToolsLog.Debug(
            "[Settings] panel render state: phase=" + phase
            + ", panel=" + panel.GetInstanceID()
            + ", activeSelf=" + panel.activeSelf
            + ", activeInHierarchy=" + panel.activeInHierarchy
            + ", rect=" + DescribeRect(panel.transform)
            + ", alpha=" + (group == null ? "none" : group.alpha.ToString("0.##"))
            + ", interactable=" + (group != null && group.interactable)
            + ", blocksRaycasts=" + (group != null && group.blocksRaycasts)
            + ", canvasEnabled=" + (canvas != null && canvas.enabled)
            + ", layer=" + panel.layer + "/host=" + (panel.transform.parent == null ? "none" : panel.transform.parent.gameObject.layer.ToString())
            + ", renderMode=" + (rootCanvas == null ? "none" : rootCanvas.renderMode.ToString())
            + ", cameraMask=" + (renderCamera == null ? "none" : renderCamera.cullingMask.ToString())
            + ", sortingLayer=" + (canvas == null ? "none" : canvas.sortingLayerName)
            + ", sortingOrder=" + (canvas == null ? "none" : canvas.sortingOrder.ToString())
            + ", workspace=" + (workspace == null ? "none" : DescribeRect(workspace))
            + ", workspaceActive=" + (workspace != null && workspace.gameObject.activeInHierarchy)
            + ", activeGraphics=" + activeGraphics
            + ".");
    }

    private static void BeginInitialPanelBuild(GameObject panel)
    {
        var ticket = PanelBuildState.Begin();
        if (ticket == 0)
        {
            return;
        }

        if (!AuraSharedFrameScheduler.StartCoroutine(
                "AuraTools.Settings.BuildPanel",
                BuildPanelAcrossFrames(panel.transform, ticket)))
        {
            PanelBuildState.Complete(ticket, false);
            AuraToolsLog.Warn("[Settings] persistent scheduler rejected panel build; it will retry on the next tab open.");
        }
    }

    internal static IEnumerator BuildPanelAcrossFrames(Transform panel, int ticket)
    {
        var completed = false;
        try
        {
            if (!CanContinuePanelBuild(panel, ticket))
            {
                yield break;
            }

            var marker = panel.GetComponent<AuraToolsPanelBuildMarker>()
                         ?? panel.gameObject.AddComponent<AuraToolsPanelBuildMarker>();
            marker.Completed = false;
            AuraToolsUi.ClearChildren(panel);
            var layout = panel.gameObject.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            }
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ToolboxSettingsShell.Build(panel);
            Canvas.ForceUpdateCanvases();
            if (panel is RectTransform panelRect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            }
            yield return null;
            if (!CanContinuePanelBuild(panel, ticket)) yield break;
            marker.Completed = true;
            completed = true;
        }
        finally
        {
            PanelBuildState.Complete(ticket, completed);
        }
    }

    private static bool CanContinuePanelBuild(Transform panel, int ticket)
    {
        return panel != null
               && panel.gameObject.activeInHierarchy
               && activePanel == panel.gameObject
               && PanelBuildState.IsCurrent(ticket);
    }
}

internal sealed class AuraToolsPanelBuildMarker : MonoBehaviour
{
    internal bool Completed { get; set; }
}

internal sealed class AuraToolsNativeTabRelay : MonoBehaviour, IPointerDownHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        AuraToolsSettingsRuntime.HideActivePanel();
    }
}
