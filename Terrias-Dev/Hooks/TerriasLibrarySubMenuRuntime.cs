using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Object = UnityEngine.Object;

namespace Terrias.Dll.Hooks;

public enum TerriasLibrarySubMenuSlot
{
    TopLeft,
    BottomRight,
    TopLeftUpper
}

public sealed class TerriasLibrarySubMenuEntry
{
    public TerriasLibrarySubMenuEntry(
        string id,
        string objectName,
        Func<string> label,
        TerriasLibrarySubMenuSlot slot,
        Action onClick)
    {
        Id = id;
        ObjectName = objectName;
        Label = label;
        Slot = slot;
        OnClick = onClick;
    }

    public string Id { get; }

    public string ObjectName { get; }

    public Func<string> Label { get; }

    public TerriasLibrarySubMenuSlot Slot { get; }

    public Action OnClick { get; }
}

public static class TerriasLibrarySubMenuRuntime
{
    private const string LogPrefix = "[LibrarySubMenu]";
    private const string BrushName = "Terrias_LibrarySubMenuBrush";
    private const string TextName = "Terrias_LibrarySubMenuText";
    private const float FallbackButtonWidth = 156f;
    private const float FallbackButtonHeight = 50f;
    private const float ButtonGap = 12f;
    private static readonly Dictionary<string, TerriasLibrarySubMenuEntry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, float> LastOpenTimes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedNativeFallbacks = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "HouseManager.Awake", EnsureButtons);
        RegisterAfter(modConfig, "HouseManager.OnEnable", EnsureButtons);
        RegisterAfter(modConfig, "HouseManager.ChangeUIShow", EnsureButtons);
        RegisterAfter(modConfig, "HouseManager.OpenWindowByIndex", EnsureButtons);
        RegisterAfter(modConfig, "HouseManager.OpenLibrary", EnsureButtons);
        TerriasLog.Info(LogPrefix + " runtime initialized.");
    }

    public static void Register(TerriasLibrarySubMenuEntry entry)
    {
        if (entry == null
            || string.IsNullOrWhiteSpace(entry.Id)
            || string.IsNullOrWhiteSpace(entry.ObjectName)
            || entry.Label == null
            || entry.OnClick == null)
        {
            return;
        }

        Entries[entry.Id.Trim()] = entry;
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "LibrarySubMenu");
    }

    private static void EnsureButtons(ModHookContext context)
    {
        try
        {
            var library = HouseLibraryUiApi.Resolve(context.Target);
            if (library == null)
            {
                return;
            }

            foreach (var entry in Entries.Values.OrderBy(item => item.Slot).ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                EnsureButton(library, entry);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " failed to create library buttons: " + ex.Message);
        }
    }

    private static void EnsureButton(HouseLibraryUiContext library, TerriasLibrarySubMenuEntry entry)
    {
        var label = SafeLabel(entry);
        var existing = FindDeepChild(library.Window, entry.ObjectName);
        if (existing != null
            && library.TemplateManager != null
            && AuraUiNativeButtonCloneAdapter.IsOwnedClone(library.TemplateManager, existing))
        {
            if (TryConfigureNativeButton(existing, library, entry, label, out var failureReason))
            {
                return;
            }

            RejectUnsafeButton(existing, entry, failureReason);
            existing = null;
        }

        if (existing != null && HouseLibraryUiApi.ContainsComponentNamed(existing.transform, "ButtonManager"))
        {
            RejectUnsafeButton(existing, entry, "existing native-style button has no matching ownership marker");
            existing = null;
        }

        if (existing != null)
        {
            ConfigureFallbackButton(existing, library, entry, label);
            return;
        }

        if (library.TemplateManager != null)
        {
            var cloneResult = AuraUiNativeButtonCloneAdapter.TryClone(new AuraUiNativeButtonCloneRequest
            {
                Template = library.TemplateManager,
                Parent = library.Parent,
                CloneName = entry.ObjectName,
                Label = label,
                OnClick = () => OpenEntry(entry),
                StripOwnerBehaviours = HouseLibraryUiApi.StripClonedHouseItems
            });
            var failureReason = cloneResult.FailureReason;
            if (cloneResult.Success
                && cloneResult.Root != null
                && TryConfigureNativeButton(cloneResult.Root, library, entry, label, out failureReason))
            {
                return;
            }

            if (cloneResult.Root != null)
            {
                RejectUnsafeButton(cloneResult.Root, entry, failureReason);
            }
            else
            {
                LogNativeFallback(entry, cloneResult.FailureReason);
            }
        }
        else
        {
            LogNativeFallback(entry, "the 查找典籍 template has no ButtonManager");
        }

        var fallback = CreateFallbackButton(library.Parent, entry.ObjectName);
        ConfigureFallbackButton(fallback, library, entry, label);
    }

    private static bool TryConfigureNativeButton(
        GameObject root,
        HouseLibraryUiContext library,
        TerriasLibrarySubMenuEntry entry,
        string label,
        out string failureReason)
    {
        root.name = entry.ObjectName;
        if (root.transform.parent != library.Parent)
        {
            root.transform.SetParent(library.Parent, false);
        }

        root.SetActive(false);
        HouseLibraryUiApi.StripClonedHouseItems(root);
        SetChildrenActiveByName(root.transform, "New", false);
        ConfigureButtonRect(root, library, entry.Slot);
        var configured = AuraUiNativeButtonCloneAdapter.TryConfigureClone(
            library.TemplateManager!,
            root,
            label,
            () => OpenEntry(entry));
        if (!configured.Success)
        {
            failureReason = configured.FailureReason;
            return false;
        }

        root.SetActive(true);
        failureReason = "";
        return true;
    }

    private static void ConfigureFallbackButton(
        GameObject root,
        HouseLibraryUiContext library,
        TerriasLibrarySubMenuEntry entry,
        string label)
    {
        root.name = entry.ObjectName;
        if (root.transform.parent != library.Parent)
        {
            root.transform.SetParent(library.Parent, false);
        }

        root.SetActive(true);
        SetChildrenActiveByName(root.transform, "New", false);
        ConfigureButtonRect(root, library, entry.Slot);
        ApplyFallbackSprite(root);
        ConfigureFallbackText(root, label);
        ConfigureFallbackClick(root, entry);
    }

    private static GameObject CreateFallbackButton(Transform parent, string objectName)
    {
        var root = new GameObject(objectName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var image = root.AddComponent<Image>();
        image.raycastTarget = true;
        var button = root.AddComponent<Button>();
        button.targetGraphic = image;
        return root;
    }

    private static void ConfigureButtonRect(
        GameObject root,
        HouseLibraryUiContext library,
        TerriasLibrarySubMenuSlot slot)
    {
        var rect = root.GetComponent<RectTransform>() ?? root.AddComponent<RectTransform>();
        var cardRect = library.CardButton?.GetComponent<RectTransform>();
        var rollRect = library.RollButton?.GetComponent<RectTransform>();
        var templateRect = (library.Template ?? library.CardButton ?? library.RollButton)?.GetComponent<RectTransform>();
        if (templateRect != null)
        {
            CopyRectSettings(rect, templateRect);
        }
        else
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(FallbackButtonWidth, FallbackButtonHeight);
        }

        PositionButton(rect, library.Parent, cardRect, rollRect, templateRect, slot);
        var element = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        element.ignoreLayout = library.Parent.GetComponent<LayoutGroup>() != null;
        element.minWidth = Math.Max(FallbackButtonWidth, rect.sizeDelta.x);
        element.preferredWidth = element.minWidth;
        element.minHeight = Math.Max(FallbackButtonHeight, rect.sizeDelta.y);
        element.preferredHeight = element.minHeight;
    }

    private static void PositionButton(
        RectTransform rect,
        Transform parent,
        RectTransform? cardRect,
        RectTransform? rollRect,
        RectTransform? templateRect,
        TerriasLibrarySubMenuSlot slot)
    {
        if (cardRect != null
            && rollRect != null
            && cardRect.parent == rollRect.parent
            && rect.parent == cardRect.parent)
        {
            rect.anchoredPosition = slot switch
            {
                TerriasLibrarySubMenuSlot.TopLeft => new Vector2(cardRect.anchoredPosition.x, rollRect.anchoredPosition.y),
                TerriasLibrarySubMenuSlot.TopLeftUpper => new Vector2(
                    cardRect.anchoredPosition.x,
                    rollRect.anchoredPosition.y + Math.Max(FallbackButtonHeight, rollRect.rect.height) + ButtonGap),
                _ => new Vector2(rollRect.anchoredPosition.x, cardRect.anchoredPosition.y)
            };
            return;
        }

        if (slot is TerriasLibrarySubMenuSlot.TopLeft or TerriasLibrarySubMenuSlot.TopLeftUpper)
        {
            if (rollRect != null && rect.parent == rollRect.parent)
            {
                var width = Math.Max(FallbackButtonWidth, rollRect.rect.width);
                rect.anchoredPosition = rollRect.anchoredPosition + new Vector2(
                    -width - ButtonGap,
                    slot == TerriasLibrarySubMenuSlot.TopLeftUpper ? Math.Max(FallbackButtonHeight, rollRect.rect.height) + ButtonGap : 0f);
                return;
            }

            if (cardRect != null && rect.parent == cardRect.parent)
            {
                var height = Math.Max(FallbackButtonHeight, cardRect.rect.height);
                rect.anchoredPosition = cardRect.anchoredPosition + new Vector2(
                    0f,
                    (slot == TerriasLibrarySubMenuSlot.TopLeftUpper ? 2f : 1f) * (height + ButtonGap));
                return;
            }
        }
        else
        {
            if (cardRect != null && rect.parent == cardRect.parent)
            {
                var width = Math.Max(FallbackButtonWidth, cardRect.rect.width);
                rect.anchoredPosition = cardRect.anchoredPosition + new Vector2(width + ButtonGap, 0f);
                return;
            }

            if (rollRect != null && rect.parent == rollRect.parent)
            {
                var height = Math.Max(FallbackButtonHeight, rollRect.rect.height);
                rect.anchoredPosition = rollRect.anchoredPosition + new Vector2(0f, -height - ButtonGap);
                return;
            }
        }

        if (templateRect != null)
        {
            var width = Math.Max(FallbackButtonWidth, templateRect.rect.width);
            var height = Math.Max(FallbackButtonHeight, templateRect.rect.height);
            rect.anchoredPosition = templateRect.anchoredPosition
                                    + (slot is TerriasLibrarySubMenuSlot.TopLeft or TerriasLibrarySubMenuSlot.TopLeftUpper
                                        ? new Vector2(-width - ButtonGap,
                                            slot == TerriasLibrarySubMenuSlot.TopLeftUpper ? height + ButtonGap : 0f)
                                        : new Vector2(0f, -height - ButtonGap));
            return;
        }

        rect.anchoredPosition = slot switch
        {
            TerriasLibrarySubMenuSlot.TopLeft => new Vector2(-FallbackButtonWidth - ButtonGap, FallbackButtonHeight + ButtonGap),
            TerriasLibrarySubMenuSlot.TopLeftUpper => new Vector2(-FallbackButtonWidth - ButtonGap, 2f * (FallbackButtonHeight + ButtonGap)),
            _ => new Vector2(-18f, 18f)
        };
    }

    private static void CopyRectSettings(RectTransform target, RectTransform source)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }

    private static void ApplyFallbackSprite(GameObject root)
    {
        var sprite = TerriasUiSprites.LibrarySubMenuButton(LogPrefix)
                     ?? TerriasUiSprites.Button(LogPrefix);
        foreach (Transform child in root.transform)
        {
            child.gameObject.SetActive(false);
        }

        var rootImage = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        rootImage.sprite = null;
        rootImage.type = Image.Type.Simple;
        rootImage.color = new Color(1f, 1f, 1f, 0f);
        rootImage.raycastTarget = true;
        if (sprite == null)
        {
            return;
        }

        var brush = FindDirectChild(root.transform, BrushName);
        if (brush == null)
        {
            brush = new GameObject(BrushName, typeof(RectTransform), typeof(Image)).transform;
            brush.SetParent(root.transform, false);
        }

        brush.gameObject.SetActive(true);
        brush.SetAsFirstSibling();
        var brushRect = brush.GetComponent<RectTransform>()!;
        brushRect.anchorMin = Vector2.zero;
        brushRect.anchorMax = Vector2.one;
        brushRect.pivot = new Vector2(0.5f, 0.5f);
        brushRect.offsetMin = Vector2.zero;
        brushRect.offsetMax = Vector2.zero;
        var image = brush.GetComponent<Image>()!;
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.fillCenter = true;
        image.color = Color.white;
        image.raycastTarget = false;
        image.preserveAspect = false;
    }

    private static void ConfigureFallbackText(GameObject root, string label)
    {
        var textTransform = FindDirectChild(root.transform, TextName);
        if (textTransform == null)
        {
            textTransform = new GameObject(TextName, typeof(RectTransform)).transform;
            textTransform.SetParent(root.transform, false);
        }

        textTransform.gameObject.SetActive(true);
        textTransform.SetAsLastSibling();
        var rect = textTransform.GetComponent<RectTransform>()!;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(8f, 4f);
        rect.offsetMax = new Vector2(-8f, -4f);
        var text = textTransform.GetComponent<Text>() ?? textTransform.gameObject.AddComponent<Text>();
        text.text = label;
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = 18;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = 18;
        text.raycastTarget = false;
    }

    private static void ConfigureFallbackClick(GameObject root, TerriasLibrarySubMenuEntry entry)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.raycastTarget = true;
        var button = root.GetComponent<Button>() ?? root.AddComponent<Button>();
        button.enabled = true;
        button.interactable = true;
        var visual = FindDirectChild(root.transform, BrushName)?.GetComponent<Image>() ?? image;
        AuraUiButtonFeedback.Apply(button, visual, TerriasUiComponents.Theme.Accent);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OpenEntry(entry));
    }

    private static void OpenEntry(TerriasLibrarySubMenuEntry entry)
    {
        var now = Time.unscaledTime;
        if (LastOpenTimes.TryGetValue(entry.Id, out var last) && now - last < 0.08f)
        {
            return;
        }

        LastOpenTimes[entry.Id] = now;
        try
        {
            entry.OnClick();
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " entry failed: id=" + entry.Id + ", error=" + ex.Message);
        }
    }

    private static string SafeLabel(TerriasLibrarySubMenuEntry entry)
    {
        try
        {
            var label = entry.Label();
            return string.IsNullOrWhiteSpace(label) ? entry.Id : label.Trim();
        }
        catch
        {
            return entry.Id;
        }
    }

    private static void RejectUnsafeButton(GameObject root, TerriasLibrarySubMenuEntry entry, string reason)
    {
        LogNativeFallback(entry, reason);
        root.SetActive(false);
        root.name = entry.ObjectName + "-Rejected";
        Object.Destroy(root);
    }

    private static void LogNativeFallback(TerriasLibrarySubMenuEntry entry, string reason)
    {
        if (!LoggedNativeFallbacks.Add(entry.Id))
        {
            return;
        }

        TerriasLog.Warn(LogPrefix + " native style clone rejected; using Aura fallback. id="
                       + entry.Id + ", reason=" + reason);
    }

    private static Transform? FindDirectChild(Transform parent, string name)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static GameObject? FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name)
        {
            return parent.gameObject;
        }

        for (var i = 0; i < parent.childCount; i++)
        {
            var found = FindDeepChild(parent.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void SetChildrenActiveByName(Transform parent, string childName, bool active)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
            {
                child.gameObject.SetActive(active);
            }

            SetChildrenActiveByName(child, childName, active);
        }
    }
}
