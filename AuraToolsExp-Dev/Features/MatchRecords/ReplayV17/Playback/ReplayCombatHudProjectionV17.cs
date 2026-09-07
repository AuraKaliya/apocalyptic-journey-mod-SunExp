using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.GameApi;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

/// <summary>
/// A sanitized, screen-space instantiation of the native status, HP, Buff, and
/// action prefabs. Gameplay behaviours remain disabled while the complete
/// renderer/material/layout hierarchy is retained.
/// </summary>
internal sealed class ReplayCombatHudProjectionV17 : IDisposable
{
    private readonly Camera camera;
    private readonly RectTransform canvasRect;
    private readonly Vector2 referenceResolution;
    private readonly GameObject statusRoot;
    private readonly GameObject intentRoot;
    private readonly SpriteRenderer hpFill;
    private readonly SpriteRenderer healthDelayFill;
    private readonly SpriteRenderer shieldFill;
    private readonly TMP_Text hpText;
    private readonly TMP_Text shieldText;
    private readonly GameObject shieldCounter;
    private readonly Vector2 hpTextSize;
    private readonly Vector2 shieldTextSize;
    private readonly float intentSpacing;
    private readonly RectTransform buffContent;
    private readonly RectTransform intentContent;
    private readonly RectTransform intentRect;
    private readonly IReadOnlyDictionary<string, ReplayBuffDescriptorV17> buffDescriptors;
    private readonly IReadOnlyDictionary<string, ReplayIntentDescriptorV17> intentDescriptors;
    private readonly ReplayUiTemplateCacheV17 templates;
    private readonly List<BuffSlot> buffSlots = new();
    private readonly List<IntentSlot> intentSlots = new();
    private readonly ReplayCustomEntityPresentationV17? customPresentation;
    private readonly bool detachedHud;
    private bool present = true;
    private bool intentsVisible;
    private bool visible = true;
    private ExtensionIntent? extensionIntent;

