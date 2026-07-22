using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraUi.Shared;
using Data.Save;
using StarterDeckArbiter.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace Terrias.Dll.Hooks;

public static class EndlessSeaIntroBoardRuntime
{
    private const float FooterHeight = 46f;
    private const float CoverMaxWidth = 150f;
    private const float CoverMaxHeight = 225f;
    private const float ThemeCellWidth = 166f;
    private const float ThemeCellHeight = 284f;
    private const float TooltipWidth = 360f;
    private const float TooltipHeight = 224f;
    private const string DefaultCoverPackId = "cardpack_1";
    private const string DefaultStarterDeckHint = "请选择一个主题。开局卡组 = 固定 11 张官方基础卡 + 当前主题 4 张。";
    private const string DefaultOnlyStarterDeckHint = "未检测到可用扩展主题；请确认已开启对应官方卡包。当前可选择默认主题。";

    private static readonly Color Gold = new(0.92f, 0.74f, 0.34f);
    private static readonly Color PaleGold = new(1f, 0.91f, 0.62f);
    private static readonly Color SoftText = new(0.87f, 0.91f, 0.97f);
    private static readonly Color MutedText = new(0.62f, 0.7f, 0.84f);
    private static readonly Color PanelTint = new(0.035f, 0.04f, 0.13f, 0.98f);
    private static readonly Color SectionTint = new(0.018f, 0.024f, 0.09f, 0.92f);
    private static readonly Color DeckTint = new(0.052f, 0.064f, 0.16f, 0.94f);
    private static readonly Color DeckHoverTint = new(0.16f, 0.16f, 0.27f, 1f);
    private static readonly Color DeckPressedTint = new(0.22f, 0.16f, 0.07f, 1f);
    private static readonly Dictionary<string, Sprite?> cardIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite?> packCoverCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Button> deckButtons = new();
    private static readonly List<GameObject> deckSelectedFrames = new();
    private static GameObject? activePanel;
    private static RectTransform? activeTooltipLayer;
    private static GameObject? activeTooltip;
    private static Text? hintText;
    private static string starterDeckHint = DefaultStarterDeckHint;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "MapManager.MapUIStart", TryShowIntroBoard);
        RegisterAfter(modConfig, "NormalMapManager.MapUIStart", TryShowIntroBoard);
        RegisterAfter(modConfig, "MapSelectUI.Start", TryShowIntroBoard);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "EndlessSeaIntro");
    }

    private static void TryShowIntroBoard(ModHookContext context)
    {
        TryShowIntroBoard("hook");
    }

    private static bool TryShowIntroBoard(string source)
    {
        try
        {
            if (!ShouldShow())
            {
                return false;
            }

            var roleTable = RoleTable.Instance;
            if (roleTable == null || IsApplied(roleTable))
            {
                return false;
            }

            if (activePanel != null)
            {
                return true;
            }

            var parent = TerriasModalHost.ModalParent();
            if (parent == null)
            {
                TerriasLog.Warn("[EndlessSeaIntro] skipped: UI canvas unavailable from " + source + ".");
                return false;
            }

            TerriasLog.Info("[EndlessSeaIntro] opening intro board from " + source + ".");
            ShowIntroBoard(roleTable, parent);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea intro board failed", ex);
            return false;
        }
    }

    private static bool ShouldShow()
    {
        return EndlessSeaModeRuntime.IsEndlessSeaRun()
            && EndlessSeaModeRuntime.CurrentFloor() == 1
            && GameSaveManager.GetValue<string>(TerriasIds.EndlessSeaIntroSeenKey) != "1"
            && GameSaveManager.GetValue<string>(TerriasIds.EndlessSeaStarterDeckAppliedKey) != "1";
    }

    private static void ShowIntroBoard(RoleTable roleTable, Transform parent)
    {
        activePanel = TerriasModalHost.CreateFullscreenRoot(
            "TerriasEndlessSeaIntroBoard",
            parent,
            new Color(0f, 0f, 0f, 0.68f));
        TerriasTransientUiRegistry.Register("EndlessSeaIntro", ClosePanel);
        deckButtons.Clear();
        deckSelectedFrames.Clear();
        HideTooltip();

        var windowSize = ResolveWindowSize(parent);
        var windowRect = TerriasUiBuilder.CreateRect(
            "Board",
            activePanel.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            windowSize);
        var window = windowRect.gameObject;
        TerriasUiBuilder.ApplyPanelImage(window, TerriasUiSprites.Panel("[EndlessSeaIntro]"), PanelTint, true);
        starterDeckHint = DefaultStarterDeckHint;

        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateHeader(window.transform);
        CreateDivider(window.transform);
        CreateMainContent(window.transform, roleTable);
        CreateFooter(window.transform);
        activeTooltipLayer = CreateTooltipLayer(window.transform);
        Canvas.ForceUpdateCanvases();
    }

    private static void CreateHeader(Transform parent)
    {
        var header = CreateLayoutObject("Header", parent);
        var element = header.AddComponent<LayoutElement>();
        element.preferredHeight = 68f;
        element.minHeight = 68f;

        var title = AddTextFill(header.transform, TerriasIds.EndlessSeaTitle, 32, TextAnchor.UpperCenter, Gold);
        title.fontStyle = FontStyle.Bold;
        AddTextShadow(title, new Color(0f, 0f, 0f, 0.72f), new Vector2(1.5f, -1.5f));

        var subtitle = AddTextFill(header.transform, "玩法说明 · 主题卡包", 17, TextAnchor.LowerCenter, PaleGold);
        subtitle.fontStyle = FontStyle.Bold;
        var subtitleRect = subtitle.GetComponent<RectTransform>();
        subtitleRect.offsetMin = new Vector2(0f, 3f);
        subtitleRect.offsetMax = new Vector2(0f, -39f);
    }

    private static void CreateDivider(Transform parent)
    {
        var divider = CreateLayoutObject("Divider", parent);
        var element = divider.AddComponent<LayoutElement>();
        element.preferredHeight = 2f;
        element.minHeight = 2f;
        TerriasUiBuilder.ApplyPanelImage(divider, null, new Color(Gold.r, Gold.g, Gold.b, 0.85f));
    }

    private static void CreateMainContent(Transform parent, RoleTable roleTable)
    {
        var root = CreateLayoutObject("MainContent", parent);
        var element = root.AddComponent<LayoutElement>();
        element.minHeight = 470f;
        element.flexibleHeight = 1f;

        var layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        CreateRulesPane(root.transform);
        CreateThemePane(root.transform, roleTable);
    }

    private static void CreateRulesPane(Transform parent)
    {
        var pane = CreateLayoutObject("RulesPane", parent);
        var element = pane.AddComponent<LayoutElement>();
        element.minWidth = 320f;
        element.preferredWidth = 410f;
        element.flexibleWidth = 0.9f;

        var layout = pane.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 16, 4, 6);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var label = AddTextBlock(pane.transform, "玩法说明", 19, TextAnchor.MiddleLeft, PaleGold, 28f);
        label.fontStyle = FontStyle.Bold;
        AddTextShadow(label, new Color(0f, 0f, 0f, 0.55f), new Vector2(1f, -1f));

        var scrollRoot = CreateLayoutObject("RulesScroll", pane.transform);
        var scrollElement = scrollRoot.AddComponent<LayoutElement>();
        scrollElement.flexibleHeight = 1f;
        scrollElement.minHeight = 380f;

        var viewport = TerriasUiBuilder.CreateRect(
            "Viewport",
            scrollRoot.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        viewport.offsetMin = new Vector2(0f, 0f);
        viewport.offsetMax = new Vector2(0f, 0f);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.04f);
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var content = TerriasUiBuilder.CreateRect(
            "Content",
            viewport,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        content.offsetMin = new Vector2(0f, 0f);
        content.offsetMax = new Vector2(-8f, 0f);

        var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateRuleSection(content, "一、开局卡组", "1. 开局时需要选择一个主题卡包。\n2. 开局卡组由两部分组成：\n   固定 11 张基础卡 + 当前主题 4 张主题卡。\n3. 【学院必修】为默认主题，始终可选。\n4. 其它主题会根据当前已启用的卡包动态显示。\n5. 鼠标悬停主题卡包时，可以查看该主题包含的 4 张主题卡牌。");
        CreateRuleSection(content, "二、地图节点", "1. 无尽之渊以“层”为单位推进，每层包含 6 个地图节点。\n2. 每层第一个节点固定为当前层怪物，最后一个节点固定为【首领】或【无尽首领】。\n3. 中间 4 个节点初始为空，需要从手牌中的节点牌拖入配置。\n4. 每轮可选节点牌的配比为：\n   1 张【休息处】 + 1 张【建筑牌】 + 若干【普通怪/精英】节点牌。\n5. 战斗节点会根据当前层数、当前模式和节点类型进行抽取。");
        CreateRuleSection(content, "三、战斗奖励与卡牌规则", "1. 无尽之渊使用本玩法专属奖励规则。\n2. 战斗结束后会根据当前层数和节点类型提供不同奖励。\n3. 本模式内通过任意渠道获得的卡牌，都会默认附着【焚毁】。\n4. 从第 2 层开始，每层可展开一次专属里程碑奖励选择。");
        CreateRuleSection(content, "四、潜行模式", "1. 第 1 至第 6 层为【潜行模式】。\n2. 随着层数提升，敌人强度和战斗奖励都会逐步提高。\n3. 潜行模式下，每层会在地图节点场景触发一次【深渊震荡】。");
        CreateRuleSection(content, "五、无尽模式", "1. 第 7 层起进入【无尽模式】。\n2. 无尽模式没有固定终点。\n3. 无尽模式下，每场战斗会触发一次【深渊震荡】。\n4. 【注视等级】越高，深渊震荡中必须选择的策略数量越多，最高为 3。");
        CreateRuleSection(content, "六、深渊震荡", "1. 深渊震荡会提供 3 个互斥策略：\n   【遗物坠落】：随机销毁 1 件已装备遗物。\n   【湮灭浸染】：给当前卡组内随机 3 张卡添加【湮灭】。\n   【注视加深】：【注视等级】+1。\n2. 必须选满当前要求数量并结算后，才能继续推进。");
        CreateRuleSection(content, "七、本源上限", "1. 无尽之渊中，魔力、精神、幸运、感知的上限至少提高到 50。\n2. 50 层奖励属于通用本源里程碑；在其它游玩模式中，只要本源上限与实际层数足够，也能解锁。\n\n<color=#FFD36A>魔力达到 50：</color>\n1. 每场战斗开始时，获得 2 张附着【绝灭】火漆的【不稳定思绪】。\n\n<color=#FFD36A>精神达到 50：</color>\n1. 每场战斗开始时，魔能上限提高 3 点。\n\n<color=#FFD36A>幸运达到 50：</color>\n1. 每场战斗中，检定骰与数值骰获得额外50点加成，达到150点数时效果可再额外触发2次。\n\n<color=#FFD36A>感知达到 50：</color>\n1. 每场战斗结束后，生命值恢复至上限。");
        CreateRuleSection(content, "八、玩法目标", "1. 在有限资源与持续损耗中完成更高层数的挑战。\n2. 合理选择主题卡组、节点路线、奖励类型和深渊震荡策略。\n3. 通过休息处、建筑节点和里程碑奖励调整状态，通过战斗奖励维持卡组强度。\n4. 在无尽模式中，尽可能延缓牌库湮灭、遗物损耗、注视等级和战斗压力的累积。");

        var scroll = scrollRoot.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;
        scroll.verticalNormalizedPosition = 1f;
    }

    private static void CreateRuleSection(Transform parent, string title, string body)
    {
        var section = CreateLayoutObject("Rule_" + title, parent);

        var layout = section.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 8, 5, 7);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var titleText = AddAutoTextBlock(section.transform, title, 16, TextAnchor.UpperLeft, Gold);
        titleText.fontStyle = FontStyle.Bold;
        var bodyText = AddAutoTextBlock(section.transform, EndlessSeaRichTextSanitizer.Sanitize(body), 14, TextAnchor.UpperLeft, SoftText);
        bodyText.supportRichText = true;
        bodyText.lineSpacing = 1.12f;
    }

    private static void CreateThemePane(Transform parent, RoleTable roleTable)
    {
        var pane = CreateLayoutObject("ThemePane", parent);
        var element = pane.AddComponent<LayoutElement>();
        element.minWidth = 380f;
        element.preferredWidth = 560f;
        element.flexibleWidth = 1.2f;
        TerriasUiBuilder.ApplyPanelImage(pane, TerriasUiSprites.Panel("[EndlessSeaIntro]"), SectionTint, true);

        var layout = pane.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 14);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var titleRow = CreateLayoutObject("ThemeHeader", pane.transform);
        titleRow.AddComponent<LayoutElement>().preferredHeight = 32f;
        var titleLayout = titleRow.AddComponent<HorizontalLayoutGroup>();
        titleLayout.childControlWidth = true;
        titleLayout.childControlHeight = true;
        titleLayout.childForceExpandWidth = false;
        titleLayout.childForceExpandHeight = true;

        var label = AddTextBlock(titleRow.transform, "选择开局主题", 19, TextAnchor.MiddleLeft, PaleGold, 30f, 1f);
        label.fontStyle = FontStyle.Bold;
        AddTextBlock(titleRow.transform, "悬停查看 4 张主题卡", 13, TextAnchor.MiddleRight, MutedText, 30f, 0f, 170f);

        var profiles = EndlessSeaStarterDeckCatalog.AvailableProfiles();
        starterDeckHint = profiles.Any(profile => !string.IsNullOrWhiteSpace(profile.RequiredPackId))
            ? DefaultStarterDeckHint
            : DefaultOnlyStarterDeckHint;

        var scrollRoot = CreateLayoutObject("ThemeScroll", pane.transform);
        var scrollElement = scrollRoot.AddComponent<LayoutElement>();
        scrollElement.minHeight = 410f;
        scrollElement.flexibleHeight = 1f;
        TerriasUiBuilder.ApplyLabelImage(scrollRoot, TerriasUiSprites.Label("[EndlessSeaIntro]"), new Color(0.01f, 0.014f, 0.045f, 0.56f), true);

        var viewport = TerriasUiBuilder.CreateRect(
            "Viewport",
            scrollRoot.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        viewport.offsetMin = new Vector2(8f, 8f);
        viewport.offsetMax = new Vector2(-8f, -8f);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.04f);
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var gridContent = TerriasUiBuilder.CreateRect(
            "ThemeGrid",
            viewport,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        gridContent.offsetMin = Vector2.zero;
        gridContent.offsetMax = Vector2.zero;

        var contentWidth = ResolveThemeGridContentWidth(parent);
        var columns = contentWidth >= 530f ? 3 : 2;
        var grid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(6, 6, 6, 6);
        grid.spacing = new Vector2(14f, 14f);
        grid.cellSize = new Vector2(ThemeCellWidth, ThemeCellHeight);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        gridContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollRoot.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = gridContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        foreach (var profile in profiles)
        {
            CreateThemePackButton(gridContent, roleTable, profile);
        }
    }

    private static void CreateThemePackButton(Transform parent, RoleTable roleTable, EndlessSeaStarterDeckProfile profile)
    {
        var panel = CreateLayoutObject("Theme_" + profile.Id, parent);
        var image = TerriasUiBuilder.ApplyLabelImage(panel, TerriasUiSprites.Label("[EndlessSeaIntro]"), DeckTint, true);
        var button = panel.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(
            button,
            image,
            Color.white,
            DeckHoverTint,
            DeckPressedTint,
            new Color(0.45f, 0.45f, 0.45f, 0.7f));
        button.onClick.AddListener(() =>
        {
            MarkSelectedTheme(panel);
            ApplyStarterDeck(roleTable, profile);
        });
        deckButtons.Add(button);

        var selectedFrame = CreateSelectedFrame(panel.transform);
        deckSelectedFrames.Add(selectedFrame);

        var probe = panel.AddComponent<EndlessSeaThemePackHoverProbe>();
        probe.Configure(source => ShowThemeTooltip(profile, source), HideTooltip);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        CreatePackCover(panel.transform, profile);
        var title = AddTextBlock(panel.transform, profile.Title, 16, TextAnchor.MiddleCenter, Gold, 25f);
        title.fontStyle = FontStyle.Bold;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 11;
        title.resizeTextMaxSize = 16;
        AddTextShadow(title, new Color(0f, 0f, 0f, 0.55f), new Vector2(1f, -1f));

        var subtitle = AddTextBlock(panel.transform, profile.Subtitle, 12, TextAnchor.MiddleCenter, MutedText, 18f);
        subtitle.resizeTextForBestFit = true;
        subtitle.resizeTextMinSize = 10;
        subtitle.resizeTextMaxSize = 12;
        selectedFrame.transform.SetAsLastSibling();
    }

    private static GameObject CreateSelectedFrame(Transform parent)
    {
        var frameRect = TerriasUiBuilder.CreateRect(
            "SelectedFrame",
            parent,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        frameRect.offsetMin = new Vector2(-2f, -2f);
        frameRect.offsetMax = new Vector2(2f, 2f);

        var layout = frameRect.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var frame = frameRect.gameObject.AddComponent<Image>();
        frame.sprite = TerriasUiSprites.Button("[EndlessSeaIntro]");
        frame.type = frame.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        frame.fillCenter = false;
        frame.color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
        frame.raycastTarget = false;
        frameRect.gameObject.SetActive(false);
        return frameRect.gameObject;
    }

    private static void CreatePackCover(Transform parent, EndlessSeaStarterDeckProfile profile)
    {
        var host = CreateLayoutObject("Cover", parent);
        var element = host.AddComponent<LayoutElement>();
        element.minWidth = CoverMaxWidth;
        element.preferredWidth = CoverMaxWidth;
        element.minHeight = CoverMaxHeight;
        element.preferredHeight = CoverMaxHeight;
        TerriasUiBuilder.ApplyPanelImage(host, TerriasUiSprites.Panel("[EndlessSeaIntro]"), new Color(0.012f, 0.016f, 0.052f, 0.92f));

        var sprite = TryLoadPackCover(profile);
        if (sprite == null)
        {
            var fallback = AddTextFill(host.transform, profile.Title, 18, TextAnchor.MiddleCenter, PaleGold);
            fallback.fontStyle = FontStyle.Bold;
            fallback.resizeTextForBestFit = true;
            fallback.resizeTextMinSize = 11;
            fallback.resizeTextMaxSize = 18;
            return;
        }

        var coverRect = TerriasUiBuilder.CreateRect(
            "Image",
            host.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(CoverMaxWidth, CoverMaxHeight));
        var cover = coverRect.gameObject.AddComponent<Image>();
        cover.sprite = sprite;
        cover.color = Color.white;
        cover.preserveAspect = true;
        cover.raycastTarget = false;
    }

    private static void MarkSelectedTheme(GameObject selected)
    {
        foreach (var frame in deckSelectedFrames)
        {
            if (frame != null)
            {
                frame.SetActive(frame.transform.parent == selected.transform);
            }
        }
    }

    private static void CreateFooter(Transform parent)
    {
        var footer = CreateLayoutObject("Footer", parent);
        var element = footer.AddComponent<LayoutElement>();
        element.preferredHeight = FooterHeight;
        element.minHeight = FooterHeight;

        hintText = AddTextFill(footer.transform, starterDeckHint, 15, TextAnchor.MiddleCenter, PaleGold);
        hintText.resizeTextForBestFit = true;
        hintText.resizeTextMinSize = 11;
        hintText.resizeTextMaxSize = 15;
    }

    private static void ShowThemeTooltip(EndlessSeaStarterDeckProfile profile, RectTransform source)
    {
        if (activeTooltipLayer == null)
        {
            return;
        }

        HideTooltip();
        var tooltipRect = TerriasUiBuilder.CreateRect(
            "ThemeCardsTooltip",
            activeTooltipLayer,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, 1f),
            new Vector2(TooltipWidth, TooltipHeight));
        activeTooltip = tooltipRect.gameObject;
        activeTooltip.transform.SetAsLastSibling();
        var background = TerriasUiBuilder.ApplyLabelImage(activeTooltip, TerriasUiSprites.Label("[EndlessSeaIntro]"), new Color(0.015f, 0.018f, 0.055f, 0.88f));
        if (background != null)
        {
            background.raycastTarget = false;
        }

        var layout = activeTooltip.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var title = AddTextBlock(activeTooltip.transform, profile.Title + " · 主题卡", 16, TextAnchor.MiddleLeft, PaleGold, 24f);
        title.fontStyle = FontStyle.Bold;

        var gridRoot = CreateLayoutObject("Cards", activeTooltip.transform);
        gridRoot.AddComponent<LayoutElement>().preferredHeight = 160f;
        var grid = gridRoot.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.cellSize = new Vector2(160f, 74f);
        grid.spacing = new Vector2(10f, 10f);

        foreach (var cardId in profile.ThemeCardIds.Take(EndlessSeaStarterDeckCatalog.ThemeDeckSize))
        {
            CreateTooltipCard(gridRoot.transform, cardId);
        }

        PositionTooltip(tooltipRect, source);
    }

    private static RectTransform CreateTooltipLayer(Transform parent)
    {
        var layer = TerriasUiBuilder.CreateRect(
            "TooltipLayer",
            parent,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        layer.offsetMin = Vector2.zero;
        layer.offsetMax = Vector2.zero;
        var layout = layer.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        layer.SetAsLastSibling();
        return layer;
    }

    private static void CreateTooltipCard(Transform parent, string cardId)
    {
        var card = CreateLayoutObject("Card_" + cardId, parent);
        var layout = card.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var iconHost = CreateLayoutObject("Icon", card.transform);
        var iconElement = iconHost.AddComponent<LayoutElement>();
        iconElement.minWidth = 64f;
        iconElement.preferredWidth = 64f;
        iconElement.minHeight = 64f;
        iconElement.preferredHeight = 64f;
        TerriasUiBuilder.ApplyPanelImage(iconHost, TerriasUiSprites.Panel("[EndlessSeaIntro]"), new Color(0.02f, 0.025f, 0.08f, 0.82f));

        var sprite = TryLoadCardIcon(cardId);
        if (sprite != null)
        {
            var iconRect = TerriasUiBuilder.CreateRect(
                "Image",
                iconHost.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(64f, 64f));
            var image = iconRect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        var name = AddTextBlock(card.transform, EndlessSeaStarterDeckCatalog.CardDisplayName(cardId), 13, TextAnchor.MiddleLeft, SoftText, 64f, 1f);
        name.resizeTextForBestFit = true;
        name.resizeTextMinSize = 10;
        name.resizeTextMaxSize = 13;
    }

    private static void PositionTooltip(RectTransform tooltip, RectTransform source)
    {
        if (activeTooltipLayer == null)
        {
            return;
        }

        var parent = activeTooltipLayer;
        var corners = new Vector3[4];
        source.GetWorldCorners(corners);
        var topLeftScreen = RectTransformUtility.WorldToScreenPoint(null, corners[1]);
        var topRightScreen = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, topRightScreen, null, out var topRight);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, topLeftScreen, null, out var topLeft);

        var pos = topRight + new Vector2(16f, -4f);
        var bounds = parent.rect;
        if (pos.x + TooltipWidth > bounds.xMax - 12f)
        {
            pos = topLeft + new Vector2(-TooltipWidth - 16f, -4f);
        }

        pos.x = Mathf.Clamp(pos.x, bounds.xMin + 12f, bounds.xMax - TooltipWidth - 12f);
        pos.y = Mathf.Clamp(pos.y, bounds.yMin + TooltipHeight + 12f, bounds.yMax - 12f);
        tooltip.anchoredPosition = pos;
    }

    private static void HideTooltip()
    {
        if (activeTooltip != null)
        {
            UnityEngine.Object.Destroy(activeTooltip);
            activeTooltip = null;
        }
    }

    private static void ApplyStarterDeck(RoleTable roleTable, EndlessSeaStarterDeckProfile profile)
    {
        try
        {
            if (IsApplied(roleTable))
            {
                ClosePanel();
                return;
            }

            HideTooltip();
            UpdateHint("正在应用：" + profile.Title + "...");
            SetDeckButtonsInteractable(false);
            if (profile.CardIds.Count != EndlessSeaStarterDeckCatalog.DeckSize)
            {
                UpdateHint("开局卡组配置数量异常，请检查硬编码卡组。");
                SetDeckButtonsInteractable(true);
                return;
            }

            var invalidCards = profile.CardIds
                .Where(EndlessSeaStarterDeckCatalog.IsInvalidCardId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (invalidCards.Count > 0)
            {
                UpdateHint("开局卡组存在无效卡牌：" + string.Join(" / ", invalidCards));
                TerriasLog.Warn("[EndlessSeaIntro] rejected invalid starter deck "
                    + profile.Id
                    + ": "
                    + string.Join("|", invalidCards));
                SetDeckButtonsInteractable(true);
                return;
            }

            if (!EndlessSeaCardAffixService.RunWithStarterDeckSuppressed(() =>
                    StarterDeckArbiterRuntime.ApplyDeck(
                        roleTable,
                        profile.CardIds,
                        CreateClaim(profile),
                        EndlessSeaStarterDeckCatalog.IsInvalidCardId,
                        sync: true)))
            {
                UpdateHint("卡组写入失败，请重新选择。");
                SetDeckButtonsInteractable(true);
                return;
            }

            MarkApplied(roleTable, profile.Id);
            EndlessSeaCardAffixService.MarkStarterDeckBaseline(roleTable, "EndlessSeaIntroBoard.ApplyStarterDeck");
            SetSaveValue(TerriasIds.EndlessSeaIntroSeenKey, "1");
            SetSaveValue(TerriasIds.EndlessSeaStarterDeckAppliedKey, "1");
            SetSaveValue(TerriasIds.EndlessSeaStarterDeckModeKey, profile.Id);
            EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.MapPlanning, "EndlessSeaIntroBoard.ApplyStarterDeck");
            ClosePanel();
            EndlessAbyssEvacuationButtonRuntime.Refresh();
            TerriasFrameDispatcher.RunOnceNextFrame(
                "EndlessAbyss.MapPanels.AfterStarterDeck",
                () => EndlessSeaModeRuntime.TryOpenAbyssMapPanels("EndlessSeaIntroBoard.ApplyStarterDeck"));
            UIManager.Instance?.ShowTip(TerriasIds.EndlessAbyssTitle + "\u5f00\u5c40\u5361\u7ec4\uff1a" + profile.Title);
            TerriasLog.Info("[EndlessSeaIntro] applied starter deck "
                + profile.Id
                + "; cards="
                + string.Join("|", profile.CardIds));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea starter deck apply failed", ex);
            UpdateHint("卡组写入异常，请查看日志。");
            SetDeckButtonsInteractable(true);
        }
    }

    private static StarterDeckClaim CreateClaim(EndlessSeaStarterDeckProfile profile)
    {
        return new StarterDeckClaim
        {
            Owner = TerriasIds.StarterDeckOwnerEndlessSea,
            Scope = TerriasIds.EndlessSeaModeKey,
            ModeId = "Terrias.EndlessSea",
            Source = "intro-board",
            State = StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = TerriasIds.EndlessSeaStarterDeckAppliedKey,
            AppliedModeKey = TerriasIds.EndlessSeaStarterDeckModeKey,
            AppliedMode = profile.Id,
            LegacyMode = "terrias-endless-sea",
            DeckSize = EndlessSeaStarterDeckCatalog.DeckSize,
            SourceName = "Terrias.EndlessSea.IntroBoard"
        };
    }

    private static bool IsApplied(RoleTable roleTable)
    {
        return StarterDeckArbiterRuntime.HasApplied(
                roleTable,
                TerriasIds.EndlessSeaStarterDeckAppliedKey,
                TerriasIds.StarterDeckOwnerEndlessSea)
            || roleTable.SpecialVarMap != null
            && roleTable.SpecialVarMap.TryGetValue(TerriasIds.EndlessSeaStarterDeckAppliedKey, out var value)
            && value == "1";
    }

    private static void MarkApplied(RoleTable roleTable, string mode)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[TerriasIds.EndlessSeaIntroSeenKey] = "1";
        roleTable.SpecialVarMap[TerriasIds.EndlessSeaStarterDeckAppliedKey] = "1";
        roleTable.SpecialVarMap[TerriasIds.EndlessSeaStarterDeckModeKey] = mode;
        roleTable.SpecialVarMap[TerriasIds.StarterDeckOwnerKey] = TerriasIds.StarterDeckOwnerEndlessSea;
        roleTable.SpecialVarMap[TerriasIds.StarterDeckScopeKey] = TerriasIds.EndlessSeaModeKey;
        roleTable.SpecialVarMap[TerriasIds.StarterDeckStateKey] = TerriasIds.StarterDeckStateApplied;
    }

    private static Sprite? TryLoadPackCover(EndlessSeaStarterDeckProfile profile)
    {
        var key = profile.Id + "|" + profile.CoverPackId + "|" + profile.RequiredPackId;
        if (packCoverCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        foreach (var packId in CoverCandidates(profile))
        {
            sprite = TryLoadPackCoverByPackId(packId);
            if (sprite != null)
            {
                break;
            }
        }

        packCoverCache[key] = sprite;
        return sprite;
    }

    private static IEnumerable<string> CoverCandidates(EndlessSeaStarterDeckProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.CoverPackId))
        {
            yield return profile.CoverPackId;
        }

        if (!string.IsNullOrWhiteSpace(profile.RequiredPackId)
            && !string.Equals(profile.RequiredPackId, profile.CoverPackId, StringComparison.OrdinalIgnoreCase))
        {
            yield return profile.RequiredPackId;
        }

        yield return DefaultCoverPackId;
    }

    private static Sprite? TryLoadPackCoverByPackId(string packId)
    {
        try
        {
            var data = TerriasConfigIndex.Row(DataType.CardPack, packId);
            var iconPath = DictionaryUtil.Get(data, "Icon");
            return string.IsNullOrWhiteSpace(iconPath) ? null : TerriasResourceCache.Load<Sprite>(iconPath, true);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[EndlessSeaIntro] failed to load pack cover for " + packId + ": " + ex.Message);
            return null;
        }
    }

    private static Sprite? TryLoadCardIcon(string cardId)
    {
        if (cardIconCache.TryGetValue(cardId, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            var data = TerriasConfigIndex.Row(DataType.Card, cardId);
            var iconPath = DictionaryUtil.Get(data, "Icon");
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                sprite = TerriasResourceCache.Load<Sprite>(iconPath, true);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaIntro] failed to load card icon for " + cardId + ": " + ex.Message);
        }

        cardIconCache[cardId] = sprite;
        return sprite;
    }

    private static void UpdateHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static void SetDeckButtonsInteractable(bool interactable)
    {
        foreach (var button in deckButtons)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }

    private static void ClosePanel()
    {
        ClosePanel("EndlessSeaIntro.ClosePanel");
    }

    public static void ClosePanel(string source)
    {
        HideTooltip();
        TerriasModalHost.Close(ref activePanel, source, "[EndlessSeaIntro]");
        activeTooltipLayer = null;
        hintText = null;
        deckButtons.Clear();
        deckSelectedFrames.Clear();
        TerriasTransientUiRegistry.Unregister("EndlessSeaIntro");
    }

    private static Text AddTextFill(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var rect = TerriasUiBuilder.CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var text = ConfigureText(rect.gameObject, value, fontSize, anchor, color);
        text.raycastTarget = false;
        return text;
    }

    private static Text AddTextBlock(
        Transform parent,
        string value,
        int fontSize,
        TextAnchor anchor,
        Color color,
        float preferredHeight,
        float flexibleWidth = 0f,
        float preferredWidth = 0f)
    {
        var go = CreateLayoutObject("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = preferredHeight;
        element.preferredHeight = preferredHeight;
        if (flexibleWidth > 0f)
        {
            element.flexibleWidth = flexibleWidth;
        }

        if (preferredWidth > 0f)
        {
            element.minWidth = preferredWidth;
            element.preferredWidth = preferredWidth;
        }

        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text AddAutoTextBlock(
        Transform parent,
        string value,
        int fontSize,
        TextAnchor anchor,
        Color color)
    {
        var go = CreateLayoutObject("Text", parent);
        var text = ConfigureText(go, value, fontSize, anchor, color);
        text.verticalOverflow = VerticalWrapMode.Overflow;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return text;
    }

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var text = go.AddComponent<Text>();
        text.text = value;
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.supportRichText = true;
        return text;
    }

    private static Shadow AddTextShadow(Text text, Color color, Vector2 distance)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        return shadow;
    }

    private static GameObject CreateLayoutObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        return go;
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var available = new Vector2(Screen.width, Screen.height);
        if (parent is RectTransform rect && rect.rect.width > 0f && rect.rect.height > 0f)
        {
            available = rect.rect.size;
        }

        var width = Mathf.Min(1080f, Mathf.Max(820f, available.x - 80f));
        var height = Mathf.Min(740f, Mathf.Max(640f, available.y - 64f));
        return new Vector2(width, height);
    }

    private static float ResolveThemeGridContentWidth(Transform parent)
    {
        var width = 540f;
        if (parent is RectTransform rect && rect.rect.width > 0f)
        {
            width = rect.rect.width - 48f;
        }

        return Mathf.Max(360f, width);
    }

    private static void SetSaveValue(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value);
        }
        catch
        {
            GameSaveManager.GetNowSave()?.SetValue(key, value);
        }
    }
}
