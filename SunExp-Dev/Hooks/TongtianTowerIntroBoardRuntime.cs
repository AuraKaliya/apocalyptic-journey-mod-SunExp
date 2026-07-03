using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Data.Save;
using StarterDeckArbiter.Shared;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerIntroBoardRuntime
{
    private const float ButtonHeight = 38f;
    private static readonly Color Gold = new(0.86f, 0.73f, 0.38f);
    private static readonly Color PaleGold = new(0.96f, 0.89f, 0.64f);
    private static readonly Color SoftText = new(0.86f, 0.89f, 0.95f);
    private static readonly Color DeepBlue = new(0.02f, 0.025f, 0.13f, 0.98f);
    private static readonly Color AreaTint = new(0.018f, 0.02f, 0.105f, 0.96f);
    private static readonly Color DeckTint = new(0.055f, 0.065f, 0.16f, 0.97f);
    private static readonly Color DeckHoverTint = new(0.1f, 0.09f, 0.19f, 0.98f);
    private static GameObject? activePanel;
    private static Text? hintText;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "MapManager.MapUIStart", TryShowIntroBoard);
        RegisterAfter(modConfig, "NormalMapManager.MapUIStart", TryShowIntroBoard);
        RegisterAfter(modConfig, "MapSelectUI.Start", TryShowIntroBoard);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian tower intro " + message));
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

            var parent = SunExpModalHost.ModalParent();
            if (parent == null)
            {
                SunExpLog.Warn("[TongtianTowerIntro] skipped: UI canvas unavailable from " + source + ".");
                return false;
            }

            SunExpLog.Info("[TongtianTowerIntro] opening intro board from " + source + ".");
            ShowIntroBoard(roleTable, parent);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower intro board failed", ex);
            return false;
        }
    }

    private static bool ShouldShow()
    {
        return TongtianTowerModeRuntime.IsTongtianTowerRun()
            && TongtianTowerModeRuntime.CurrentFloor() == 1
            && GameSaveManager.GetValue<string>(SunExpIds.TongtianTowerIntroSeenKey) != "1"
            && GameSaveManager.GetValue<string>(SunExpIds.TongtianTowerStarterDeckAppliedKey) != "1";
    }

    private static void ShowIntroBoard(RoleTable roleTable, Transform parent)
    {
        activePanel = SunExpModalHost.CreateFullscreenRoot(
            "SunExpTongtianTowerIntroBoard",
            parent,
            new Color(0f, 0f, 0f, 0.78f));

        var windowSize = ResolveWindowSize(parent);
        var windowRect = SunExpUiBuilder.CreateRect(
            "Board",
            activePanel.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            windowSize);
        var window = windowRect.gameObject;
        SunExpUiBuilder.ApplyPanelImage(window, SunExpUiSprites.Panel("[TongtianTowerIntro]"), DeepBlue, true);

        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 22);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateHeader(window.transform);
        CreateDivider(window.transform);
        CreateBodyScroll(window.transform);
        CreateDeckChoiceArea(window.transform, roleTable);
        CreateFooter(window.transform);
        Canvas.ForceUpdateCanvases();
    }

    private static void CreateHeader(Transform parent)
    {
        var header = CreateLayoutObject("Header", parent);
        var element = header.AddComponent<LayoutElement>();
        element.preferredHeight = 66f;
        element.minHeight = 66f;

        var title = AddTextFill(header.transform, SunExpIds.TongtianTowerTitle, 30, TextAnchor.UpperCenter, Gold);
        title.fontStyle = FontStyle.Bold;

        var subtitle = AddTextFill(header.transform, "无限爬塔模式  第1层作战简报", 16, TextAnchor.LowerCenter, SoftText);
        var subtitleRect = subtitle.GetComponent<RectTransform>();
        subtitleRect.offsetMin = new Vector2(0f, 2f);
        subtitleRect.offsetMax = new Vector2(0f, -34f);
    }

    private static void CreateDivider(Transform parent)
    {
        var divider = CreateLayoutObject("Divider", parent);
        var element = divider.AddComponent<LayoutElement>();
        element.preferredHeight = 2f;
        element.minHeight = 2f;
        SunExpUiBuilder.ApplyPanelImage(divider, null, new Color(Gold.r, Gold.g, Gold.b, 0.85f));
    }

    private static void CreateBodyScroll(Transform parent)
    {
        var root = CreateLayoutObject("BodyScroll", parent);
        var element = root.AddComponent<LayoutElement>();
        element.minHeight = 220f;
        element.flexibleHeight = 1f;
        SunExpUiBuilder.ApplyPanelImage(root, null, AreaTint, true);

        var viewportRect = SunExpUiBuilder.CreateRect(
            "Viewport",
            root.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        viewportRect.offsetMin = new Vector2(14f, 12f);
        viewportRect.offsetMax = new Vector2(-14f, -12f);
        var viewportImage = viewportRect.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.05f);
        viewportImage.raycastTarget = true;
        viewportRect.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var contentRect = SunExpUiBuilder.CreateRect(
            "Content",
            viewportRect,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, 0f));
        contentRect.offsetMin = new Vector2(8f, 0f);
        contentRect.offsetMax = new Vector2(-8f, 0f);

        var contentLayout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(6, 12, 4, 4);
        contentLayout.spacing = 10f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentRect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bodyText = AddTextBlock(
            contentRect,
            TongtianTowerRichTextSanitizer.Sanitize(IntroBody()),
            17,
            TextAnchor.UpperLeft,
            SoftText,
            420f,
            1f);
        bodyText.supportRichText = true;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        bodyText.lineSpacing = 1.05f;
        bodyText.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.verticalNormalizedPosition = 1f;
    }

    private static void CreateDeckChoiceArea(Transform parent, RoleTable roleTable)
    {
        var area = CreateLayoutObject("StarterDeckChoices", parent);
        var element = area.AddComponent<LayoutElement>();
        element.minHeight = 178f;
        element.preferredHeight = 178f;

        var layout = area.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        foreach (var profile in TongtianTowerStarterDeckCatalog.Profiles)
        {
            CreateDeckButton(area.transform, roleTable, profile);
        }
    }

    private static void CreateDeckButton(Transform parent, RoleTable roleTable, TongtianTowerStarterDeckProfile profile)
    {
        var panel = CreateLayoutObject("Deck_" + profile.Id, parent);
        var element = panel.AddComponent<LayoutElement>();
        element.minWidth = 210f;
        element.flexibleWidth = 1f;

        var image = SunExpUiBuilder.ApplyPanelImage(panel, SunExpUiSprites.Button("[TongtianTowerIntro]"), DeckTint, true);
        var button = panel.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = DeckHoverTint,
            pressedColor = new Color(0.78f, 0.74f, 0.64f, 1f),
            selectedColor = Color.white,
            disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        button.onClick.AddListener(() => ApplyStarterDeck(roleTable, profile));

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 7f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var title = AddTextBlock(panel.transform, profile.Title, 20, TextAnchor.MiddleCenter, Gold, 27f, 0f);
        title.fontStyle = FontStyle.Bold;
        AddTextBlock(panel.transform, profile.Subtitle, 14, TextAnchor.MiddleCenter, PaleGold, 22f, 0f);
        AddTextBlock(panel.transform, profile.Description, 13, TextAnchor.UpperLeft, SoftText, 43f, 0f);
        var preview = AddTextBlock(panel.transform, profile.Preview, 12, TextAnchor.MiddleLeft, new Color(0.77f, 0.83f, 0.96f), 32f, 0f);
        preview.supportRichText = false;
    }

    private static void CreateFooter(Transform parent)
    {
        var footer = CreateLayoutObject("Footer", parent);
        var element = footer.AddComponent<LayoutElement>();
        element.preferredHeight = ButtonHeight;
        element.minHeight = ButtonHeight;

        hintText = AddTextFill(footer.transform, "选择一套开局卡组后开始第一层。奖励牌会获得焚毁限制，请持续补充卡组。", 15, TextAnchor.MiddleCenter, PaleGold);
    }

    private static void ApplyStarterDeck(RoleTable roleTable, TongtianTowerStarterDeckProfile profile)
    {
        try
        {
            if (IsApplied(roleTable))
            {
                ClosePanel();
                return;
            }

            if (profile.CardIds.Count != TongtianTowerStarterDeckCatalog.DeckSize)
            {
                UpdateHint("开局卡组配置数量异常，请检查硬编码卡组。");
                return;
            }

            var invalidCards = profile.CardIds
                .Where(TongtianTowerStarterDeckCatalog.IsInvalidCardId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (invalidCards.Count > 0)
            {
                UpdateHint("开局卡组存在无效卡牌：" + string.Join(" / ", invalidCards));
                return;
            }

            if (!StarterDeckArbiterRuntime.ApplyDeck(
                    roleTable,
                    profile.CardIds,
                    CreateClaim(profile),
                    TongtianTowerStarterDeckCatalog.IsInvalidCardId,
                    sync: true))
            {
                UpdateHint("卡组写入失败，请重新选择。");
                return;
            }

            MarkApplied(roleTable, profile.Id);
            SetSaveValue(SunExpIds.TongtianTowerIntroSeenKey, "1");
            SetSaveValue(SunExpIds.TongtianTowerStarterDeckAppliedKey, "1");
            SetSaveValue(SunExpIds.TongtianTowerStarterDeckModeKey, profile.Id);
            ClosePanel();
            UIManager.Instance?.ShowTip("通天之塔开局卡组：" + profile.Title);
            SunExpLog.Info("[TongtianTowerIntro] applied starter deck "
                + profile.Id
                + "; cards="
                + string.Join("|", profile.CardIds));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower starter deck apply failed", ex);
            UpdateHint("卡组写入异常，请查看日志。");
        }
    }

    private static StarterDeckClaim CreateClaim(TongtianTowerStarterDeckProfile profile)
    {
        return new StarterDeckClaim
        {
            Owner = SunExpIds.StarterDeckOwnerTongtianTower,
            Scope = SunExpIds.TongtianTowerModeKey,
            ModeId = "SunExp.TongtianTower",
            Source = "intro-board",
            State = StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = SunExpIds.TongtianTowerStarterDeckAppliedKey,
            AppliedModeKey = SunExpIds.TongtianTowerStarterDeckModeKey,
            AppliedMode = profile.Id,
            LegacyMode = "sunexp-tongtian-tower",
            DeckSize = TongtianTowerStarterDeckCatalog.DeckSize,
            SourceName = "SunExp.TongtianTower.IntroBoard"
        };
    }

    private static bool IsApplied(RoleTable roleTable)
    {
        return StarterDeckArbiterRuntime.HasApplied(
                roleTable,
                SunExpIds.TongtianTowerStarterDeckAppliedKey,
                SunExpIds.StarterDeckOwnerTongtianTower)
            || roleTable.SpecialVarMap != null
            && roleTable.SpecialVarMap.TryGetValue(SunExpIds.TongtianTowerStarterDeckAppliedKey, out var value)
            && value == "1";
    }

    private static void MarkApplied(RoleTable roleTable, string mode)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[SunExpIds.TongtianTowerIntroSeenKey] = "1";
        roleTable.SpecialVarMap[SunExpIds.TongtianTowerStarterDeckAppliedKey] = "1";
        roleTable.SpecialVarMap[SunExpIds.TongtianTowerStarterDeckModeKey] = mode;
        roleTable.SpecialVarMap[SunExpIds.StarterDeckOwnerKey] = SunExpIds.StarterDeckOwnerTongtianTower;
        roleTable.SpecialVarMap[SunExpIds.StarterDeckScopeKey] = SunExpIds.TongtianTowerModeKey;
        roleTable.SpecialVarMap[SunExpIds.StarterDeckStateKey] = SunExpIds.StarterDeckStateApplied;
    }

    private static void UpdateHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static void ClosePanel()
    {
        SunExpModalHost.Close(ref activePanel, "TongtianTowerIntro.ClosePanel", "[TongtianTowerIntro]");
        hintText = null;
    }

    private static string IntroBody()
    {
        return "<b>玩法目标</b>\n"
            + "通天之塔是无限爬塔模式。每一层会生成一组全解锁地图节点，你可以自由选择路线，最后一个节点固定为<color=#FFD36A>首领</color>。\n\n"
            + "<b>地图规则</b>\n"
            + "本模式不提供事件节点，只会出现<color=#FFD36A>怪物</color>、<color=#FFD36A>首领</color>、<color=#FFD36A>建筑</color>。每层最多只有一个建筑节点，并按层数循环建筑槽位。\n\n"
            + "<b>成长压力</b>\n"
            + "进入更高层后，战斗基础数据会自动提升。奖励牌数量更多，但通天之塔奖励牌会附带焚毁限制，防止无限资源滚雪球。\n\n"
            + "<b>卡组运营</b>\n"
            + "请把奖励牌视为消耗品。不要只追求单次爆发，持续补牌、保留低费启动、控制高费密度，会让高层更稳定。";
    }

    private static Text AddTextFill(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var rect = SunExpUiBuilder.CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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
        float flexibleHeight,
        float preferredWidth = 0f)
    {
        var go = CreateLayoutObject("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = preferredHeight;
        element.preferredHeight = preferredHeight;
        element.flexibleHeight = flexibleHeight;
        if (preferredWidth > 0f)
        {
            element.minWidth = preferredWidth;
            element.preferredWidth = preferredWidth;
        }

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var text = go.AddComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
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

        var width = Mathf.Min(1120f, Mathf.Max(760f, available.x - 64f));
        var height = Mathf.Min(800f, Mathf.Max(660f, available.y - 40f));
        return new Vector2(width, height);
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
