using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public static class EndlessAbyssMilestoneRewardPanel
{
    private const string PanelName = "SunExp_EndlessAbyssMilestoneRewardPanel";
    private const float HeaderHeight = 96f;
    private const float RewardCardHeight = 96f;
    private const float RowHeight = 56f;
    private const float ButtonWidth = 120f;
    private const float ButtonHeight = 46f;
    private const float FooterHeight = 54f;
    private const int ButtonFontSize = 16;

    private static readonly Color WindowTint = new(0.024f, 0.03f, 0.052f, 0.98f);
    private static readonly Color HeaderTint = new(0.035f, 0.038f, 0.075f, 0.98f);
    private static readonly Color CardTint = new(0.062f, 0.07f, 0.105f, 0.98f);
    private static readonly Color DisabledTint = new(0.045f, 0.045f, 0.052f, 0.94f);
    private static readonly Color Gold = new(0.9f, 0.76f, 0.4f);
    private static readonly Color SoftText = new(0.9f, 0.93f, 0.88f);
    private static GameObject? activePanel;
    private static Transform? contentRoot;
    private static Text? hintText;
    private static int activeFloor;

    public static bool IsOpen => activePanel != null;

    public static bool TryOpenForCurrentFloor(string source)
    {
        try
        {
            if (activePanel != null)
            {
                return true;
            }

            var floor = Math.Max(1, GameSaveManager.GetValue<int>(SunExpIds.EndlessSeaFloorKey));
            if (!EndlessAbyssMilestoneRewardService.CanClaim(floor))
            {
                return false;
            }

            Open(floor, source);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless abyss milestone reward panel failed", ex);
            Close("EndlessAbyssMilestone.OpenFailed");
            return false;
        }
    }

    private static void Open(int floor, string source)
    {
        activeFloor = Math.Max(1, floor);
        var parent = SunExpModalHost.ModalParent();
        if (parent == null)
        {
            return;
        }

        activePanel = SunExpModalHost.CreateFullscreenRoot(PanelName, parent, new Color(0f, 0f, 0f, 0.68f));
        SunExpTransientUiRegistry.Register("EndlessAbyssMilestone", Close);
        var window = SunExpUiComponents.CreateVerticalWindow(
            "Window",
            activePanel.transform,
            ResolveWindowSize(parent),
            SunExpUiSprites.Panel("[EndlessAbyssMilestone]"),
            WindowTint,
            new RectOffset(24, 24, 18, 14),
            12f);

        CreateHeader(window.transform);
        contentRoot = CreateContentRoot(window.transform);
        CreateFooter(window.transform);
        ShowMainOptions();
        SunExpLog.Info("[EndlessAbyssMilestone] opened from " + source + "; floor=" + activeFloor + ".");
    }

    private static void CreateHeader(Transform parent)
    {
        var header = SunExpUiComponents.CreatePanelSection(
            "Header",
            parent,
            SunExpUiSprites.Panel("[EndlessAbyssMilestone]"),
            HeaderTint,
            HeaderHeight,
            HeaderHeight);
        SunExpUiComponents.ConfigureVerticalLayout(header, new RectOffset(14, 14, 8, 8), 3f);

        SunExpUiComponents.AddTextBlock(header.transform, "\u6df1\u6e0a\u91cc\u7a0b\u7891", 28, TextAnchor.MiddleCenter, Gold, 36f);
        SunExpUiComponents.AddTextBlock(header.transform, "\u7b2c " + activeFloor + " \u5c42\u5956\u52b1\u9009\u62e9", 15, TextAnchor.MiddleCenter, SoftText, 24f);
    }

    private static Transform CreateContentRoot(Transform parent)
    {
        var root = SunExpUiComponents.CreatePanelSection(
            "ContentRoot",
            parent,
            SunExpUiSprites.Panel("[EndlessAbyssMilestone]"),
            new Color(0.01f, 0.014f, 0.03f, 0.9f),
            330f,
            330f,
            1f);
        SunExpUiComponents.ConfigureVerticalLayout(
            root,
            new RectOffset(18, 18, 18, 18),
            10f,
            childForceExpandHeight: true);
        return root.transform;
    }

    private static void CreateFooter(Transform parent)
    {
        var footer = SunExpUiComponents.CreateFooterRow(parent, FooterHeight, new RectOffset(6, 6, 4, 4), 12f);
        hintText = SunExpUiComponents.AddTextBlock(footer.transform, "", 14, TextAnchor.MiddleLeft, SoftText, 34f, 1f);
    }

    private static void ShowMainOptions()
    {
        ClearContent();
        ConfigureContentRootLayout(18, 18, 18, 18, 10f, true);
        var scrollContent = SunExpUiComponents.CreateVerticalScrollArea(
            contentRoot!,
            "RewardOptions",
            220f,
            1f,
            10f,
            24f,
            new Color(0f, 0f, 0f, 0.05f)).Content;

        CreateRewardCard(
            scrollContent,
            "\u4efb\u9009 1 \u4ef6 1/2/3 \u9636\u9057\u7269",
            "\u6253\u5f00\u9057\u7269\u5217\u8868\u5e76\u4ece\u4e2d\u6311\u9009\u3002",
            EndlessAbyssMilestoneRewardService.RelicCandidates().Count > 0,
            ShowRelicPicker);
        CreateRewardCard(
            scrollContent,
            "\u968f\u673a\u83b7\u5f97 1 \u5f20\u5f02\u6b21\u5143\u5361",
            "\u4ece\u914d\u7f6e\u7684\u5f02\u6b21\u5143\u5361\u6c60\u4e2d\u62bd\u53d6\u3002",
            true,
            GrantOtherDimensionCard);
        CreateRewardCard(
            scrollContent,
            "\u9009\u62e9 1 \u5f20\u5361\u724c\u6e05\u9664\u711a\u6bc1",
            "\u4ec5\u663e\u793a\u5f53\u524d\u5361\u7ec4\u4e2d\u62e5\u6709\u711a\u6bc1\u7684\u5361\u3002",
            EndlessAbyssMilestoneRewardService.BurnoutCards().Count > 0,
            ShowBurnoutPicker);
        CreateRewardCard(
            scrollContent,
            "\u9009\u62e9 1 \u5f20\u5361\u724c\u6dfb\u52a0\u7edd\u706d",
            "\u4ec5\u663e\u793a\u5f53\u524d\u5361\u7ec4\u4e2d\u5c1a\u672a\u62e5\u6709\u7edd\u706d\u7684\u5361\u3002",
            EndlessAbyssMilestoneRewardService.ExtinctionTargets().Count > 0,
            ShowExtinctionPicker);
        SetHint("\u9009\u62e9 1 \u4e2a\u91cc\u7a0b\u7891\u5956\u52b1\u3002");
    }

    private static void CreateRewardCard(Transform parent, string title, string body, bool enabled, Action action)
    {
        var go = SunExpUiComponents.CreateLayoutObject("RewardCard", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = RewardCardHeight;
        element.preferredHeight = RewardCardHeight;
        var images = EndlessAbyssFramedTextCard.Create(
            go,
            "[EndlessAbyssMilestone]",
            enabled ? CardTint : DisabledTint,
            title,
            body,
            enabled ? Gold : new Color(0.55f, 0.55f, 0.55f),
            enabled ? SoftText : new Color(0.58f, 0.58f, 0.58f));

        var button = go.AddComponent<Button>();
        button.targetGraphic = images.ButtonTarget;
        button.interactable = enabled;
        button.onClick.AddListener(() => RunAction(action, "RewardCard:" + title));
    }

    private static void ShowRelicPicker()
    {
        var options = EndlessAbyssMilestoneRewardService.RelicCandidates();
        ShowList("\u9009\u62e9\u9057\u7269", options, option => "T" + option.Tier, option => option.Name, option =>
        {
            if (EndlessAbyssMilestoneRewardService.GrantRelic(activeFloor, option.Id, out var message))
            {
                Close("EndlessAbyssMilestone.Relic");
            }
            else
            {
                SetHint(message);
            }
        });
    }

    private static void ShowBurnoutPicker()
    {
        var options = EndlessAbyssMilestoneRewardService.BurnoutCards();
        ShowList("\u6e05\u9664\u711a\u6bc1", options, _ => "\u711a\u6bc1", option => option.Name, option =>
        {
            if (EndlessAbyssMilestoneRewardService.RemoveBurnout(activeFloor, option.Card, out var message))
            {
                Close("EndlessAbyssMilestone.RemoveBurnout");
            }
            else
            {
                SetHint(message);
            }
        });
    }

    private static void ShowExtinctionPicker()
    {
        var options = EndlessAbyssMilestoneRewardService.ExtinctionTargets();
        ShowList("\u6dfb\u52a0\u7edd\u706d", options, _ => "\u5361\u724c", option => option.Name, option =>
        {
            if (EndlessAbyssMilestoneRewardService.AddExtinction(activeFloor, option.Card, out var message))
            {
                Close("EndlessAbyssMilestone.AddExtinction");
            }
            else
            {
                SetHint(message);
            }
        });
    }

    private static void GrantOtherDimensionCard()
    {
        if (EndlessAbyssMilestoneRewardService.GrantRandomOtherDimensionCard(activeFloor, out var message))
        {
            Close("EndlessAbyssMilestone.OtherDimension");
        }
        else
        {
            SetHint(message);
        }
    }

    private static void ShowList<T>(
        string title,
        IReadOnlyList<T> options,
        Func<T, string> badge,
        Func<T, string> name,
        Action<T> select)
    {
        ClearContent();
        var root = contentRoot;
        if (root == null)
        {
            return;
        }

        ConfigureContentRootLayout(18, 18, 16, 18, 10f, true);

        var titleRow = SunExpUiComponents.CreateLayoutObject("ListTitle", root);
        var titleElement = titleRow.AddComponent<LayoutElement>();
        titleElement.minHeight = ButtonHeight;
        titleElement.preferredHeight = ButtonHeight;
        var titleLayout = titleRow.AddComponent<HorizontalLayoutGroup>();
        titleLayout.spacing = 12f;
        titleLayout.childControlWidth = true;
        titleLayout.childControlHeight = true;
        titleLayout.childForceExpandWidth = false;
        titleLayout.childForceExpandHeight = false;
        titleLayout.childAlignment = TextAnchor.MiddleCenter;
        CreateButton(titleRow.transform, "\u8fd4\u56de", new Vector2(ButtonWidth, ButtonHeight), ShowMainOptions);
        SunExpUiComponents.AddTextBlock(titleRow.transform, title + " (" + options.Count + ")", 18, TextAnchor.MiddleLeft, Gold, 34f, 1f);

        var scrollContent = SunExpUiComponents.CreateVerticalScrollArea(
            root,
            "List",
            220f,
            1f,
            10f,
            24f,
            new Color(0f, 0f, 0f, 0.05f)).Content;
        foreach (var option in options)
        {
            CreateRow(scrollContent, badge(option), name(option), () => select(option));
        }

        SetHint(options.Count == 0 ? "\u5f53\u524d\u6ca1\u6709\u53ef\u9009\u9879\u3002" : "\u4ece\u5217\u8868\u4e2d\u9009\u62e9 1 \u9879\u3002");
    }

    private static void CreateRow(Transform parent, string badge, string name, Action action)
    {
        var row = SunExpUiComponents.CreateLayoutObject("Row", parent);
        var element = row.AddComponent<LayoutElement>();
        element.minHeight = RowHeight;
        element.preferredHeight = RowHeight;
        SunExpUiBuilder.ApplyLabelImage(row, SunExpUiSprites.Label("[EndlessAbyssMilestone]"), CardTint, true);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 5, 5);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        SunExpUiComponents.AddTextBlock(row.transform, badge, 13, TextAnchor.MiddleCenter, Gold, 40f, 0f, 74f);
        SunExpUiComponents.AddTextBlock(row.transform, name, 15, TextAnchor.MiddleLeft, SoftText, 40f, 1f);
        CreateButton(row.transform, "\u9009\u62e9", new Vector2(ButtonWidth, ButtonHeight), action);
    }

    private static void ClearContent()
    {
        if (contentRoot == null)
        {
            return;
        }

        SunExpUiPool.ReleaseOrDestroyChildren(contentRoot, "EndlessAbyssMilestone.ClearContent", "[EndlessAbyssMilestone]");
    }

    private static void ConfigureContentRootLayout(int left, int right, int top, int bottom, float spacing, bool expandHeight)
    {
        if (contentRoot == null)
        {
            return;
        }

        SunExpUiComponents.ConfigureVerticalLayout(
            contentRoot.gameObject,
            new RectOffset(left, right, top, bottom),
            spacing,
            childForceExpandHeight: expandHeight);
    }

    public static void Close(string source)
    {
        ClearContent();
        contentRoot = null;
        hintText = null;
        activeFloor = 0;
        SunExpModalHost.Close(ref activePanel, source, "[EndlessAbyssMilestone]");
        SunExpTransientUiRegistry.Unregister("EndlessAbyssMilestone");
    }

    private static void SetHint(string value)
    {
        if (hintText != null)
        {
            hintText.text = value;
        }
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Action action)
    {
        return SunExpUiComponents.CreateTextButton(
            parent,
            label,
            size,
            SunExpUiSprites.Button("[EndlessAbyssMilestone]"),
            new Color(0.08f, 0.07f, 0.11f, 0.98f),
            SoftText,
            ButtonFontSize,
            () => RunAction(action, "Button:" + label));
    }

    private static void RunAction(Action action, string source)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[EndlessAbyssMilestone] UI action failed: " + source, ex);
            SetHint("\u91cc\u7a0b\u7891\u64cd\u4f5c\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002");
        }
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var rect = parent as RectTransform;
        var width = rect != null && rect.rect.width > 0f ? rect.rect.width : 1280f;
        var height = rect != null && rect.rect.height > 0f ? rect.rect.height : 720f;
        return new Vector2(Mathf.Clamp(width * 0.6f, 600f, 820f), Mathf.Clamp(height * 0.74f, 540f, 660f));
    }
}
