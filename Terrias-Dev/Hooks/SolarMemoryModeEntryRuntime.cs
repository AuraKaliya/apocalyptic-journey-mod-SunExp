using System;
using System.Linq;
using System.Reflection;
using AuraUi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryModeEntryRuntime
{
    private const string EntryObjectName = "Terrias_SolarMemoryMode";
    private const string DefaultEntryTitleSpritePath = "Mods/Terrias/ModResource/Images/UI/solar_memory_title_c.png";
    private const string DefaultEntryHighlightedTitleSpritePath = "Mods/Terrias/ModResource/Images/UI/solar_memory_title_c_h.png";
    private const float DefaultEntryTitleArtHeightRatio = 0.735f;

    private static Font? cachedFont;
    private static Sprite? entryTitleSprite;
    private static Sprite? entryHighlightedTitleSprite;
    private static string entryTitleSpritePath = "";
    private static string entryHighlightedTitleSpritePath = "";
    private static bool entryTitleSpriteLoadAttempted;
    private static bool entryHighlightedTitleSpriteLoadAttempted;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterModeChoiceEntry();
        ModeChoiceLayoutRuntime.Initialize(modConfig);
    }

    private static void RegisterModeChoiceEntry()
    {
        ModeChoiceEntryRegistry.Register(new ModeChoiceEntryDefinition(
            EntryObjectName,
            "SublimationMode",
            100,
            ConfigureRegisteredEntry,
            modeChoice => SolarMemoryRunLauncher.Start(modeChoice, SolarMemoryDeckIsolationRuntime.InitialPackSelection().ToList()),
            TerriasIds.SolarMemoryTitle,
            TerriasIds.SolarMemorySemanticModeId));
    }

    private static void ConfigureRegisteredEntry(GameObject entry, ModeChoiceUI modeChoice)
    {
        try
        {
            ConfigureEntryUnlocked(entry.transform);
            ConfigureEntryHoverState(entry);
            ConfigureEntryTexts(entry.transform);
            ResetEntryVisualState(entry);
            ConfigureEntryClick(entry, modeChoice);
            entry.SetActive(true);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory entry injection failed", ex);
        }
    }

    private static void ConfigureEntryUnlocked(Transform entry)
    {
        foreach (var child in entry.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, "Lock", StringComparison.OrdinalIgnoreCase))
            {
                child.gameObject.SetActive(false);
            }
        }

        var switchButton = entry.GetComponent<SwitchButton>();
        if (switchButton != null)
        {
            switchButton.interactable = true;
        }

        foreach (var selectable in entry.GetComponentsInChildren<Selectable>(true))
        {
            selectable.interactable = true;
        }
    }

    private static void ConfigureEntryTexts(Transform entry)
    {
        SetTmpText(entry.Find("Text/Text"), TerriasIds.SolarMemoryDescription + "\n" + TerriasIds.SolarMemorySubtitle);
        var hasTitleSprites = ConfigureEntryTitleSprites(entry);

        var title = entry.Find("TerriasTitle");
        if (hasTitleSprites)
        {
            if (title != null)
            {
                title.gameObject.SetActive(false);
            }

            return;
        }

        if (title == null)
        {
            var go = new GameObject("TerriasTitle", typeof(RectTransform));
            title = go.transform;
            title.SetParent(entry, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 0.58f);
            rect.anchorMax = new Vector2(0.92f, 0.88f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.AddComponent<Text>();
            ConfigureText(text, TerriasIds.SolarMemoryTitle, 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        }
        else
        {
            var text = title.GetComponent<Text>();
            if (text != null)
            {
                text.text = TerriasIds.SolarMemoryTitle;
            }

            title.gameObject.SetActive(true);
        }
    }

    private static bool ConfigureEntryTitleSprites(Transform entry)
    {
        var normalSprite = GetEntryTitleSprite();
        var highlightedSprite = GetEntryHighlightedTitleSprite();
        if (normalSprite == null || highlightedSprite == null)
        {
            return false;
        }

        var normalTitle = entry.Find("Normal/Title");
        var highlightedTitle = entry.Find("HighLighted/Title");
        var pressedTitle = entry.Find("Pressed/Title");
        ClearEntryStateImages(entry.Find("Normal"), normalTitle);
        ClearEntryStateImages(entry.Find("HighLighted"), highlightedTitle);
        ClearEntryStateImages(entry.Find("Pressed"), pressedTitle);
        SetImageSprite(normalTitle, normalSprite);
        SetImageSprite(highlightedTitle, highlightedSprite);
        SetImageSprite(pressedTitle, highlightedSprite);
        return true;
    }

    private static Sprite? GetEntryTitleSprite()
    {
        var path = VisualRegistry.ModeEntry("solar_memory")?.NormalTitleSprite;
        return GetCachedEntrySprite(ref entryTitleSprite, ref entryTitleSpritePath, ref entryTitleSpriteLoadAttempted, path, DefaultEntryTitleSpritePath);
    }

    private static Sprite? GetEntryHighlightedTitleSprite()
    {
        var path = VisualRegistry.ModeEntry("solar_memory")?.HighlightedTitleSprite;
        return GetCachedEntrySprite(
            ref entryHighlightedTitleSprite,
            ref entryHighlightedTitleSpritePath,
            ref entryHighlightedTitleSpriteLoadAttempted,
            path,
            DefaultEntryHighlightedTitleSpritePath);
    }

    private static Sprite? GetCachedEntrySprite(ref Sprite? cached, ref string cachedPath, ref bool attempted, string? path, string fallbackPath)
    {
        var candidatePath = path ?? "";
        var resolvedPath = string.IsNullOrWhiteSpace(candidatePath) ? fallbackPath : candidatePath.Trim();
        if (!string.Equals(cachedPath, resolvedPath, StringComparison.Ordinal))
        {
            cached = null;
            cachedPath = resolvedPath;
            attempted = false;
        }

        if (cached != null)
        {
            return cached;
        }

        if (attempted)
        {
            return null;
        }

        attempted = true;
        cached = LoadEntrySprite(resolvedPath);
        return cached;
    }

    private static Sprite? LoadEntrySprite(string path)
    {
        try
        {
            var sprite = TerriasResourceCache.Load<Sprite>(path, true);
            if (sprite == null)
            {
                TerriasLog.Warn("[SolarMemoryMode] entry sprite missing: " + path);
                return null;
            }

            var trimmed = TrimTransparentPadding(sprite) ?? sprite;
            return CropEntryTitleArt(trimmed) ?? trimmed;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SolarMemoryMode] failed to load entry sprite " + path + ": " + ex.Message);
            return null;
        }
    }

    private static Sprite? TrimTransparentPadding(Sprite sprite)
    {
        try
        {
            var texture = sprite.texture;
            var rect = sprite.rect;
            var minX = (int)rect.xMax;
            var minY = (int)rect.yMax;
            var maxX = (int)rect.xMin - 1;
            var maxY = (int)rect.yMin - 1;
            var startX = Mathf.Max(0, Mathf.FloorToInt(rect.xMin));
            var startY = Mathf.Max(0, Mathf.FloorToInt(rect.yMin));
            var endX = Mathf.Min(texture.width, Mathf.CeilToInt(rect.xMax));
            var endY = Mathf.Min(texture.height, Mathf.CeilToInt(rect.yMax));

            for (var y = startY; y < endY; y++)
            {
                for (var x = startX; x < endX; x++)
                {
                    if (texture.GetPixel(x, y).a <= 0.01f)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return sprite;
            }

            var trimmed = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            if (Mathf.Approximately(trimmed.width, rect.width) && Mathf.Approximately(trimmed.height, rect.height))
            {
                return sprite;
            }

            return Sprite.Create(texture, trimmed, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SolarMemoryMode] failed to trim entry sprite: " + ex.Message);
            return sprite;
        }
    }

    private static Sprite? CropEntryTitleArt(Sprite sprite)
    {
        try
        {
            var visual = VisualRegistry.ModeEntry("solar_memory");
            var ratio = visual?.TitleArtHeightRatio ?? DefaultEntryTitleArtHeightRatio;
            ratio = Mathf.Clamp(ratio, 0.05f, 1f);

            var rect = sprite.rect;
            var height = Mathf.Max(1f, rect.height * ratio);
            var cropped = new Rect(rect.x, rect.y + rect.height - height, rect.width, height);
            return Sprite.Create(sprite.texture, cropped, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SolarMemoryMode] failed to crop entry title sprite: " + ex.Message);
            return sprite;
        }
    }

    private static void SetImageSprite(Transform? target, Sprite sprite)
    {
        var image = target?.GetComponent<Image>();
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.preserveAspect = true;
        image.enabled = true;
    }

    private static void ClearEntryStateImages(Transform? stateRoot, Transform? keep)
    {
        if (stateRoot == null)
        {
            return;
        }

        foreach (var image in stateRoot.GetComponentsInChildren<Image>(true))
        {
            if (keep != null && image.transform == keep)
            {
                continue;
            }

            image.sprite = null;
            image.enabled = false;
        }

        foreach (var rawImage in stateRoot.GetComponentsInChildren<RawImage>(true))
        {
            if (keep != null && rawImage.transform == keep)
            {
                continue;
            }

            rawImage.texture = null;
            rawImage.enabled = false;
        }
    }

    private static void ConfigureEntryHoverState(GameObject entry)
    {
        var switchButton = entry.GetComponent<SwitchButton>();
        if (switchButton != null)
        {
            switchButton.Normal = FindStateCanvasGroup(entry.transform, "Normal");
            switchButton.Highlighted = FindStateCanvasGroup(entry.transform, "HighLighted", "Highlighted");
            switchButton.Pressed = FindStateCanvasGroup(entry.transform, "Pressed");
            switchButton.isAnimated = false;
            switchButton.animationType = SwitchButton.AnimationType.None;
            switchButton.transitionTime = 0f;
        }

        foreach (var component in entry.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().Name != "ButtonManager")
            {
                continue;
            }

            component.StopAllCoroutines();
            SetCanvasGroupField(component, "normalCG", 1f);
            SetCanvasGroupField(component, "highlightCG", 0f);
            SetCanvasGroupField(component, "disabledCG", 0f);
            component.enabled = false;
        }
    }

    private static CanvasGroup? FindStateCanvasGroup(Transform entry, params string[] names)
    {
        foreach (var name in names)
        {
            var state = entry.Find(name);
            if (state == null)
            {
                continue;
            }

            return state.GetComponent<CanvasGroup>() ?? state.gameObject.AddComponent<CanvasGroup>();
        }

        return null;
    }

    private static void SetCanvasGroupField(MonoBehaviour component, string fieldName, float alpha)
    {
        var field = component.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(component) is not CanvasGroup canvasGroup)
        {
            return;
        }

        canvasGroup.alpha = alpha;
        canvasGroup.blocksRaycasts = alpha > 0.99f;
        canvasGroup.interactable = alpha > 0.99f;
    }

    private static void ResetEntryVisualState(GameObject entry)
    {
        var switchButton = entry.GetComponent<SwitchButton>();
        if (switchButton != null)
        {
            switchButton.SetOffImmediate();
            return;
        }

        SetCanvasGroupState(FindStateCanvasGroup(entry.transform, "Normal"), true);
        SetCanvasGroupState(FindStateCanvasGroup(entry.transform, "HighLighted", "Highlighted"), false);
        SetCanvasGroupState(FindStateCanvasGroup(entry.transform, "Pressed"), false);
    }

    private static void SetCanvasGroupState(CanvasGroup? canvasGroup, bool active)
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = active ? 1f : 0f;
        canvasGroup.blocksRaycasts = active;
        canvasGroup.interactable = active;
    }

    private static void ConfigureEntryClick(GameObject entry, ModeChoiceUI modeChoice)
    {
        var switchButton = entry.GetComponent<SwitchButton>();
        if (switchButton != null)
        {
            switchButton.interactable = true;
            switchButton.onClick.RemoveAllListeners();
            switchButton.onClick.AddListener(new UnityAction(() => SolarMemoryRunLauncher.Start(modeChoice, SolarMemoryDeckIsolationRuntime.InitialPackSelection().ToList())));
        }

        foreach (var component in entry.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                component.enabled = false;
            }
        }

        foreach (var button in entry.GetComponentsInChildren<Button>(true))
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(new UnityAction(() => SolarMemoryRunLauncher.Start(modeChoice, SolarMemoryDeckIsolationRuntime.InitialPackSelection().ToList())));
        }
    }

    private static void ConfigureText(Text text, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        text.text = value;
        text.font = cachedFont ??= AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Math.Max(10, fontSize - 8);
        text.resizeTextMaxSize = fontSize;
    }

    private static void SetTmpText(Transform? target, string value)
    {
        if (target == null)
        {
            return;
        }

        var component = target.GetComponent("TMPro.TMP_Text");
        if (component == null)
        {
            return;
        }

        var property = component.GetType().GetProperty("text");
        property?.SetValue(component, value);
    }
}