    internal ReplayCombatHudProjectionV17(
        Transform canvasParent,
        Camera camera,
        Vector2 referenceResolution,
        ReplayEntityDescriptorV17 entity,
        ReplayEntityPresentationBindingV17 binding,
        IReadOnlyDictionary<string, ReplayBuffDescriptorV17> buffDescriptors,
        IReadOnlyDictionary<string, ReplayIntentDescriptorV17> intentDescriptors,
        ReplayUiTemplateCacheV17 templates)
    {
        this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        canvasRect = canvasParent as RectTransform
                     ?? throw new InvalidOperationException("Replay capture canvas has no RectTransform.");
        this.referenceResolution = referenceResolution;
        this.buffDescriptors = buffDescriptors;
        this.intentDescriptors = intentDescriptors;
        this.templates = templates;
        customPresentation = binding.CustomPresentation;
        detachedHud = customPresentation != null
                      && string.Equals(customPresentation.HudMode, "DetachedRightVertical", StringComparison.Ordinal);

        var size = new Vector2(
            ReplayPresentationPrimitivesV17.FromQ16(binding.StatusBarSize.X),
            ReplayPresentationPrimitivesV17.FromQ16(binding.StatusBarSize.Y));
        statusRoot = ReplayNativePrefabInstanceV17.Clone(
            templates.StatusTemplate,
            canvasParent,
            "ReplayStatus:" + binding.EntityId);
        var statusRect = statusRoot.GetComponent<RectTransform>()
                         ?? throw new InvalidOperationException("Native replay StatusBarUI has no RectTransform.");
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.sizeDelta = size;
        statusRoot.transform.localScale = Vector3.one * ReplayPresentationPrimitivesV17.FromQ16(binding.HudScaleQ16);

        var selectedName = statusRoot.transform.Find("Name/Selected/Name")?.GetComponent<TMP_Text>();
        var unselectedName = statusRoot.transform.Find("Name/UnSelected/Name")?.GetComponent<TMP_Text>();
        if (selectedName == null && unselectedName == null)
            throw new InvalidOperationException("Native replay StatusBarUI has no name text.");
        if (selectedName != null) selectedName.text = entity.Name ?? "";
        if (unselectedName != null) unselectedName.text = entity.Name ?? "";
        statusRoot.transform.Find("Name/Selected")?.gameObject.SetActive(false);
        statusRoot.transform.Find("Name/UnSelected")?.gameObject.SetActive(true);
        var hpItem = ReplayNativePrefabInstanceV17.Clone(templates.HpTemplate, statusRoot.transform, "NativeHpItem");
        hpFill = hpItem.transform.Find("fill")?.GetComponent<SpriteRenderer>()
                 ?? throw new InvalidOperationException("Native replay HpItem has no fill SpriteRenderer.");
        healthDelayFill = hpItem.transform.Find("redfill")?.GetComponent<SpriteRenderer>()
                          ?? throw new InvalidOperationException("Native replay HpItem has no redfill SpriteRenderer.");
        shieldFill = hpItem.transform.Find("bluefill")?.GetComponent<SpriteRenderer>()
                     ?? throw new InvalidOperationException("Native replay HpItem has no bluefill SpriteRenderer.");
        hpFill.material = new Material(hpFill.sharedMaterial);
        healthDelayFill.material = new Material(healthDelayFill.sharedMaterial);
        shieldFill.material = new Material(shieldFill.sharedMaterial);
        hpText = hpItem.transform.Find("hpTxt")?.GetComponent<TMP_Text>()
                 ?? ReplayUiV17.Tmp(statusRoot.transform, "HpText", templates.Font, 18f, TextAlignmentOptions.Center);
        shieldText = hpItem.transform.Find("DefendShow/val")?.GetComponent<TMP_Text>()
                     ?? ReplayUiV17.Tmp(statusRoot.transform, "ShieldText", templates.Font, 14f, TextAlignmentOptions.Center);
        shieldCounter = hpItem.transform.Find("DefendShow")?.gameObject
                        ?? throw new InvalidOperationException("Native replay HpItem has no defense counter.");
        hpTextSize = hpText.rectTransform.sizeDelta;
        shieldTextSize = shieldText.rectTransform.sizeDelta;

        var buffBar = ReplayNativePrefabInstanceV17.Clone(
            templates.BuffBarTemplate,
            statusRoot.transform,
            "NativeBuffBar");
        buffContent = buffBar.transform.Find("Content") as RectTransform
                      ?? throw new InvalidOperationException("Native replay BuffBarUI has no Content RectTransform.");
        if (customPresentation != null)
        {
            statusRoot.transform.localScale *= ReplayPresentationPrimitivesV17.FromQ16(customPresentation.HudScaleQ16);
            var rotation = new Vector3(
                0f, 0f, ReplayPresentationPrimitivesV17.FromQ16(customPresentation.HudRotationQ16));
            hpItem.transform.localEulerAngles += rotation;
            hpText.transform.localEulerAngles -= rotation;
            shieldText.transform.localEulerAngles -= rotation;
            if (detachedHud)
            {
                statusRoot.transform.Find("Name")?.gameObject.SetActive(false);
                buffBar.SetActive(false);
                ConfigureVerticalText(hpText);
                ConfigureVerticalText(shieldText);
            }
            if (!detachedHud && !string.IsNullOrWhiteSpace(customPresentation.BadgeIconResourcePath))
            {
                var badge = ReplayUiV17.Rect(
                    "PresentationBadge", statusRoot.transform,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(42f, 18f));
                badge.GetComponent<RectTransform>().anchoredPosition = new Vector2(8f, 22f);
                var badgeImage = badge.AddComponent<Image>();
                badgeImage.sprite = ReplayResourceResolverV17.RequiredSprite(
                    customPresentation.BadgeIconResourcePath,
                    "entity-presentation-badge:" + binding.EntityId);
                badgeImage.preserveAspect = true;
                badgeImage.raycastTarget = false;
            }
        }

        intentRoot = ReplayNativePrefabInstanceV17.Clone(
            templates.ActionContentTemplate,
            canvasParent,
            "ReplayIntents:" + binding.EntityId);
        intentRoot.transform.localScale = Vector3.one * ReplayPresentationPrimitivesV17.FromQ16(binding.HudScaleQ16);
        // The v1 detached presentation uses compact intents independently of
        // the vertical HP bar's declared scale.
        if (detachedHud) intentRoot.transform.localScale *= 0.60f;
        intentContent = intentRoot.transform.Find("content") as RectTransform
                        ?? throw new InvalidOperationException("Native replay ActionContent has no content RectTransform.");
        intentRect = intentRoot.GetComponent<RectTransform>()
                     ?? throw new InvalidOperationException("Native replay ActionContent has no root RectTransform.");
        intentSpacing = entity.Archetype == ReplayEntityArchetypesV17.EnemyCombatant ? 0f : 40f;
        intentRoot.SetActive(false);
    }

