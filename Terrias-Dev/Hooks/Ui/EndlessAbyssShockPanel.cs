using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public static class EndlessAbyssShockPanel
{
    private const string PanelName = "SunExp_EndlessAbyssShockPanel";
    private const float HeaderHeight = 96f;
    private const float OptionHeight = 96f;
    private const float OptionSpacing = 10f;
    private const float StrategyPaddingHorizontal = 18f;
    private const float StrategyPaddingVertical = 18f;
    private const float StrategyMinHeight = OptionHeight * 2.25f + OptionSpacing + StrategyPaddingVertical * 2f;
    private const float StrategyPreferredHeight = OptionHeight * 3f + OptionSpacing * 2f + StrategyPaddingVertical * 2f;
    private const float ButtonWidth = 120f;
    private const float ButtonHeight = 46f;
    private const float FooterHeight = 54f;
    private const int ButtonFontSize = 16;

    private static readonly Color WindowTint = new(0.026f, 0.028f, 0.045f, 0.98f);
    private static readonly Color HeaderTint = new(0.055f, 0.046f, 0.07f, 0.98f);
    private static readonly Color OptionTint = new(0.07f, 0.072f, 0.1f, 0.98f);
    private static readonly Color SelectedTint = new(0.21f, 0.15f, 0.06f, 0.98f);
    private static readonly Color Gold = new(0.92f, 0.78f, 0.42f);
    private static readonly Color SoftText = new(0.9f, 0.92f, 0.86f);
    private static GameObject? activePanel;
    private static Text? hintText;
    private static Button? confirmButton;
    private static readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Image> optionImages = new(StringComparer.Ordinal);

    public static bool IsOpen => activePanel != null;

    public static bool TryOpenPending(Action? onClosed, string source)
    {
        try
        {
            if (activePanel != null)
            {
                return true;
            }

            var request = EndlessAbyssShockService.PendingRequest();
            if (request == null)
            {
                return false;
            }

            Open(request, onClosed, source);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless abyss shock panel failed", ex);
            Close("EndlessAbyssShockPanel.OpenFailed");
            return false;
        }
    }

    private static void Open(EndlessAbyssShockRequest request, Action? onClosed, string source)
    {
        selected.Clear();
        optionImages.Clear();
        var parent = SunExpModalHost.ModalParent();
        if (parent == null)
        {
            return;
        }

        activePanel = SunExpModalHost.CreateFullscreenRoot(PanelName, parent, new Color(0f, 0f, 0f, 0.72f));
        SunExpTransientUiRegistry.Register("EndlessAbyssShock", Close);
        var window = SunExpUiComponents.CreateVerticalWindow(
            "Window",
            activePanel.transform,
            ResolveWindowSize(parent),
            SunExpUiSprites.Panel("[EndlessAbyssShock]"),
            WindowTint,
            new RectOffset(24, 24, 18, 14),
            12f);

        CreateHeader(window.transform, request);
        CreateOptions(window.transform);
        CreateFlexibleSpacer(window.transform);
        CreateFooter(window.transform, request, onClosed);
        RefreshSelectionHint();
        SunExpLog.Info("[EndlessAbyssShock] opened from " + source + "; key=" + request.Key + ".");
    }

    private static void CreateHeader(Transform parent, EndlessAbyssShockRequest request)
    {
        var header = SunExpUiComponents.CreatePanelSection(
            "Header",
            parent,
            SunExpUiSprites.Panel("[EndlessAbyssShock]"),
            HeaderTint,
            HeaderHeight,
            HeaderHeight);
        SunExpUiComponents.ConfigureVerticalLayout(header, new RectOffset(14, 14, 8, 8), 3f);

        SunExpUiComponents.AddTextBlock(header.transform, SunExpIds.EndlessAbyssShockName, 28, TextAnchor.MiddleCenter, Gold, 34f);
        SunExpUiComponents.AddTextBlock(
            header.transform,
            SunExpIds.EndlessAbyssGazeName + " " + EndlessAbyssGazeService.CurrentLevel()
            + " / " + "\u5fc5\u9009 " + EndlessAbyssGazeService.RequiredShockChoices(),
            15,
            TextAnchor.MiddleCenter,
            SoftText,
            24f);
        SunExpUiComponents.AddTextBlock(header.transform, TriggerText(request), 13, TextAnchor.MiddleCenter, SoftText, 22f);
    }

    private static void CreateOptions(Transform parent)
    {
        var root = SunExpUiComponents.CreatePanelSection(
            "StrategyArea",
            parent,
            SunExpUiSprites.Panel("[EndlessAbyssShock]"),
            new Color(0.012f, 0.014f, 0.03f, 0.92f),
            StrategyMinHeight,
            StrategyPreferredHeight);
        SunExpUiComponents.ConfigureVerticalLayout(
            root,
            new RectOffset(
                (int)StrategyPaddingHorizontal,
                (int)StrategyPaddingHorizontal,
                (int)StrategyPaddingVertical,
                (int)StrategyPaddingVertical),
            0f,
            childForceExpandHeight: true);

        var content = SunExpUiComponents.CreateVerticalScrollArea(
            root.transform,
            "StrategyContent",
            StrategyMinHeight - StrategyPaddingVertical * 2f,
            1f,
            OptionSpacing,
            26f,
            new Color(0f, 0f, 0f, 0.04f)).Content;

        CreateOption(content, EndlessAbyssShockOptionIds.Sacrifice, "\u732e\u796d", "\u968f\u673a\u9500\u6bc1 1 \u4ef6\u5df2\u88c5\u5907\u9057\u7269\uff0c\u83b7\u5f97 2 \u5f20\u968f\u673a\u5361\u724c\u3002");
        CreateOption(content, EndlessAbyssShockOptionIds.CrackCards, "\u88c2\u75d5", "\u7ed9\u5f53\u524d\u5361\u7ec4\u5185\u968f\u673a 2 \u5f20\u6ca1\u6709\u88c2\u75d5\u7684\u5361\u6dfb\u52a0\u88c2\u75d5\uff0c\u83b7\u5f97 300 \u91d1\u5e01\u3002");
        CreateOption(content, EndlessAbyssShockOptionIds.IncreaseGaze, "\u6ce8\u89c6", SunExpIds.EndlessAbyssGazeName + " +1\uff0c\u751f\u547d\u4e0a\u9650 +20\uff0c\u968f\u673a 1 \u4e2a\u672c\u6e90 +2\u3002");
        CreateOption(content, EndlessAbyssShockOptionIds.Evolution, "\u8fdb\u5316", "\u654c\u4eba\u989d\u5916\u83b7\u5f97 1 \u4e2a\u9ad8\u7ea7\u7279\u6027\uff0c\u83b7\u5f97 1 \u4e2a\u968f\u673a\u795d\u798f\u3002");
    }

    private static void CreateOption(RectTransform parent, string id, string title, string body)
    {
        var go = SunExpUiComponents.CreateLayoutObject("Option-" + id, parent);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = OptionHeight;
        element.minHeight = OptionHeight;
        var images = EndlessAbyssFramedTextCard.Create(
            go,
            "[EndlessAbyssShock]",
            OptionTint,
            title,
            body,
            Gold,
            SoftText);
        optionImages[id] = images.TintTarget;

        var button = go.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, images.ButtonTarget, Gold);
        button.onClick.AddListener(() => ToggleOption(id));
    }

    private static void CreateFooter(Transform parent, EndlessAbyssShockRequest request, Action? onClosed)
    {
        var footer = SunExpUiComponents.CreateFooterRow(parent, FooterHeight, new RectOffset(6, 6, 4, 4), 12f);

        hintText = SunExpUiComponents.AddTextBlock(footer.transform, "", 14, TextAnchor.MiddleLeft, SoftText, 34f, 1f);
        confirmButton = CreateButton(footer.transform, "\u627f\u53d7", new Vector2(ButtonWidth, ButtonHeight), () =>
        {
            var result = EndlessAbyssShockService.ApplyPending(selected.ToArray(), "EndlessAbyssShockPanel");
            if (!result.Success)
            {
                SetHint(result.Message);
                return;
            }

            Close("EndlessAbyssShockPanel.Confirm");
            onClosed?.Invoke();
        });
    }

    private static void ToggleOption(string id)
    {
        if (selected.Contains(id))
        {
            selected.Remove(id);
        }
        else if (selected.Count < EndlessAbyssGazeService.RequiredShockChoices())
        {
            selected.Add(id);
        }

        RefreshSelectionHint();
    }

    private static void RefreshSelectionHint()
    {
        var required = EndlessAbyssGazeService.RequiredShockChoices();
        foreach (var pair in optionImages)
        {
            pair.Value.color = selected.Contains(pair.Key) ? SelectedTint : OptionTint;
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = selected.Count == required;
        }

        SetHint("\u5df2\u9009 " + selected.Count + "/" + required + "\uff0c\u6df1\u6e0a\u9707\u8361\u5fc5\u987b\u7ed3\u7b97\u540e\u624d\u80fd\u7ee7\u7eed\u3002");
    }

    private static void SetHint(string value)
    {
        if (hintText != null)
        {
            hintText.text = value;
        }
    }

    private static string TriggerText(EndlessAbyssShockRequest request)
    {
        return "\u6765\u6e90\uff1a"
            + request.Trigger
            + " / \u5c42\u6570 "
            + Math.Max(1, request.Floor)
            + (string.IsNullOrWhiteSpace(request.NodeKind) ? "" : " / " + request.NodeKind);
    }

    public static void Close(string source)
    {
        selected.Clear();
        optionImages.Clear();
        confirmButton = null;
        hintText = null;
        SunExpModalHost.Close(ref activePanel, source, "[EndlessAbyssShock]");
        SunExpTransientUiRegistry.Unregister("EndlessAbyssShock");
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Action action)
    {
        return SunExpUiComponents.CreateTextButton(
            parent,
            label,
            size,
            SunExpUiSprites.Button("[EndlessAbyssShock]"),
            new Color(0.08f, 0.07f, 0.11f, 0.98f),
            SoftText,
            ButtonFontSize,
            action);
    }

    private static void CreateFlexibleSpacer(Transform parent)
    {
        SunExpUiComponents.CreateFlexibleSpacer(parent);
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var rect = parent as RectTransform;
        var width = rect != null && rect.rect.width > 0f ? rect.rect.width : 1280f;
        var height = rect != null && rect.rect.height > 0f ? rect.rect.height : 720f;
        return new Vector2(Mathf.Clamp(width * 0.58f, 560f, 760f), Mathf.Clamp(height * 0.76f, 520f, 660f));
    }
}
