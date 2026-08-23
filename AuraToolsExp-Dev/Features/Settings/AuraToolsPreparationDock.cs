using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.UI;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.Settings;

/// <summary>
/// The preparation screen owns one responsive Toolbox-styled action dock.
/// Feature modules contribute actions; they never position sibling buttons
/// relative to the game's ready button themselves.
/// </summary>
internal static class AuraToolsPreparationDock
{
    private const string RootName = "AuraToolsPreparationDock";
    private const float ButtonWidth = 112f;
    private const float ButtonHeight = 38f;
    private const float Spacing = 6f;
    private const float Padding = 6f;
    private static readonly Dictionary<string, Descriptor> Descriptors =
        new(StringComparer.OrdinalIgnoreCase);
    private static GameEntryUI? currentEntry;
    private static GameObject? root;
    private static string signature = "";
    private static bool initialized;

    internal static bool IsActive => root != null && root.activeInHierarchy;

    internal static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        AuraToolsHookRegistry.After(
            modConfig,
            "GameEntryUI.Init",
            context => Attach(context.Target as GameEntryUI),
            "PreparationDock");
        AuraToolsHookRegistry.After(
            modConfig,
            "GameEntryUI.ShowCareer",
            context => Attach(context.Target as GameEntryUI),
            "PreparationDock");
        AuraToolsHookRegistry.Before(
            modConfig,
            "GameEntryUI.StartGame",
            _ => Detach(),
            "PreparationDock");
    }

    internal static void Register(
        string id,
        string label,
        int order,
        Func<bool> visible,
        Action<Transform> action)
    {
        var key = (id ?? "").Trim();
        if (key.Length == 0 || action == null) return;
        Descriptors[key] = new Descriptor
        {
            Id = key,
            Label = string.IsNullOrWhiteSpace(label) ? key : label.Trim(),
            Order = order,
            Visible = visible ?? (() => true),
            Action = action
        };
        signature = "";
        Refresh();
    }

    internal static void Attach(GameEntryUI? entry)
    {
        if (entry != null) currentEntry = entry;
        Refresh();
    }

    internal static void Refresh()
    {
        if (currentEntry == null || currentEntry.transform == null)
        {
            DestroyRoot("entry unavailable");
            return;
        }

        var ready = currentEntry.transform.Find("ForeBack/Button") as RectTransform;
        if (ready == null || ready.parent == null)
        {
            DestroyRoot("ready button unavailable");
            return;
        }

        var visible = Descriptors.Values
            .Where(IsVisible)
            .OrderBy(value => value.Order)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (visible.Length == 0)
        {
            DestroyRoot("no visible actions");
            return;
        }

        var nextSignature = string.Join("|", visible.Select(value => value.Id + ":" + value.Label));
        if (root == null || root.transform.parent != ready.parent)
        {
            DestroyRoot("reparent");
            root = AuraToolsUi.CreateRect(
                RootName,
                ready.parent,
                ready.anchorMin,
                ready.anchorMax,
                ready.pivot,
                Vector2.zero);
            AuraToolsUi.AddSectionImage(root);
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            root.AddComponent<AuraToolsPreparationDockLayoutDriver>();
            signature = "";
        }

        if (!string.Equals(signature, nextSignature, StringComparison.Ordinal))
        {
            AuraToolsUi.ClearChildren(root.transform);
            foreach (var descriptor in visible)
            {
                AddButton(root.transform, descriptor);
            }
            signature = nextSignature;
        }

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(
            Padding * 2f + visible.Length * ButtonWidth + Math.Max(0, visible.Length - 1) * Spacing,
            ButtonHeight + Padding * 2f);
        Position(rect, ready);
        root.SetActive(true);
        root.transform.SetAsLastSibling();
    }

    internal static void Reposition()
    {
        if (root == null || currentEntry == null) return;
        var ready = currentEntry.transform.Find("ForeBack/Button") as RectTransform;
        if (ready == null) return;
        Position(root.GetComponent<RectTransform>(), ready);
    }

    private static void Position(RectTransform dock, RectTransform ready)
    {
        dock.anchorMin = ready.anchorMin;
        dock.anchorMax = ready.anchorMax;
        dock.pivot = new Vector2(0.5f, 0f);
        var readyWidth = Mathf.Max(Mathf.Abs(ready.rect.width), Mathf.Abs(ready.sizeDelta.x));
        var readyHeight = Mathf.Max(Mathf.Abs(ready.rect.height), Mathf.Abs(ready.sizeDelta.y));
        var parent = ready.parent as RectTransform;
        var parentLeft = parent == null ? -100000f : -parent.rect.width * parent.pivot.x;
        var parentRight = parent == null ? 100000f : parentLeft + parent.rect.width;
        var placement = AuraToolsPreparationDockLayoutPolicy.AboveReadyButton(
            ready.anchoredPosition.x,
            ready.anchoredPosition.y,
            readyWidth,
            readyHeight,
            ready.pivot.x,
            ready.pivot.y,
            dock.sizeDelta.x,
            parentLeft,
            parentRight);
        dock.anchoredPosition = new Vector2(placement.X, placement.Y);
    }

    private static void AddButton(Transform parent, Descriptor descriptor)
    {
        var buttonRoot = AuraToolsUi.CreateLayout("Action-" + descriptor.Id, parent);
        AuraToolsUi.SetFixedSize(buttonRoot, ButtonWidth, ButtonHeight);
        var image = AuraToolsUi.AddButtonImage(buttonRoot, AuraToolsUi.Header);
        var button = buttonRoot.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, AuraToolsUi.Accent);
        button.onClick.AddListener(() =>
        {
            if (root != null) descriptor.Action(root.transform.parent);
        });
        AuraToolsUi.AddFillText(
            buttonRoot.transform,
            descriptor.Label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter,
            AuraToolsUi.Text);
    }

    private static bool IsVisible(Descriptor descriptor)
    {
        try { return descriptor.Visible(); }
        catch { return false; }
    }

    private static void Detach()
    {
        currentEntry = null;
        DestroyRoot("leave preparation");
    }

    private static void DestroyRoot(string source)
    {
        if (root == null) return;
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, "PreparationDock: " + source);
        Object.Destroy(root);
        root = null;
        signature = "";
    }

    private sealed class Descriptor
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public int Order { get; set; }
        public Func<bool> Visible { get; set; } = () => false;
        public Action<Transform> Action { get; set; } = _ => { };
    }
}

internal sealed class AuraToolsPreparationDockLayoutDriver : MonoBehaviour
{
    private void OnEnable() => AuraToolsPreparationDock.Reposition();

    private void OnRectTransformDimensionsChange() => AuraToolsPreparationDock.Reposition();
}