    internal void Apply(ReplayEntityStateV17 value, IReadOnlyList<ReplayIntentStateV17> intents)
    {
        present = value.IsPresent;
        statusRoot.SetActive(present && visible);
        if (!present)
        {
            intentRoot.SetActive(false);
            return;
        }

        var maximum = Math.Max(1, value.MaxHp);
        var hpRatio = Mathf.Clamp01(value.CurrentHp / (float)maximum);
        SetMaterialFill(hpFill, hpRatio);
        SetMaterialFill(healthDelayFill, hpRatio);
        SetMaterialFill(shieldFill, Mathf.Clamp01(value.Defense / (float)maximum));
        hpText.text = detachedHud
            ? VerticalDigits(Math.Max(0, value.CurrentHp))
            : Math.Max(0, value.CurrentHp).ToString();
        hpText.color = value.IsAlive ? Color.white : new Color(0.65f, 0.65f, 0.68f, 1f);
        if (detachedHud)
        {
            ResizeVerticalText(hpText, hpTextSize, Math.Max(0, value.CurrentHp));
            shieldText.text = VerticalDigits(value.Defense);
            ResizeVerticalText(shieldText, shieldTextSize, Math.Max(0, value.Defense));
        }
        else ReplayNativeUiPresentationApi.SetDigitText(
            shieldText, Math.Max(0, value.Defense).ToString());
        shieldCounter.SetActive(true);
        shieldCounter.transform.Find("Large")?.gameObject.SetActive(value.Defense >= 100);
        shieldCounter.transform.Find("Small")?.gameObject.SetActive(value.Defense < 100);
        shieldFill.gameObject.SetActive(value.Defense > 0);
        shieldText.gameObject.SetActive(true);
        hpText.ForceMeshUpdate();

        var buffs = value.Buffs
            .Select(item => (State: item, Descriptor: Descriptor(item.DescriptorId, buffDescriptors)))
            .OrderBy(item => item.Descriptor.SortOrder)
            .ThenBy(item => item.Descriptor.DescriptorId, StringComparer.Ordinal)
            .ThenBy(item => item.State.InstanceId, StringComparer.Ordinal)
            .ToList();
        EnsureBuffSlots(buffs.Count);
        for (var index = 0; index < buffSlots.Count; index++)
        {
            var active = index < buffs.Count;
            buffSlots[index].Root.SetActive(active);
            if (!active) continue;
            var pair = buffs[index];
            buffSlots[index].Icon.sprite = ReplayResourceResolverV17.RequiredSprite(
                pair.Descriptor.IconResourcePath,
                "buff:" + pair.Descriptor.DescriptorId);
            ReplayNativeUiPresentationApi.SetDigitText(
                buffSlots[index].Level,
                pair.State.Level > 0 ? pair.State.Level.ToString() : "");
            buffSlots[index].Root.transform.SetSiblingIndex(index);
            var sorting = buffSlots[index].Root.GetComponent<SortingGroup>();
            if (sorting != null) sorting.sortingOrder = -index;
        }

        var orderedIntents = (intents ?? Array.Empty<ReplayIntentStateV17>())
            .OrderBy(item => item.SlotIndex)
            .ThenBy(item => item.IntentInstanceId, StringComparer.Ordinal)
            .Take(4)
            .ToList();
        EnsureIntentSlots(orderedIntents.Count);
        for (var index = 0; index < intentSlots.Count; index++)
        {
            var active = index < orderedIntents.Count;
            intentSlots[index].Root.SetActive(active);
            if (!active) continue;
            var state = orderedIntents[index];
            var descriptor = Descriptor(state.DescriptorId, intentDescriptors);
            intentSlots[index].Background.sprite = ReplayResourceResolverV17.RequiredSprite(
                descriptor.BackIconResourcePath,
                "intent-background:" + descriptor.DescriptorId);
            intentSlots[index].Icon.sprite = ReplayResourceResolverV17.RequiredSprite(
                descriptor.IconResourcePath,
                "intent-icon:" + descriptor.DescriptorId);
            intentSlots[index].Icon.SetNativeSize();
            ReplayNativeUiPresentationApi.SetDigitText(intentSlots[index].Value, state.DisplayValue ?? "");
        }
        LayoutIntents(orderedIntents.Count);
        intentsVisible = orderedIntents.Count > 0;
        if (!intentsVisible && extensionIntent != null)
        {
            ApplyExtensionIntentSlot();
            intentsVisible = true;
        }
        intentRoot.SetActive(intentsVisible && visible);
    }

