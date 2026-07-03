using System;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerModeEntryRuntime
{
    private const string EntryObjectName = "SunExp_TongtianTowerMode";
    private static Font? cachedFont;

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
            110,
            ConfigureRegisteredEntry,
            TongtianTowerRunLauncher.Start,
            SunExpIds.TongtianTowerTitle));
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
            SunExpLog.Error("Tongtian tower entry injection failed", ex);
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
        SetTmpText(entry.Find("Text/Text"), SunExpIds.TongtianTowerDescription + "\n" + SunExpIds.TongtianTowerSubtitle);

        var title = entry.Find("SunExpTitle");
        if (title == null)
        {
            var go = new GameObject("SunExpTitle", typeof(RectTransform));
            title = go.transform;
            title.SetParent(entry, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 0.58f);
            rect.anchorMax = new Vector2(0.92f, 0.88f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.AddComponent<Text>();
            ConfigureText(text, SunExpIds.TongtianTowerTitle, 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            return;
        }

        var titleText = title.GetComponent<Text>();
        if (titleText != null)
        {
            titleText.text = SunExpIds.TongtianTowerTitle;
        }

        title.gameObject.SetActive(true);
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
            switchButton.onClick.AddListener(new UnityAction(() => TongtianTowerRunLauncher.Start(modeChoice)));
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
            button.onClick.AddListener(new UnityAction(() => TongtianTowerRunLauncher.Start(modeChoice)));
        }
    }

    private static void ConfigureText(Text text, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        text.text = value;
        text.font = cachedFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
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
