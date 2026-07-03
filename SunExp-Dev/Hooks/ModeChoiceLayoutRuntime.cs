using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class ModeChoiceLayoutRuntime
{
    private const float FallbackEntryGap = 42f;
    private const float ModeChoiceSidePadding = 96f;
    private const float DragStartThreshold = 8f;
    private const float MinBoundsSize = 1f;
    private const float OverlapTolerance = 2f;
    private const float FallbackButtonWidth = 260f;
    private const float FallbackButtonHeight = 78f;
    private const int DiagnosticLogLimit = 4;
    private const int VisibleLayoutSlotCount = 4;
    private const string OldOverlayRootName = "SunExp_ModeChoiceOverlayRoot";
    private const string NativeReserveSlotPrefix = "SunExp_NativeReserve_";
    private const string NativeProxySlotPrefix = "SunExp_NativeProxy_";
    private const string CustomSlotPrefix = "SunExp_CustomSlot_";
    private const string LegacyDragSurfaceName = "SunExp_ModeChoiceDragSurface";
    private const string BackgroundDragSurfaceName = "SunExp_ModeChoiceBackgroundDragSurface";
    private static readonly string[] KnownNativeEntryNames =
    {
        "NormalMode",
        "SublimationMode",
        "SlotMode",
        "StoryMode"
    };

    private static bool initialized;
    private static int diagnosticLogCount;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraSharedHooks.RegisterAfter(modConfig, "ModeChoiceUI.Init", ApplyRegisteredEntries, SunExpLog.Debug, message => SunExpLog.Warn("mode choice " + message));
        AuraSharedHooks.RegisterAfter(modConfig, "ModeChoiceUI.DataUpdate", ApplyRegisteredEntries, SunExpLog.Debug, message => SunExpLog.Warn("mode choice " + message));
    }

    private static void ApplyRegisteredEntries(ModHookContext context)
    {
        try
        {
            if (context.Target is not ModeChoiceUI modeChoice)
            {
                return;
            }

            var modeList = modeChoice.transform.Find("ModeList");
            if (modeList == null)
            {
                SunExpLog.Warn("[ModeChoiceLayout] ModeList not found.");
                return;
            }

            var registered = ModeChoiceEntryRegistry.Entries();
            var registeredNames = registered
                .Select(definition => definition.ObjectName)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var definition in registered)
            {
                EnsureRegisteredEntry(modeChoice, modeList, definition);
            }

            AppendRegisteredEntries(modeChoice, modeList, registered, registeredNames);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Mode choice entry layout failed", ex);
        }
    }

    private static void EnsureRegisteredEntry(ModeChoiceUI modeChoice, Transform modeList, ModeChoiceEntryDefinition definition)
    {
        var entry = modeList.Find(definition.ObjectName)?.gameObject;
        if (entry == null)
        {
            var template = FindTemplate(modeList, definition.TemplateName);
            if (template == null)
            {
                SunExpLog.Warn("[ModeChoiceLayout] template not found for " + definition.ObjectName + ": " + definition.TemplateName);
                return;
            }

            entry = UnityEngine.Object.Instantiate(template.gameObject, modeList);
            entry.name = definition.ObjectName;
            entry.transform.SetAsLastSibling();
            entry.transform.localScale = template.localScale;
            entry.SetActive(false);
        }

        definition.Configure(entry, modeChoice);
        entry.SetActive(true);
    }

    private static Transform? FindTemplate(Transform modeList, string preferredName)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = modeList.Find(preferredName);
            if (preferred != null && preferred.gameObject.activeSelf)
            {
                return preferred;
            }
        }

        var nativeEntries = FindNativeEntries(modeList, new HashSet<string>(StringComparer.Ordinal));
        foreach (var nativeEntry in nativeEntries)
        {
            if (nativeEntry != null)
            {
                return nativeEntry;
            }
        }

        return string.IsNullOrWhiteSpace(preferredName) ? null : modeList.Find(preferredName);
    }

    private static void AppendRegisteredEntries(
        ModeChoiceUI modeChoice,
        Transform modeList,
        IReadOnlyList<ModeChoiceEntryDefinition> registered,
        HashSet<string> registeredNames)
    {
        var nativeEntries = FindNativeEntries(modeList, registeredNames);
        var customEntries = registered
            .Select(definition => new RegisteredEntryPlacement(definition, modeList.Find(definition.ObjectName) as RectTransform))
            .Where(entry => entry.Rect != null && entry.Rect.gameObject.activeSelf)
            .ToList();
        var shouldLogDiagnostics = ShouldLogDiagnostics();
        if (shouldLogDiagnostics)
        {
            LogModeListDiagnostics(modeList, registeredNames, nativeEntries, customEntries, "before-placement");
        }

        if (nativeEntries.Count == 0)
        {
            HideOldOverlayRoot(modeChoice);
            HideLayoutSlots(modeList);
            DisableLegacyDragSurface(modeChoice);
            ActivateFallbackButtons(modeChoice, customEntries, "no native mode entries found");
            LogPlacementResult(shouldLogDiagnostics, "fallback=no-native; custom=" + string.Join("|", customEntries.Select(entry => entry.Definition.ObjectName)));
            return;
        }

        if (customEntries.Count == 0)
        {
            HideOldOverlayRoot(modeChoice);
            HideLayoutSlots(modeList);
            DisableLegacyDragSurface(modeChoice);
            HideFallbackButtons(modeChoice, registered);
            LogPlacementResult(shouldLogDiagnostics, "skip=no custom entries");
            return;
        }

        if (HasEnabledLayoutGroup(modeList))
        {
            var protectedEntries = FindProtectedNativeEntries(modeList, registeredNames);
            var slotResult = PlaceRegisteredEntriesInLayoutSlots(modeChoice, modeList, protectedEntries, customEntries);
            if (!slotResult.Success)
            {
                ActivateFallbackButtons(modeChoice, customEntries, slotResult.Reason);
                SunExpLog.Warn("[ModeChoiceLayout] slot fallback activated: " + slotResult.Reason);
            }
            else
            {
                HideFallbackButtons(modeChoice, registered);
            }

            LogPlacementResult(shouldLogDiagnostics, slotResult.Message);
            return;
        }

        HideOldOverlayRoot(modeChoice);
        HideLayoutSlots(modeList);
        DisableLegacyDragSurface(modeChoice);
        var result = PlaceAfterNativeEntries(modeChoice, modeList, nativeEntries, customEntries);
        if (!result.Success)
        {
            ActivateFallbackButtons(modeChoice, customEntries, result.Reason);
            SunExpLog.Warn("[ModeChoiceLayout] fallback activated: " + result.Reason);
        }
        else
        {
            HideFallbackButtons(modeChoice, registered);
        }

        LogPlacementResult(shouldLogDiagnostics, result.Message);
    }

    private static List<RectTransform> FindNativeEntries(Transform modeList, HashSet<string> registeredNames)
    {
        var entries = new List<RectTransform>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var knownName in KnownNativeEntryNames)
        {
            var known = modeList.Find(knownName);
            if (known == null || registeredNames.Contains(known.name) || known is not RectTransform knownRect)
            {
                continue;
            }

            if (!known.gameObject.activeSelf)
            {
                SunExpLog.Info("[ModeChoiceLayout] known native entry is inactive: " + knownName);
                continue;
            }

            entries.Add(knownRect);
            seen.Add(known.name);
        }

        foreach (Transform child in modeList)
        {
            if (registeredNames.Contains(child.name)
                || seen.Contains(child.name)
                || IsSyntheticLayoutChild(child.name)
                || !child.gameObject.activeSelf
                || child is not RectTransform rect
                || !LooksLikeModeEntry(child))
            {
                continue;
            }

            entries.Add(rect);
            seen.Add(child.name);
        }

        return entries
            .OrderBy(entry => entry.GetSiblingIndex())
            .ToList();
    }

    private static List<RectTransform> FindProtectedNativeEntries(Transform modeList, HashSet<string> registeredNames)
    {
        var entries = new List<RectTransform>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var knownName in KnownNativeEntryNames)
        {
            var known = modeList.Find(knownName);
            if (known == null || registeredNames.Contains(known.name) || known is not RectTransform knownRect)
            {
                continue;
            }

            entries.Add(knownRect);
            seen.Add(known.name);
        }

        foreach (Transform child in modeList)
        {
            if (registeredNames.Contains(child.name)
                || seen.Contains(child.name)
                || IsSyntheticLayoutChild(child.name)
                || !child.gameObject.activeSelf
                || child is not RectTransform rect
                || !LooksLikeModeEntry(child))
            {
                continue;
            }

            entries.Add(rect);
            seen.Add(child.name);
        }

        return entries
            .OrderBy(entry => entry.GetSiblingIndex())
            .ToList();
    }

    private static bool LooksLikeModeEntry(Transform entry)
    {
        return entry.Find("Text/Text") != null
            && (entry.Find("Normal") != null || entry.Find("HighLighted") != null);
    }

    private static bool HasEnabledLayoutGroup(Transform modeList)
    {
        return modeList.GetComponents<LayoutGroup>().Any(group => group.enabled);
    }

    private static bool IsSyntheticLayoutChild(string name)
    {
        return name.StartsWith(NativeReserveSlotPrefix, StringComparison.Ordinal)
            || name.StartsWith(NativeProxySlotPrefix, StringComparison.Ordinal)
            || name.StartsWith(CustomSlotPrefix, StringComparison.Ordinal);
    }

    private static void PlaceBySiblingOrder(Transform modeList, IReadOnlyList<RectTransform> nativeEntries, IReadOnlyList<RectTransform> customEntries)
    {
        var siblingIndex = nativeEntries.Max(entry => entry.GetSiblingIndex()) + 1;
        foreach (var customEntry in customEntries)
        {
            customEntry.SetSiblingIndex(siblingIndex++);
        }

        if (modeList is RectTransform rect)
        {
            LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }

    private static PlacementResult PlaceRegisteredEntriesInLayoutSlots(
        ModeChoiceUI modeChoice,
        Transform modeList,
        IReadOnlyList<RectTransform> protectedEntries,
        IReadOnlyList<RegisteredEntryPlacement> customEntries)
    {
        if (modeList is not RectTransform modeListRect)
        {
            return PlacementResult.Failed("ModeList is not RectTransform");
        }

        HideOldOverlayRoot(modeChoice);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(modeListRect);

        var slotTemplate = FirstActiveNativeEntry(protectedEntries);
        if (slotTemplate == null)
        {
            return PlacementResult.Failed("no active native entry can provide slot shape");
        }

        var activeNativeEntries = protectedEntries
            .Where(entry => entry.gameObject.activeSelf)
            .OrderBy(entry => entry.GetSiblingIndex())
            .ToList();
        var inactiveKnownEntries = protectedEntries
            .Where(entry => !entry.gameObject.activeSelf && KnownNativeEntryNames.Contains(entry.name, StringComparer.Ordinal))
            .OrderBy(entry => entry.GetSiblingIndex())
            .ToList();
        var nativeProxySlots = new List<RectTransform>();
        foreach (var inactiveEntry in inactiveKnownEntries)
        {
            var proxySlot = EnsureNativeProxySlot(modeList, inactiveEntry, slotTemplate);
            proxySlot.SetSiblingIndex(Math.Min(inactiveEntry.GetSiblingIndex() + 1, modeList.childCount - 1));
            nativeProxySlots.Add(proxySlot);
        }
        HideLegacyReserveSlots(modeList);

        var customSlots = new List<EntryBoundsRecord>();
        foreach (var customEntry in customEntries)
        {
            if (customEntry.Rect == null)
            {
                continue;
            }

            var customSlot = EnsureLayoutSlot(
                modeList,
                CustomSlotPrefix + customEntry.Definition.ObjectName,
                slotTemplate,
                active: true);
            customSlot.SetSiblingIndex(modeList.childCount - 1);

            EnsureIgnoredByLayout(customEntry.Rect, ignored: true);
            customEntry.Rect.gameObject.SetActive(true);
            customEntry.Rect.SetAsLastSibling();
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(modeListRect);

        var messages = new List<string>
        {
            "strategy=layout-slot-placeholder",
            "customIgnoreLayout=true",
            "nativeProxySlots=" + string.Join("|", nativeProxySlots.Select(slot => slot.name)),
            "activeNatives=" + string.Join("|", activeNativeEntries.Select(entry => entry.name))
        };

        foreach (var customEntry in customEntries)
        {
            var target = customEntry.Rect;
            var customSlot = modeList.Find(CustomSlotPrefix + customEntry.Definition.ObjectName) as RectTransform;
            if (target == null || customSlot == null)
            {
                return PlacementResult.Failed("custom slot could not be resolved: " + customEntry.Definition.ObjectName);
            }

            AlignRectToSlot(customSlot, target);
            target.gameObject.SetActive(true);

            if (!TryGetLocalBounds(modeListRect, customSlot, out var slotBounds)
                || !TryGetLocalBounds(modeListRect, target, out var placedBounds))
            {
                return PlacementResult.Failed("custom slot has no valid bounds: " + customEntry.Definition.ObjectName);
            }

            customSlots.Add(new EntryBoundsRecord(customSlot, slotBounds));
            messages.Add("customSlot=" + customSlot.name + ":" + slotBounds);
            messages.Add("customBounds=" + target.name + ":" + placedBounds);
        }

        var activeSlotBounds = new List<EntryBoundsRecord>();
        foreach (Transform child in modeList)
        {
            if (!child.gameObject.activeSelf || child is not RectTransform rect)
            {
                continue;
            }

            if (rect.GetComponent<LayoutElement>()?.ignoreLayout == true)
            {
                continue;
            }

            if (TryGetLocalBounds(modeListRect, rect, out var bounds))
            {
                activeSlotBounds.Add(new EntryBoundsRecord(rect, bounds));
            }
        }

        var dragState = ConfigureHorizontalDrag(modeChoice, modeListRect, activeSlotBounds.Select(entry => entry.Bounds).ToList());
        messages.Add(dragState);
        return PlacementResult.Placed(string.Join("; ", messages));
    }

    private static RectTransform? FirstActiveNativeEntry(IEnumerable<RectTransform> entries)
    {
        return entries.FirstOrDefault(entry => entry.gameObject.activeSelf && entry.rect.width > MinBoundsSize);
    }

    private static RectTransform EnsureLayoutSlot(Transform modeList, string slotName, RectTransform template, bool active)
    {
        var slot = modeList.Find(slotName) as RectTransform;
        if (slot == null)
        {
            var slotObject = new GameObject(slotName, typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement));
            slotObject.transform.SetParent(modeList, false);
            slot = (RectTransform)slotObject.transform;
        }

        CopyRectShape(template, slot);
        slot.localScale = template.localScale;
        var canvasGroup = slot.GetComponent<CanvasGroup>() ?? slot.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        var layoutElement = slot.GetComponent<LayoutElement>() ?? slot.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = false;
        layoutElement.preferredWidth = EffectiveWidth(template);
        layoutElement.preferredHeight = template.rect.height > MinBoundsSize ? template.rect.height : Math.Max(MinBoundsSize, template.sizeDelta.y);
        slot.gameObject.SetActive(active);
        return slot;
    }

    private static RectTransform EnsureNativeProxySlot(Transform modeList, RectTransform nativeEntry, RectTransform template)
    {
        var proxyName = NativeProxySlotPrefix + nativeEntry.name;
        var proxy = modeList.Find(proxyName) as RectTransform;
        if (proxy == null)
        {
            var proxyObject = UnityEngine.Object.Instantiate(nativeEntry.gameObject, modeList);
            proxyObject.name = proxyName;
            proxy = (RectTransform)proxyObject.transform;
        }

        CopyRectShape(template, proxy);
        proxy.localScale = template.localScale;
        EnsureIgnoredByLayout(proxy, ignored: false);
        var canvasGroup = proxy.GetComponent<CanvasGroup>() ?? proxy.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        var layoutElement = proxy.GetComponent<LayoutElement>() ?? proxy.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = false;
        layoutElement.preferredWidth = EffectiveWidth(template);
        layoutElement.preferredHeight = template.rect.height > MinBoundsSize ? template.rect.height : Math.Max(MinBoundsSize, template.sizeDelta.y);
        proxy.gameObject.SetActive(true);
        return proxy;
    }

    private static void EnsureIgnoredByLayout(RectTransform rect, bool ignored = true)
    {
        var layoutElement = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = ignored;
    }

    private static void HideOldOverlayRoot(ModeChoiceUI modeChoice)
    {
        var root = modeChoice.transform.Find(OldOverlayRootName);
        if (root != null)
        {
            root.gameObject.SetActive(false);
        }
    }

    private static void DisableLegacyDragSurface(ModeChoiceUI modeChoice)
    {
        var surface = modeChoice.transform.Find(LegacyDragSurfaceName);
        if (surface == null)
        {
            return;
        }

        var image = surface.GetComponent<Image>();
        if (image != null)
        {
            image.raycastTarget = false;
        }

        var canvasGroup = surface.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        surface.gameObject.SetActive(false);
    }

    private static void HideLayoutSlots(Transform modeList)
    {
        foreach (Transform child in modeList)
        {
            if (child.name.StartsWith(NativeReserveSlotPrefix, StringComparison.Ordinal)
                || child.name.StartsWith(NativeProxySlotPrefix, StringComparison.Ordinal)
                || child.name.StartsWith(CustomSlotPrefix, StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static void HideLegacyReserveSlots(Transform modeList)
    {
        foreach (Transform child in modeList)
        {
            if (child.name.StartsWith(NativeReserveSlotPrefix, StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static void AlignRectToSlot(RectTransform slot, RectTransform target)
    {
        CopyRectShape(slot, target);
        target.anchoredPosition = slot.anchoredPosition;
        target.localScale = slot.localScale;
        target.SetAsLastSibling();
    }

    private static string ConfigureHorizontalDrag(
        ModeChoiceUI modeChoice,
        RectTransform modeList,
        IReadOnlyList<LocalBounds> contentBounds)
    {
        if (contentBounds.Count == 0)
        {
            return "dragEnabled=false; reason=no-bounds";
        }

        var viewport = modeList.parent as RectTransform ?? modeChoice.transform as RectTransform;
        var parentViewportWidth = viewport != null && viewport.rect.width > MinBoundsSize
            ? viewport.rect.width
            : Math.Max(modeList.rect.width, 1f);
        var contentMinX = contentBounds.Min(bounds => bounds.MinX);
        var contentMaxX = contentBounds.Max(bounds => bounds.MaxX);
        var contentWidth = contentMaxX - contentMinX;
        var slotCount = Math.Max(1, contentBounds.Count);
        var slotWidth = contentBounds.Average(bounds => bounds.Width);
        var inferredGap = slotCount > 1
            ? Math.Max(0f, (contentWidth - contentBounds.Sum(bounds => bounds.Width)) / (slotCount - 1))
            : 0f;
        var range = ModeChoiceDragRangeService.Calculate(
            contentMinX,
            contentMaxX,
            slotWidth,
            slotCount,
            inferredGap,
            parentViewportWidth,
            VisibleLayoutSlotCount,
            ModeChoiceSidePadding);

        DisableLegacyDragSurface(modeChoice);
        var backgroundSurface = EnsureBackgroundDragSurface(modeChoice, range.DragEnabled);
        var oldModeListDrag = modeList.GetComponent<ModeChoiceHorizontalDrag>();
        if (oldModeListDrag != null)
        {
            oldModeListDrag.enabled = false;
        }

        ConfigureDragComponent(
            modeChoice.gameObject,
            modeList,
            range.MinOffset,
            range.MaxOffset,
            range.DefaultOffset,
            range.DragEnabled);

        return "dragEnabled=" + range.DragEnabled
            + "; dragHost=ModeChoiceUI"
            + "; backgroundSurface=" + backgroundSurface
            + "; contentWidth=" + contentWidth.ToString("0.###")
            + "; viewportWidth=" + range.ViewportWidth.ToString("0.###")
            + "; parentViewportWidth=" + parentViewportWidth.ToString("0.###")
            + "; sidePadding=" + ModeChoiceSidePadding.ToString("0.###")
            + "; defaultOffset=" + range.DefaultOffset.ToString("0.###")
            + "; minOffset=" + range.MinOffset.ToString("0.###")
            + "; maxOffset=" + range.MaxOffset.ToString("0.###")
            + "; anchoredX=" + modeList.anchoredPosition.x.ToString("0.###");
    }

    private static string EnsureBackgroundDragSurface(ModeChoiceUI modeChoice, bool dragEnabled)
    {
        var root = modeChoice.transform as RectTransform;
        if (root == null)
        {
            return "false; reason=no-root";
        }

        var surface = root.Find(BackgroundDragSurfaceName) as RectTransform;
        if (surface == null)
        {
            var surfaceObject = new GameObject(
                BackgroundDragSurfaceName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            surfaceObject.transform.SetParent(root, false);
            surface = (RectTransform)surfaceObject.transform;
        }

        surface.anchorMin = Vector2.zero;
        surface.anchorMax = Vector2.one;
        surface.pivot = new Vector2(0.5f, 0.5f);
        surface.offsetMin = Vector2.zero;
        surface.offsetMax = Vector2.zero;
        surface.localScale = Vector3.one;
        surface.SetAsFirstSibling();

        var image = surface.GetComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = dragEnabled;
        surface.gameObject.SetActive(dragEnabled);
        return dragEnabled + "; sibling=" + surface.GetSiblingIndex() + "; bounds=" + RectSummary(surface);
    }

    private static void ConfigureDragComponent(
        GameObject host,
        RectTransform modeList,
        float minOffset,
        float maxOffset,
        float defaultOffset,
        bool dragEnabled)
    {
        var drag = host.GetComponent<ModeChoiceHorizontalDrag>() ?? host.AddComponent<ModeChoiceHorizontalDrag>();
        drag.Configure(modeList, minOffset, maxOffset, defaultOffset, dragEnabled);
        drag.enabled = dragEnabled;
    }

    private static PlacementResult PlaceAfterNativeEntries(
        ModeChoiceUI modeChoice,
        Transform modeList,
        IReadOnlyList<RectTransform> nativeEntries,
        IReadOnlyList<RegisteredEntryPlacement> customEntries)
    {
        if (modeList is not RectTransform reference)
        {
            return PlacementResult.Failed("ModeList is not RectTransform");
        }

        var nativeBounds = new List<EntryBoundsRecord>();
        foreach (var nativeEntry in nativeEntries)
        {
            if (TryGetLocalBounds(reference, nativeEntry, out var bounds))
            {
                nativeBounds.Add(new EntryBoundsRecord(nativeEntry, bounds));
            }
        }

        if (nativeBounds.Count == 0)
        {
            return PlacementResult.Failed("native entries have no valid local bounds");
        }

        var orderedByLeft = nativeBounds
            .OrderBy(entry => entry.Bounds.MinX)
            .ThenBy(entry => entry.Rect.GetSiblingIndex())
            .ToList();
        var rightmostNative = nativeBounds
            .OrderBy(entry => entry.Bounds.MaxX)
            .ThenBy(entry => entry.Rect.GetSiblingIndex())
            .Last();
        var gap = NativeGap(orderedByLeft);
        var blockers = new List<EntryBoundsRecord>(nativeBounds);
        var nextRightEdge = rightmostNative.Bounds.MaxX;
        var messages = new List<string>
        {
            "rightmostNative=" + rightmostNative.Rect.name,
            "gap=" + gap.ToString("0.###")
        };

        foreach (var customEntry in customEntries)
        {
            var target = customEntry.Rect!;
            CopyRectShape(rightmostNative.Rect, target);
            target.localScale = rightmostNative.Rect.localScale;
            target.SetAsLastSibling();

            if (!TryGetLocalBounds(reference, target, out var initialBounds))
            {
                return PlacementResult.Failed("custom entry has no valid bounds before placement: " + target.name);
            }

            var width = initialBounds.Width > MinBoundsSize ? initialBounds.Width : rightmostNative.Bounds.Width;
            var targetCenterX = nextRightEdge + gap + (width / 2f);
            SetCenterInReference(reference, target, targetCenterX, rightmostNative.Bounds.CenterY);

            if (!TryGetLocalBounds(reference, target, out var placedBounds))
            {
                return PlacementResult.Failed("custom entry has no valid bounds after placement: " + target.name);
            }

            var overlap = blockers.FirstOrDefault(blocker => Intersects(blocker.Bounds, placedBounds, OverlapTolerance));
            if (overlap.Rect != null)
            {
                return PlacementResult.Failed(target.name + " overlaps " + overlap.Rect.name
                    + "; target=" + placedBounds
                    + "; blocker=" + overlap.Bounds);
            }

            blockers.Add(new EntryBoundsRecord(target, placedBounds));
            nextRightEdge = placedBounds.MaxX;
            messages.Add(target.name + "=" + placedBounds);
        }

        var dragState = ConfigureHorizontalDrag(modeChoice, reference, blockers.Select(entry => entry.Bounds).ToList());
        messages.Add(dragState);
        return PlacementResult.Placed(string.Join("; ", messages));
    }

    private static float NativeGap(IReadOnlyList<EntryBoundsRecord> orderedByLeft)
    {
        var gaps = new List<float>();
        for (var i = 1; i < orderedByLeft.Count; i++)
        {
            var gap = orderedByLeft[i].Bounds.MinX - orderedByLeft[i - 1].Bounds.MaxX;
            if (gap > MinBoundsSize)
            {
                gaps.Add(gap);
            }
        }

        if (gaps.Count == 0)
        {
            return FallbackEntryGap;
        }

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    private static float NativeSpacing(IReadOnlyList<RectTransform> orderedByX)
    {
        var spacings = new List<float>();
        for (var i = 1; i < orderedByX.Count; i++)
        {
            var spacing = orderedByX[i].anchoredPosition.x - orderedByX[i - 1].anchoredPosition.x;
            if (spacing > MinBoundsSize)
            {
                spacings.Add(spacing);
            }
        }

        if (spacings.Count == 0)
        {
            return EffectiveWidth(orderedByX[orderedByX.Count - 1]) + FallbackEntryGap;
        }

        spacings.Sort();
        return spacings[spacings.Count / 2];
    }

    private static void CopyRectShape(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.offsetMin = source.offsetMin;
        target.offsetMax = source.offsetMax;
    }

    private static void SetCenterInReference(RectTransform reference, RectTransform target, float centerX, float centerY)
    {
        if (!TryGetLocalBounds(reference, target, out var current))
        {
            return;
        }

        var delta = new Vector2(centerX - current.CenterX, centerY - current.CenterY);
        target.anchoredPosition += delta;
    }

    private static bool TryGetLocalBounds(RectTransform reference, RectTransform rect, out LocalBounds bounds)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        for (var i = 0; i < corners.Length; i++)
        {
            var local = reference.InverseTransformPoint(corners[i]);
            minX = Math.Min(minX, local.x);
            maxX = Math.Max(maxX, local.x);
            minY = Math.Min(minY, local.y);
            maxY = Math.Max(maxY, local.y);
        }

        bounds = new LocalBounds(minX, maxX, minY, maxY);
        return bounds.Width > MinBoundsSize && bounds.Height > MinBoundsSize;
    }

    private static string RectSummary(RectTransform rect)
    {
        return "anchored=" + rect.anchoredPosition
            + ",size=" + rect.rect.width.ToString("0.###") + "x" + rect.rect.height.ToString("0.###")
            + ",offsetMin=" + rect.offsetMin
            + ",offsetMax=" + rect.offsetMax;
    }

    private static bool Intersects(LocalBounds a, LocalBounds b, float tolerance)
    {
        return a.MinX < b.MaxX - tolerance
            && a.MaxX > b.MinX + tolerance
            && a.MinY < b.MaxY - tolerance
            && a.MaxY > b.MinY + tolerance;
    }

    private static float EffectiveWidth(RectTransform rect)
    {
        return rect.rect.width > MinBoundsSize ? rect.rect.width : Math.Max(MinBoundsSize, rect.sizeDelta.x);
    }

    private static void ActivateFallbackButtons(
        ModeChoiceUI modeChoice,
        IReadOnlyList<RegisteredEntryPlacement> entries,
        string reason)
    {
        foreach (var entry in entries)
        {
            if (entry.Rect != null)
            {
                entry.Rect.gameObject.SetActive(false);
            }

            EnsureFallbackButton(modeChoice, entry.Definition, true, reason);
        }
    }

    private static void HideFallbackButtons(ModeChoiceUI modeChoice, IReadOnlyList<ModeChoiceEntryDefinition> entries)
    {
        foreach (var entry in entries)
        {
            EnsureFallbackButton(modeChoice, entry, false, "native placement safe");
        }
    }

    private static void EnsureFallbackButton(ModeChoiceUI modeChoice, ModeChoiceEntryDefinition definition, bool visible, string reason)
    {
        var parent = modeChoice.transform;
        var fallbackName = definition.ObjectName + "_FallbackButton";
        var fallback = parent.Find(fallbackName) as RectTransform;
        if (!visible)
        {
            if (fallback != null)
            {
                fallback.gameObject.SetActive(false);
            }

            return;
        }

        if (definition.Activate == null)
        {
            SunExpLog.Warn("[ModeChoiceLayout] fallback requested but no activate callback is registered for " + definition.ObjectName + ": " + reason);
            return;
        }

        if (fallback == null)
        {
            var fallbackObject = new GameObject(fallbackName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            fallbackObject.transform.SetParent(parent, false);
            fallback = (RectTransform)fallbackObject.transform;
        }

        fallback.anchorMin = new Vector2(1f, 0.5f);
        fallback.anchorMax = new Vector2(1f, 0.5f);
        fallback.pivot = new Vector2(1f, 0.5f);
        fallback.sizeDelta = new Vector2(FallbackButtonWidth, FallbackButtonHeight);
        fallback.anchoredPosition = new Vector2(-36f, 0f);
        fallback.localScale = Vector3.one;
        fallback.SetAsLastSibling();

        var image = fallback.GetComponent<Image>();
        image.color = new Color(0.08f, 0.06f, 0.14f, 0.94f);

        var button = fallback.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(new UnityAction(() => definition.Activate(modeChoice)));

        var label = fallback.Find("Label") as RectTransform;
        if (label == null)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(fallback, false);
            label = (RectTransform)labelObject.transform;
        }

        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(12f, 8f);
        label.offsetMax = new Vector2(-12f, -8f);

        var text = label.GetComponent<Text>();
        text.text = definition.DisplayName;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.97f, 0.88f, 0.62f, 1f);
        text.raycastTarget = false;

        fallback.gameObject.SetActive(true);
        SunExpLog.Info("[ModeChoiceLayout] fallback button active: " + definition.ObjectName + "; reason=" + reason);
    }

    private static bool ShouldLogDiagnostics()
    {
        if (diagnosticLogCount >= DiagnosticLogLimit)
        {
            return false;
        }

        diagnosticLogCount++;
        return true;
    }

    private static void LogModeListDiagnostics(
        Transform modeList,
        HashSet<string> registeredNames,
        IReadOnlyList<RectTransform> nativeEntries,
        IReadOnlyList<RegisteredEntryPlacement> customEntries,
        string phase)
    {
        var builder = new StringBuilder();
        builder.Append("[ModeChoiceLayout] diagnostic phase=").Append(phase)
            .Append("; childCount=").Append(modeList.childCount)
            .Append("; natives=").Append(string.Join("|", nativeEntries.Select(entry => entry.name)))
            .Append("; customs=").Append(string.Join("|", customEntries.Select(entry => entry.Definition.ObjectName)));

        var reference = modeList as RectTransform;
        if (reference != null && TryGetLocalBounds(reference, reference, out var modeListBounds))
        {
            builder.Append("; modeListBounds=").Append(modeListBounds);
        }

        foreach (Transform child in modeList)
        {
            builder.AppendLine();
            builder.Append("  child index=").Append(child.GetSiblingIndex())
                .Append(" name=").Append(child.name)
                .Append(" activeSelf=").Append(child.gameObject.activeSelf)
                .Append(" activeInHierarchy=").Append(child.gameObject.activeInHierarchy)
                .Append(" knownNative=").Append(KnownNativeEntryNames.Contains(child.name, StringComparer.Ordinal))
                .Append(" registered=").Append(registeredNames.Contains(child.name))
                .Append(" looksLikeEntry=").Append(LooksLikeModeEntry(child))
                .Append(" hasText=").Append(child.Find("Text/Text") != null)
                .Append(" hasNormal=").Append(child.Find("Normal") != null)
                .Append(" hasHighlighted=").Append(child.Find("HighLighted") != null);

            if (child is RectTransform rect)
            {
                builder.Append(" anchored=").Append(rect.anchoredPosition)
                    .Append(" sizeDelta=").Append(rect.sizeDelta)
                    .Append(" anchorMin=").Append(rect.anchorMin)
                    .Append(" anchorMax=").Append(rect.anchorMax)
                    .Append(" pivot=").Append(rect.pivot)
                    .Append(" scale=").Append(rect.localScale);
                if (reference != null && TryGetLocalBounds(reference, rect, out var bounds))
                {
                    builder.Append(" bounds=").Append(bounds);
                }
            }
        }

        SunExpLog.Info(builder.ToString());
    }

    private static void LogPlacementResult(bool enabled, string message)
    {
        if (enabled)
        {
            SunExpLog.Info("[ModeChoiceLayout] placement " + message);
        }
    }

    internal static float EntryAppendSpacingForTests(IReadOnlyList<RectTransform> orderedByX)
    {
        return NativeSpacing(orderedByX);
    }

    private sealed class ModeChoiceHorizontalDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform? modeList;
        private RectTransform? coordinateRoot;
        private float minOffset;
        private float maxOffset;
        private float modeListBaseX;
        private float currentOffset;
        private Vector2 startLocalPoint;
        private Vector2 lastLocalPoint;
        private bool dragEnabled;
        private bool dragging;
        private bool thresholdReached;
        private bool configured;

        public void Configure(RectTransform modeListRect, float minOffsetValue, float maxOffsetValue, float defaultOffset, bool enabledValue)
        {
            var targetChanged = !configured || modeList != modeListRect;
            if (targetChanged)
            {
                modeList = modeListRect;
                modeListBaseX = modeListRect.anchoredPosition.x;
                currentOffset = Mathf.Clamp(defaultOffset, minOffsetValue, maxOffsetValue);
                configured = true;
            }

            coordinateRoot = modeListRect.parent as RectTransform ?? modeListRect;
            minOffset = minOffsetValue;
            maxOffset = maxOffsetValue;
            currentOffset = Mathf.Clamp(currentOffset, minOffset, maxOffset);
            dragEnabled = enabledValue;
            dragging = false;
            thresholdReached = false;
            ApplyOffset(currentOffset);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!dragEnabled || coordinateRoot == null)
            {
                return;
            }

            dragging = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                coordinateRoot,
                eventData.position,
                eventData.pressEventCamera,
                out lastLocalPoint);
            startLocalPoint = lastLocalPoint;
            thresholdReached = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || !dragEnabled || coordinateRoot == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    coordinateRoot,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var currentLocalPoint))
            {
                return;
            }

            if (!thresholdReached && Math.Abs(currentLocalPoint.x - startLocalPoint.x) < DragStartThreshold)
            {
                return;
            }

            thresholdReached = true;
            ApplyOffset(currentOffset + currentLocalPoint.x - lastLocalPoint.x);
            lastLocalPoint = currentLocalPoint;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
        }

        private void ApplyOffset(float offset)
        {
            if (modeList == null)
            {
                return;
            }

            currentOffset = Mathf.Clamp(offset, minOffset, maxOffset);
            modeList.anchoredPosition = new Vector2(modeListBaseX + currentOffset, modeList.anchoredPosition.y);
        }
    }

    private readonly struct RegisteredEntryPlacement
    {
        public RegisteredEntryPlacement(ModeChoiceEntryDefinition definition, RectTransform? rect)
        {
            Definition = definition;
            Rect = rect;
        }

        public ModeChoiceEntryDefinition Definition { get; }

        public RectTransform? Rect { get; }
    }

    private readonly struct EntryBoundsRecord
    {
        public EntryBoundsRecord(RectTransform rect, LocalBounds bounds)
        {
            Rect = rect;
            Bounds = bounds;
        }

        public RectTransform Rect { get; }

        public LocalBounds Bounds { get; }
    }

    private readonly struct LocalBounds
    {
        public LocalBounds(float minX, float maxX, float minY, float maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public float MinX { get; }

        public float MaxX { get; }

        public float MinY { get; }

        public float MaxY { get; }

        public float Width => MaxX - MinX;

        public float Height => MaxY - MinY;

        public float CenterX => (MinX + MaxX) / 2f;

        public float CenterY => (MinY + MaxY) / 2f;

        public override string ToString()
        {
            return "x=[" + MinX.ToString("0.###") + "," + MaxX.ToString("0.###")
                + "],y=[" + MinY.ToString("0.###") + "," + MaxY.ToString("0.###")
                + "],w=" + Width.ToString("0.###")
                + ",h=" + Height.ToString("0.###");
        }
    }

    private readonly struct PlacementResult
    {
        private PlacementResult(bool success, string reason, string message)
        {
            Success = success;
            Reason = reason;
            Message = message;
        }

        public bool Success { get; }

        public string Reason { get; }

        public string Message { get; }

        public static PlacementResult Placed(string message)
        {
            return new PlacementResult(true, "", "placed; " + message);
        }

        public static PlacementResult Failed(string reason)
        {
            return new PlacementResult(false, reason, "fallback; reason=" + reason);
        }
    }
}