    internal void PresentExtensionIntent(ReplayExtensionIntentVisualV17 visual)
    {
        if (visual.IsWait)
        {
            ClearExtensionIntent();
            return;
        }
        extensionIntent = new ExtensionIntent(
            visual.IconResourcePath,
            visual.BackgroundResourcePath,
            visual.DisplayValue);
        ApplyExtensionIntentSlot();
        intentsVisible = true;
        intentRoot.SetActive(present && visible);
    }

    internal void ClearExtensionIntent()
    {
        extensionIntent = null;
        if (intentSlots.Count > 0) intentSlots[0].Root.SetActive(false);
        intentsVisible = false;
        intentRoot.SetActive(false);
    }

    internal void UpdateWorldAnchors(
        Vector3 bottomWorldPosition,
        Vector3 headWorldPosition,
        Bounds bodyBounds)
    {
        if (!present || !visible) return;
        var detached = customPresentation != null
                       && string.Equals(customPresentation.HudMode, "DetachedRightVertical", StringComparison.Ordinal);
        var statusPoint = detached
            ? new Vector3(bodyBounds.max.x, bodyBounds.center.y, bodyBounds.center.z)
            : bottomWorldPosition;
        Project(
            statusRoot.GetComponent<RectTransform>(),
            statusPoint,
            detached ? new Vector2(14f, 0f) : Vector2.zero);
        if (intentsVisible) Project(intentRect,
            detached ? new Vector3(bodyBounds.center.x, bodyBounds.max.y, bodyBounds.center.z) : headWorldPosition,
            detached ? new Vector2(0f, 14f) : Vector2.zero);
        else intentRoot.SetActive(false);
    }

    public void Dispose()
    {
        if (hpFill != null && hpFill.material != null) Object.Destroy(hpFill.material);
        if (healthDelayFill != null && healthDelayFill.material != null) Object.Destroy(healthDelayFill.material);
        if (shieldFill != null && shieldFill.material != null) Object.Destroy(shieldFill.material);
        if (statusRoot != null) Object.Destroy(statusRoot);
        if (intentRoot != null) Object.Destroy(intentRoot);
    }

    internal void SetVisible(bool value)
    {
        visible = value;
        statusRoot.SetActive(present && visible);
        intentRoot.SetActive(present && visible && intentsVisible);
    }

    private void EnsureBuffSlots(int count)
    {
        while (buffSlots.Count < count)
        {
            var index = buffSlots.Count;
            var root = ReplayNativePrefabInstanceV17.Clone(
                templates.BuffTemplate,
                buffContent,
                "ReplayBuff:" + index);
            var icon = root.transform.Find("Content/Image")?.GetComponent<SpriteRenderer>()
                       ?? throw new InvalidOperationException("Native replay BuffItem has no Content/Image SpriteRenderer.");
            var nativeMaterial = AuraToolsResourceCache.Load<Material>("Material/BuffIcon", true)
                                 ?? AuraToolsResourceCache.Load<Material>("Material/BuffIcon", false);
            if (nativeMaterial != null) icon.sharedMaterial = nativeMaterial;
            var level = root.transform.Find("Content/Level")?.GetComponent<TMP_Text>()
                        ?? throw new InvalidOperationException("Native replay BuffItem has no Content/Level text.");
            buffSlots.Add(new BuffSlot(root, icon, level));
        }
    }

    private void EnsureIntentSlots(int count)
    {
        while (intentSlots.Count < count)
        {
            var index = intentSlots.Count;
            var root = ReplayNativePrefabInstanceV17.Clone(
                templates.ActionTemplate,
                intentContent,
                "ReplayIntent:" + index);
            var background = root.transform.Find("Icon")?.GetComponent<Image>()
                             ?? throw new InvalidOperationException("Native replay ActionMsg has no Icon background.");
            var icon = root.transform.Find("Icon/child")?.GetComponent<Image>()
                       ?? throw new InvalidOperationException("Native replay ActionMsg has no Icon/child image.");
            var value = root.transform.Find("Icon/val")?.GetComponent<TMP_Text>()
                        ?? throw new InvalidOperationException("Native replay ActionMsg has no Icon/val text.");
            // This is the native intent-consumption animation, not its idle icon.
            // Playback state controls visibility; an authored Animator cannot run
            // this effect independently of the recorded action clock.
            root.transform.Find("Icon/DesAni")?.gameObject.SetActive(false);
            intentSlots.Add(new IntentSlot(root, background, icon, value));
        }
    }

    private void ApplyExtensionIntentSlot()
    {
        if (extensionIntent == null) return;
        EnsureIntentSlots(1);
        for (var index = 0; index < intentSlots.Count; index++)
            intentSlots[index].Root.SetActive(index == 0);
        var slot = intentSlots[0];
        slot.Background.sprite = ReplayResourceResolverV17.RequiredSprite(
            extensionIntent.BackgroundResourcePath,
            "extension-intent-background");
        slot.Icon.sprite = ReplayResourceResolverV17.RequiredSprite(
            extensionIntent.IconResourcePath,
            "extension-intent-icon");
        slot.Icon.SetNativeSize();
        ReplayNativeUiPresentationApi.SetDigitText(slot.Value, extensionIntent.DisplayValue);
        LayoutIntents(1);
    }

    private void LayoutIntents(int count)
    {
        var width = 0f;
        for (var index = 0; index < count; index++)
            width += intentSlots[index].Root.GetComponent<RectTransform>().sizeDelta.x + intentSpacing;
        var cursor = -width * 0.5f;
        for (var index = 0; index < count; index++)
        {
            var rect = intentSlots[index].Root.GetComponent<RectTransform>();
            var advance = rect.sizeDelta.x + intentSpacing;
            rect.anchoredPosition = new Vector2(cursor + advance * 0.5f, 0f);
            cursor += advance;
        }
    }

    private static void ConfigureVerticalText(TMP_Text text)
    {
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.enableAutoSizing = false;
        text.lineSpacing = -10f;
    }

    private static void ResizeVerticalText(TMP_Text text, Vector2 originalSize, int value)
    {
        var lines = Math.Max(1, Math.Max(0, value).ToString().Length);
        var lineHeight = originalSize.y > 0.01f ? originalSize.y : Mathf.Max(1f, text.fontSize * 1.15f);
        text.rectTransform.sizeDelta = new Vector2(originalSize.x, lineHeight * lines);
        text.ForceMeshUpdate();
    }

    private void Project(RectTransform target, Vector3 worldPosition, Vector2 offset)
    {
        var viewport = camera.WorldToViewportPoint(worldPosition);
        target.gameObject.SetActive(viewport.z > 0f && present);
        if (viewport.z <= 0f) return;
        var size = canvasRect.rect.size;
        if (size.x <= 0f || size.y <= 0f) size = referenceResolution;
        target.anchoredPosition = new Vector2(
            (viewport.x - 0.5f) * size.x,
            (viewport.y - 0.5f) * size.y) + offset;
    }

    private static void SetMaterialFill(SpriteRenderer target, float ratio)
    {
        if (target == null || target.material == null) return;
        if (target.material.HasProperty("_FillAmount")) target.material.SetFloat("_FillAmount", ratio);
        target.enabled = ratio > 0f;
    }

    private static string VerticalDigits(int value) => string.Join("\n", Math.Max(0, value).ToString().ToCharArray());

    private static T Descriptor<T>(string id, IReadOnlyDictionary<string, T> values) where T : class =>
        values.TryGetValue(id ?? "", out var value)
            ? value
            : throw new InvalidOperationException("Replay UI descriptor is missing: " + id);

    private sealed class BuffSlot
    {
        internal BuffSlot(GameObject root, SpriteRenderer icon, TMP_Text level)
        {
            Root = root;
            Icon = icon;
            Level = level;
        }

        internal GameObject Root { get; }
        internal SpriteRenderer Icon { get; }
        internal TMP_Text Level { get; }
    }

    private sealed class IntentSlot
    {
        internal IntentSlot(GameObject root, Image background, Image icon, TMP_Text value)
        {
            Root = root;
            Background = background;
            Icon = icon;
            Value = value;
        }

        internal GameObject Root { get; }
        internal Image Background { get; }
        internal Image Icon { get; }
        internal TMP_Text Value { get; }
    }

    private sealed class ExtensionIntent
    {
        internal ExtensionIntent(string iconResourcePath, string backgroundResourcePath, string displayValue)
        {
            IconResourcePath = iconResourcePath;
            BackgroundResourcePath = backgroundResourcePath;
            DisplayValue = displayValue;
        }

        internal string IconResourcePath { get; }
        internal string BackgroundResourcePath { get; }
        internal string DisplayValue { get; }
    }
}

internal sealed class ReplayUiTemplateCacheV17
{
    internal ReplayUiTemplateCacheV17(ReplayUiTemplateDescriptorV17 descriptor)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        FightUiTemplate = RequiredPrefab(descriptor.FightUiResourcePath);
        var status = RequiredPrefab(descriptor.StatusBarResourcePath);
        var hp = RequiredPrefab(descriptor.HpItemResourcePath);
        var buffBar = RequiredPrefab(descriptor.BuffBarResourcePath);
        var buff = RequiredPrefab(descriptor.BuffItemResourcePath);
        var actionContent = RequiredPrefab(descriptor.ActionContentResourcePath);
        var action = RequiredPrefab(descriptor.ActionItemResourcePath);
        CardTemplate = RequiredPrefab(descriptor.CardItemResourcePath);
        StatusTemplate = status;
        HpTemplate = hp;
        BuffBarTemplate = buffBar;
        BuffTemplate = buff;
        ActionContentTemplate = actionContent;
        ActionTemplate = action;
        Font = new[] { status, hp, buffBar, buff, actionContent, action }
                   .SelectMany(item => item.GetComponentsInChildren<TMP_Text>(true))
                   .Select(item => item?.font)
                   .FirstOrDefault(item => item != null)
               ?? throw new InvalidOperationException("Replay native UI templates have no readable TMP font.");
        StatusBackground = FindSprite(status, "background", "name");
        HpBackground = FindSprite(hp, "background", "redfill");
        HpFill = FindSprite(hp, "fill");
        ShieldFill = FindSprite(hp, "bluefill");
        BuffFrame = FindSprite(buff, "background", "content");
    }

    internal TMP_FontAsset Font { get; }
    internal GameObject FightUiTemplate { get; }
    internal GameObject StatusTemplate { get; }
    internal GameObject HpTemplate { get; }
    internal GameObject BuffBarTemplate { get; }
    internal GameObject BuffTemplate { get; }
    internal GameObject ActionContentTemplate { get; }
    internal GameObject ActionTemplate { get; }
    internal GameObject CardTemplate { get; }
    internal Sprite? StatusBackground { get; }
    internal Sprite? HpBackground { get; }
    internal Sprite? HpFill { get; }
    internal Sprite? ShieldFill { get; }
    internal Sprite? BuffFrame { get; }

    private static GameObject RequiredPrefab(string path) =>
        AuraToolsResourceCache.Load<GameObject>(path, true)
        ?? AuraToolsResourceCache.Load<GameObject>(path, false)
        ?? throw new InvalidOperationException("Replay native UI template is missing: " + path);

    private static Sprite? FindSprite(GameObject root, params string[] preferredNames)
    {
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(item => item?.sprite != null)
            .Select(item => (item.name, item.sprite));
        var images = root.GetComponentsInChildren<Image>(true)
            .Where(item => item?.sprite != null)
            .Select(item => (item.name, item.sprite));
        var candidates = renderers.Concat(images).ToList();
        foreach (var preferred in preferredNames)
        {
            var match = candidates.FirstOrDefault(item =>
                string.Equals(item.name, preferred, StringComparison.OrdinalIgnoreCase));
            if (match.sprite != null) return match.sprite;
            match = candidates.FirstOrDefault(item =>
                item.name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match.sprite != null) return match.sprite;
        }
        return candidates.Select(item => item.sprite).FirstOrDefault(item => item != null);
    }
}
