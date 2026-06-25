using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryModeRuntime
{
    private const string EntryObjectName = "SunExp_SolarMemoryMode";
    private const string PackWindowName = "SunExp_SolarMemoryPackWindow";
    private const string EntryTitleSpritePath = "Mods/SunExp/ModResource/Images/UI/solar_memory_title_c.png";
    private const string EntryHighlightedTitleSpritePath = "Mods/SunExp/ModResource/Images/UI/solar_memory_title_c_h.png";
    private const string SolarMemoryEventMapCardTexturePath = "Mods/SunExp/ModResource/Images/MapNode/日耀回忆-事件.png";
    private const float EntryTitleArtHeightRatio = 0.735f;
    private const int SolarMemoryOpeningSlotIndex = 0;
    private const int SolarMemoryMidLayerSlotIndex = 3;
    private static readonly Color PanelColor = new(0.11f, 0.09f, 0.08f, 0.96f);
    private static readonly Color AccentColor = new(0.84f, 0.55f, 0.2f, 1f);
    private static readonly Color RowNormalColor = new(0.18f, 0.16f, 0.14f, 0.92f);
    private static readonly Color RowSelectedColor = new(0.38f, 0.24f, 0.11f, 0.96f);
    private static readonly Color ButtonColor = new(0.29f, 0.21f, 0.16f, 0.96f);
    private static Font? cachedFont;
    private static Sprite? entryTitleSprite;
    private static Sprite? entryHighlightedTitleSprite;
    private static bool entryTitleSpriteLoadAttempted;
    private static bool entryHighlightedTitleSpriteLoadAttempted;
    private static bool handlingSolarMemoryFightAbort;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterModeChoiceEntry();
        ModeChoiceLayoutRuntime.Initialize(modConfig);
        RegisterAfter(modConfig, "MapSelectUI.DataUpdate", ApplySolarMemoryLayerTitle);
        RegisterBefore(modConfig, "GameConfigManager.CardPackCheck", FilterSolarMemoryCardPackCheck);
        RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RewriteSolarMemoryMap);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcNextMap", EnsureSolarMemoryCurrentNodeBeforeNextMap);
        RegisterAfter(modConfig, "MapManager.RpcNextMap", SyncSolarMemoryClientLastNodeAfterNextMap);
        RegisterBefore(modConfig, "NormalMapManager.MapItemInit", SettleLegacySolarFinaleBeforeMapItems);
        RegisterAfter(modConfig, "NormalMapManager.MapItemInit", ApplySolarMemoryFixedSlotsAfterMapItems);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ReapplySolarMemoryFixedSlotLocks);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", SettleSolarMemoryBossAfterWin);
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", PrepareSolarMemoryFightAbort);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", SettleSolarMemoryFightAbort);
        RegisterAfter(modConfig, "Fight_Loss.Init", SettleSolarMemoryFightLoss);
        RegisterBefore(modConfig, "NormalMapManager.ReadyToChangeMap", FinishSolarMemoryAfterFinalLayer);
    }

    private static void RegisterModeChoiceEntry()
    {
        ModeChoiceEntryRegistry.Register(new ModeChoiceEntryDefinition(
            EntryObjectName,
            "SublimationMode",
            100,
            ConfigureRegisteredEntry,
            modeChoice => SolarMemoryRunLauncher.Start(modeChoice, InitialPackSelection().ToList()),
            SunExpIds.SolarMemoryTitle));
    }

    public static void OpenOriginWindow()
    {
        try
        {
            SolarMemoryPreparationRuntime.StartOrResume();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory origin window failed", ex);
        }
    }

    public static void OpenBlessingWindow()
    {
        try
        {
            SolarMemoryPreparationRuntime.StartOrResume();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory blessing window failed", ex);
        }
    }

    public static void OpenDeckWindow()
    {
        try
        {
            if (RoleTable.Instance == null)
            {
                return;
            }

            if (!SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryDeckConfiguredKey))
            {
                ClearSolarMemoryReservePool();
            }
            else
            {
                SanitizeSolarMemoryRoleCards(RoleTable.Instance, "OpenDeckWindow");
            }

            var ui = UIManager.Instance.ShowUI<OutDeckUI>("OutDeckUI", true);
            ui.SetRole(new OutDeckUIData(RoleTable.Instance));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory deck window failed", ex);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Solar memory " + message));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Solar memory " + message));
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
            SunExpLog.Error("Solar memory entry injection failed", ex);
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
        SetTmpText(entry.Find("Text/Text"), SunExpIds.SolarMemoryDescription + "\n" + SunExpIds.SolarMemorySubtitle);
        var hasTitleSprites = ConfigureEntryTitleSprites(entry);

        var title = entry.Find("SunExpTitle");
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
            var go = new GameObject("SunExpTitle", typeof(RectTransform));
            title = go.transform;
            title.SetParent(entry, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 0.58f);
            rect.anchorMax = new Vector2(0.92f, 0.88f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.AddComponent<Text>();
            ConfigureText(text, SunExpIds.SolarMemoryTitle, 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        }
        else
        {
            var text = title.GetComponent<Text>();
            if (text != null)
            {
                text.text = SunExpIds.SolarMemoryTitle;
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
        if (entryTitleSprite != null)
        {
            return entryTitleSprite;
        }

        if (entryTitleSpriteLoadAttempted)
        {
            return null;
        }

        entryTitleSpriteLoadAttempted = true;
        entryTitleSprite = LoadEntrySprite(EntryTitleSpritePath);
        return entryTitleSprite;
    }

    private static Sprite? GetEntryHighlightedTitleSprite()
    {
        if (entryHighlightedTitleSprite != null)
        {
            return entryHighlightedTitleSprite;
        }

        if (entryHighlightedTitleSpriteLoadAttempted)
        {
            return null;
        }

        entryHighlightedTitleSpriteLoadAttempted = true;
        entryHighlightedTitleSprite = LoadEntrySprite(EntryHighlightedTitleSpritePath);
        return entryHighlightedTitleSprite;
    }

    private static Sprite? LoadEntrySprite(string path)
    {
        try
        {
            var sprite = ResourceLoader.Load<Sprite>(path, true);
            if (sprite == null)
            {
                SunExpLog.Warn("[SolarMemoryMode] entry sprite missing: " + path);
                return null;
            }

            var trimmed = TrimTransparentPadding(sprite) ?? sprite;
            return CropEntryTitleArt(trimmed) ?? trimmed;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMode] failed to load entry sprite " + path + ": " + ex.Message);
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
            SunExpLog.Warn("[SolarMemoryMode] failed to trim entry sprite: " + ex.Message);
            return sprite;
        }
    }

    private static Sprite? CropEntryTitleArt(Sprite sprite)
    {
        try
        {
            var rect = sprite.rect;
            var height = Mathf.Max(1f, rect.height * EntryTitleArtHeightRatio);
            var cropped = new Rect(rect.x, rect.y + rect.height - height, rect.width, height);
            return Sprite.Create(sprite.texture, cropped, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMode] failed to crop entry title sprite: " + ex.Message);
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
            switchButton.onClick.AddListener(new UnityAction(() => SolarMemoryRunLauncher.Start(modeChoice, InitialPackSelection().ToList())));
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
            button.onClick.AddListener(new UnityAction(() => SolarMemoryRunLauncher.Start(modeChoice, InitialPackSelection().ToList())));
        }
    }

    private static void OpenPackWindow(ModeChoiceUI modeChoice)
    {
        try
        {
            CloseExistingPackWindow();

            var parent = UIManager.Instance?.upperCanvasTf ?? UIManager.Instance?.canvasTf ?? modeChoice.transform;
            var root = CreateRect(PackWindowName, parent);
            Stretch(root);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            var panel = CreateRect("Panel", root);
            panel.sizeDelta = new Vector2(920f, 650f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = PanelColor;

            AddText(panel, "Title", SunExpIds.SolarMemoryTitle, 34, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white,
                new Vector2(40f, -54f), new Vector2(520f, 56f));
            AddText(panel, "Subtitle", SunExpIds.SolarMemoryDescription + " / " + SunExpIds.SolarMemorySubtitle, 19, FontStyle.Normal,
                TextAnchor.MiddleRight, new Color(1f, 0.86f, 0.64f, 1f), new Vector2(400f, -58f), new Vector2(470f, 42f));

            var selected = InitialPackSelection();
            var rows = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            var summary = AddText(panel, "Summary", "", 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.94f, 0.9f, 0.82f, 1f),
                new Vector2(40f, -105f), new Vector2(840f, 34f));

            var scroll = CreatePackScroll(panel);
            var content = scroll.content;
            foreach (var pack in VisibleCardPacks())
            {
                var row = CreatePackRow(content, pack, selected, rows, summary);
                row.SetParent(content, false);
            }

            RefreshPackRows(rows, selected, summary);
            AddButton(panel, "SelectAll", "全选", new Vector2(40f, 38f), new Vector2(120f, 44f), () =>
            {
                selected.Clear();
                foreach (var pack in VisibleCardPacks())
                {
                    selected.Add(pack["Id"]);
                }
                RefreshPackRows(rows, selected, summary);
            });
            AddButton(panel, "UseCurrent", "恢复当前", new Vector2(180f, 38f), new Vector2(150f, 44f), () =>
            {
                selected.Clear();
                selected.UnionWith(InitialPackSelection());
                RefreshPackRows(rows, selected, summary);
            });
            AddButton(panel, "Cancel", "取消", new Vector2(610f, 38f), new Vector2(120f, 44f), CloseExistingPackWindow);
            AddButton(panel, "Start", "进入日耀回忆", new Vector2(750f, 38f), new Vector2(130f, 44f), () =>
            {
                if (selected.Count == 0)
                {
                    UIManager.Instance?.ShowTip("至少选择一个卡包", null);
                    return;
                }

                CloseExistingPackWindow();
                SolarMemoryRunLauncher.Start(modeChoice, selected.ToList());
            });
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory pack window failed", ex);
        }
    }

    private static ScrollRect CreatePackScroll(RectTransform panel)
    {
        var viewport = CreateRect("Viewport", panel);
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.offsetMin = new Vector2(40f, 100f);
        viewport.offsetMax = new Vector2(-40f, -140f);
        viewport.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.04f, 0.035f, 0.58f);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 24f;
        return scrollRect;
    }

    private static RectTransform CreatePackRow(RectTransform content, Dictionary<string, string> pack, HashSet<string> selected,
        Dictionary<string, Image> rows, Text summary)
    {
        var id = pack["Id"];
        var row = CreateRect("Pack_" + id, content);
        row.sizeDelta = new Vector2(0f, 58f);
        var layout = row.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 58f;
        layout.preferredHeight = 58f;
        var image = row.gameObject.AddComponent<Image>();
        rows[id] = image;
        var button = row.gameObject.AddComponent<Button>();
        button.onClick.AddListener(new UnityAction(() =>
        {
            if (!selected.Add(id))
            {
                selected.Remove(id);
            }

            RefreshPackRows(rows, selected, summary);
        }));

        var counts = PackCounts(id);
        var type = pack.TryGetValue("Type", out var typeValue) ? typeValue : "";
        var name = PackName(pack);
        AddText(row, "Name", name, 20, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, new Vector2(18f, 0f), new Vector2(420f, 58f));
        AddText(row, "Meta", type + "  " + counts, 17, FontStyle.Normal, TextAnchor.MiddleRight, new Color(0.93f, 0.84f, 0.7f, 1f),
            new Vector2(450f, 0f), new Vector2(360f, 58f));
        return row;
    }

    private static void CaptureSolarMemoryGenerationState(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not NormalMapManager manager)
            {
                SolarMemoryMapNodePoolApplier.ResetGenerationCapture();
                return;
            }

            SolarMemoryMapNodePoolApplier.CaptureGenerationState(manager);
        }
        catch (Exception ex)
        {
            SolarMemoryMapNodePoolApplier.ResetGenerationCapture();
            SunExpLog.Error("Solar memory map generation capture failed", ex);
        }
    }

    private static void RewriteSolarMemoryMap(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not NormalMapManager manager)
            {
                return;
            }

            EnsureSolarMemoryMapState(manager, "NormalMapManager.GeneratrMap", true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map rewrite failed", ex);
        }
    }

    private static void EnsureSolarMemoryMapBeforeSelect(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            var mapManager = MapManager.Instance;
            if (mapManager?.ModeMapManager is NormalMapManager manager)
            {
                EnsureSolarMemoryMapState(manager, "MapSelectUI.ReadyToSelect", false);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory pre-select map repair failed", ex);
        }
    }

    private static void ApplySolarMemoryLayerTitle(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            var layer = CurrentSolarMemoryLayer();
            var title = SunExpIds.SolarMemoryLayerNames[Math.Max(0, Math.Min(SunExpIds.SolarMemoryLayerNames.Length - 1, layer))];
            SetTmpText(mapSelect.transform.Find("Title/Text/text"), title);

            var text = mapSelect.transform.Find("Title/Text/text")?.GetComponent<Text>();
            if (text != null)
            {
                text.text = title;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory layer title failed", ex);
        }
    }

    private static bool EnsureSolarMemoryMapState(NormalMapManager manager, string source, bool trimEventRecord)
    {
        return SolarMemoryMapNodePoolApplier.ApplyToCurrentLayer(manager, source, trimEventRecord);
    }

    private static void ApplySolarMemoryFixedSlotsAfterMapItems(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapSelectUI mapSelect)
            {
                return;
            }

            ApplySolarMemoryFixedSlots(mapSelect, manager, true, "NormalMapManager.MapItemInit");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory fixed slot apply failed", ex);
        }
    }

    private static void ReapplySolarMemoryFixedSlotLocks(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            if (!HasSolarMemoryCurrentNodeReady()
                && !TryRestoreSolarMemoryCurrentNodeFromMapManager("MapSelectUI.ShowMap"))
            {
                SunExpLog.Debug("[SolarMemoryMapLock] skipped fixed slot apply from MapSelectUI.ShowMap: current node is not ready.");
                return;
            }

            ApplySolarMemoryFixedSlots(mapSelect, MapManager.Instance?.ModeMapManager as NormalMapManager, false, "MapSelectUI.ShowMap");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory fixed slot lock repair failed", ex);
        }
    }

    private static void ApplySolarMemoryFixedSlots(MapSelectUI mapSelect, NormalMapManager? manager, bool sync, string source)
    {
        if (manager == null)
        {
            return;
        }

        var nodes = TryGetMapSelectNodes(mapSelect, source);
        if (nodes == null || nodes.Length == 0)
        {
            return;
        }

        var layer = SolarMemoryLayer(manager);
        var changed = false;
        foreach (var spec in FixedNodeSpecs(layer))
        {
            if (spec.SlotIndex < 0 || spec.SlotIndex >= nodes.Length)
            {
                continue;
            }

            var data = CreateFixedNodeData(spec);
            if (data == null)
            {
                continue;
            }

            var node = nodes[spec.SlotIndex];
            if (node == null)
            {
                continue;
            }

            node.data = data;
            node.NodeDice ??= Dice.Default;
            EnsureFixedSlotVisual(mapSelect, spec.SlotIndex, node, data);
            changed = true;
        }

        if (sync && changed)
        {
            mapSelect.SendNode();
            SunExpLog.Info("[SolarMemoryMapLock] fixed slots applied from " + source + "; layer=" + layer + ".");
        }
    }

    private static MapTree.Node[]? TryGetMapSelectNodes(MapSelectUI mapSelect, string source)
    {
        try
        {
            return mapSelect.GetNodes();
        }
        catch (Exception ex)
        {
            var message = "[SolarMemoryMapLock] skipped fixed slot apply from "
                + source
                + ": map nodes unavailable ("
                + ex.GetType().Name
                + ": "
                + ex.Message
                + ").";
            if (IsClientOnlyPlayer())
            {
                SunExpLog.Debug(message);
            }
            else
            {
                SunExpLog.Warn(message);
            }

            return null;
        }
    }

    private static IEnumerable<SolarMemoryFixedNodeSpec> FixedNodeSpecs(int layer)
    {
        var normalizedLayer = ClampSolarMemoryLayer(layer);
        yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryOpeningSlotIndex, normalizedLayer, SolarMemoryOpeningSlotIndex);

        switch (normalizedLayer)
        {
            case 0:
                yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryMapNodePoolFactory.EndingSlotIndex, normalizedLayer, SolarMemoryMapNodePoolFactory.EndingSlotIndex);
                break;
            case 1:
                yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryMidLayerSlotIndex, normalizedLayer, SolarMemoryMidLayerSlotIndex);
                yield return SolarMemoryFixedNodeSpec.Boss(SolarMemoryMapNodePoolFactory.EndingSlotIndex, normalizedLayer, SunExpIds.SolarBossOrbitMirrorMapId, SunExpIds.SolarBossOrbitMirrorLevelId);
                break;
            case 2:
                yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryMidLayerSlotIndex, normalizedLayer, SolarMemoryMidLayerSlotIndex);
                yield return SolarMemoryFixedNodeSpec.Boss(SolarMemoryMapNodePoolFactory.PenultimateSlotIndex, normalizedLayer, SunExpIds.SolarBossSecondSunMapId, SunExpIds.SolarBossSecondSunLevelId);
                yield return SolarMemoryFixedNodeSpec.Boss(SolarMemoryMapNodePoolFactory.EndingSlotIndex, normalizedLayer, SunExpIds.SolarBossSaintWunaMapId, SunExpIds.SolarBossSaintWunaLevelId);
                break;
        }
    }

    private static Dictionary<string, string>? CreateFixedNodeData(SolarMemoryFixedNodeSpec spec)
    {
        Dictionary<string, string>? row;
        if (spec.IsEvent)
        {
            var eventIndex = SolarMemoryEventIndex(spec.Layer, spec.MapSlotIndex);
            var mapId = SunExpIds.SolarMemoryMapIds[eventIndex];
            var shortMapId = SunExpIds.SolarMemoryShortMapIds[eventIndex];
            row = MapRow(mapId) ?? MapRow(shortMapId);
            var data = row == null ? new Dictionary<string, string>() : new Dictionary<string, string>(row);
            data["Id"] = mapId;
            data["Type"] = "Event";
            data["NodeId"] = SunExpIds.SolarMemoryFullEventIds[eventIndex];
            data["Level"] = "-1";
            return data;
        }

        row = MapRow(spec.MapId);
        if (row == null)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] missing map row: " + spec.MapId);
            return null;
        }

        var bossData = new Dictionary<string, string>(row);
        bossData["Id"] = spec.MapId;
        bossData["Type"] = "Fight";
        bossData["NodeId"] = spec.NodeId;
        bossData["Level"] = "-1";
        return bossData;
    }

    private static Dictionary<string, string>? MapRow(string mapId)
    {
        return Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, mapId)
            ?? Singleton<GameConfigManager>.Instance.GetTable(DataType.Map).Getlines()
                .FirstOrDefault(row => string.Equals(Field(row, "Id"), mapId, StringComparison.Ordinal)
                    || string.Equals("SunExp_sunexp_" + Field(row, "Id"), mapId, StringComparison.Ordinal));
    }

    private static void EnsureFixedSlotVisual(MapSelectUI mapSelect, int slotIndex, MapTree.Node node, IDictionary<string, string> data)
    {
        var slot = MapSlotTransform(mapSelect, slotIndex);
        var content = slot?.Find("Content");
        if (slot == null || content == null)
        {
            return;
        }

        foreach (var existing in content.GetComponentsInChildren<MapItem>(true))
        {
            UnityEngine.Object.Destroy(existing.gameObject);
        }

        var nullSlot = content.Find("Null");
        if (nullSlot != null)
        {
            nullSlot.gameObject.SetActive(false);
        }

        var prefabName = Field(data, "Type") + "Prefab";
        var template = mapSelect.transform.Find("MapSelect/" + prefabName);
        if (template == null)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] missing map prefab: " + prefabName);
            return;
        }

        var fixedItem = UnityEngine.Object.Instantiate(template.gameObject, content);
        fixedItem.name = prefabName;
        fixedItem.transform.localScale = Vector3.one;
        fixedItem.SetActive(true);

        var item = fixedItem.GetComponent<MapItem>() ?? fixedItem.AddComponent<MapItem>();
        item.Init(node);
        ApplyMapCardTexture(fixedItem.transform, data);

        if (fixedItem.TryGetComponent<ObjectGroup>(out var objectGroup))
        {
            objectGroup.blocksRaycasts = false;
        }

        var frame = slot.Find("Frame");
        if (frame != null && !HasChain(frame))
        {
            var chain = mapSelect.transform.Find("Chain");
            if (chain != null)
            {
                UnityEngine.Object.Instantiate(chain.gameObject, frame).SetActive(true);
            }
        }
    }

    private static Transform? MapSlotTransform(MapSelectUI mapSelect, int slotIndex)
    {
        var root = mapSelect.transform.Find("Map/NodeContent");
        if (root == null)
        {
            return null;
        }

        if (slotIndex == 0)
        {
            return root.Find("Start");
        }

        if (slotIndex == SolarMemoryMapNodePoolFactory.EndingSlotIndex)
        {
            return root.Find("End");
        }

        return root.Find("Node" + slotIndex);
    }

    private static bool HasChain(Transform frame)
    {
        foreach (Transform child in frame)
        {
            if (child.name.StartsWith("Chain", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyMapCardTexture(Transform item, IDictionary<string, string> data)
    {
        var background = item.Find("Front/background")?.GetComponent<MeshRenderer>();
        if (background == null)
        {
            return;
        }

        var type = Field(data, "Type");
        if (type == "Event")
        {
            var customTexture = LoadMapCardTexture(SolarMemoryEventMapCardTexturePath);
            if (customTexture != null)
            {
                var icon = item.Find("Front/icon");
                if (icon != null)
                {
                    icon.gameObject.SetActive(false);
                }

                background.material.mainTexture = customTexture;
                return;
            }

            background.material.mainTexture = ResourceLoader.Load<Texture>("Icon/CardTemplate/故事牌", true);
        }
        else if (type == "Build")
        {
            background.material.mainTexture = ResourceLoader.Load<Texture>("Icon/CardTemplate/建筑牌", true);
        }
    }

    private static Texture? LoadMapCardTexture(string path)
    {
        try
        {
            return ResourceLoader.Load<Texture>(path, true);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] failed to load map card texture " + path + ": " + ex.Message);
            return null;
        }
    }

    private static bool RewriteSolarMemoryDefaultLayer(MapTree tree, int layer)
    {
        var defaultSegmentSize = DefaultLayerSegmentSize();
        var defaultStart = layer * defaultSegmentSize;
        if (defaultStart < 0 || defaultStart >= tree.DefaultNode.Count)
        {
            return false;
        }

        var changed = false;
        tree.DefaultNode[defaultStart] = CreateSolarMemoryEventNode(tree, layer, SolarMemoryOpeningSlotIndex);
        changed = true;

        var defaultEnd = Math.Min(tree.DefaultNode.Count, defaultStart + defaultSegmentSize);
        for (var i = defaultStart + 1; i < defaultEnd; i++)
        {
            tree.DefaultNode[i] = CreateBossChainNode(tree, i - defaultStart, layer);
            changed = true;
        }

        return changed;
    }

    private static bool RewriteSolarMemorySelectLayer(MapTree tree, int layer)
    {
        var selectSegmentSize = SelectLayerSegmentSize();
        var selectStart = layer * selectSegmentSize;
        if (selectStart < 0 || selectStart >= tree.SelectNode.Count)
        {
            return false;
        }

        var changed = false;
        var selectEnd = Math.Min(tree.SelectNode.Count, selectStart + selectSegmentSize);
        for (var i = selectStart; i < selectEnd; i++)
        {
            var indexInSegment = i - selectStart;
            if (indexInSegment == SolarMemoryMidLayerSlotIndex)
            {
                tree.SelectNode[i] = CreateSolarMemoryEventNode(tree, layer, SolarMemoryMidLayerSlotIndex);
                changed = true;
                continue;
            }

            if (IsBreakNode(tree.SelectNode[i]))
            {
                continue;
            }

            tree.SelectNode[i] = CreateBossChainNode(tree, indexInSegment, layer);
            changed = true;
        }

        return changed;
    }

    private static int SolarMemoryLayer(NormalMapManager manager)
    {
        return ClampSolarMemoryLayer(manager.Level / 6);
    }

    private static int ClampSolarMemoryLayer(int layer)
    {
        return Math.Max(0, Math.Min(SunExpIds.SolarMemoryMaxLayer - 1, layer));
    }

    private static int CurrentSolarMemoryLayer()
    {
        if (MapManager.Instance?.ModeMapManager is not NormalMapManager manager)
        {
            return 0;
        }

        return SolarMemoryLayer(manager);
    }

    private static int SolarMemoryEventIndex(int layer, int mapSlotIndex)
    {
        var normalizedLayer = ClampSolarMemoryLayer(layer);
        var slot = mapSlotIndex >= SolarMemoryMidLayerSlotIndex ? 1 : 0;
        var index = normalizedLayer * 2 + slot;
        return Math.Max(0, Math.Min(SunExpIds.SolarMemoryFullEventIds.Length - 1, index));
    }

    private static int DefaultLayerSegmentSize()
    {
        return Math.Max(1, 2 + GameSaveManager.GetValue<int>(GameVar.ExLockDes));
    }

    private static int SelectLayerSegmentSize()
    {
        return Math.Max(1, 8 - GameSaveManager.GetValue<int>(GameVar.ExDeleteDes));
    }

    private static void RepairSolarMemoryMapSelection(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is string[] maps && args[i + 1] is string[] mapData)
                {
                    if (RepairSolarMemoryMapArrays(maps, mapData))
                    {
                        SunExpLog.Info("[SolarMemoryMapSync] map selection arrays repaired.");
                    }

                    TryRestoreSolarMemoryCurrentNodeFromSyncArrays(maps, mapData, "MapManager.MapSelectionSync");
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map selection repair failed", ex);
        }
    }

    private static void EnsureSolarMemoryCurrentNodeBeforeNextMap(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            if (MapManager.Instance?.MapTree?.currentNode == null)
            {
                TryRestoreSolarMemoryCurrentNodeFromMapManager("MapManager.RpcNextMap");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] pre-next-map current node repair failed: " + ex.Message);
        }
    }

    private static void PrepareSolarMemoryFightAbort(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            handlingSolarMemoryFightAbort = true;
            EnsureSolarMemoryCurrentNodeForTransition("Fight_Escape.ResetStates:before");
            CloseSolarMemoryTransientUi("Fight_Escape.ResetStates:before");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] prepare failed: " + ex.Message);
        }
    }

    private static void SettleSolarMemoryFightAbort(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                handlingSolarMemoryFightAbort = false;
                return;
            }

            ClearSolarFinalePendingBattle("Fight_Escape.ResetStates");
            EnsureSolarMemoryCurrentNodeForTransition("Fight_Escape.ResetStates:after");
            CloseSolarMemoryTransientUi("Fight_Escape.ResetStates:after");
            SunExpLog.Info("[SolarMemoryFightAbort] escape/loss branch settled.");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] settle failed: " + ex.Message);
        }
        finally
        {
            handlingSolarMemoryFightAbort = false;
        }
    }

    private static void SettleSolarMemoryFightLoss(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            ClearSolarFinalePendingBattle("Fight_Loss.Init");
            CloseSolarMemoryTransientUi("Fight_Loss.Init");
            if (!handlingSolarMemoryFightAbort)
            {
                EnsureSolarMemoryCurrentNodeForTransition("Fight_Loss.Init");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] loss settle failed: " + ex.Message);
        }
    }

    private static void SyncSolarMemoryClientLastNodeAfterNextMap(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            var node = MapManager.Instance?.MapTree?.currentNode;
            if (node != null)
            {
                GameSaveManager.UpdateNode(node);
                SunExpLog.Debug("[SolarMemoryMapSync] synced client save node after RpcNextMap.");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] post-next-map save node sync failed: " + ex.Message);
        }
    }

    private static bool RepairSolarMemoryMapArrays(string[] maps, string[] mapData)
    {
        if (maps.Length == 0 || mapData.Length == 0)
        {
            return false;
        }

        var layer = CurrentSolarMemoryLayer();
        var changed = false;
        foreach (var spec in FixedNodeSpecs(layer))
        {
            changed = RepairSolarMemorySyncIndex(maps, mapData, spec) || changed;
        }

        var count = Math.Min(maps.Length, mapData.Length);
        for (var i = 0; i < count; i++)
        {
            if (FixedNodeSpecs(layer).Any(spec => spec.SlotIndex == i))
            {
                continue;
            }

            if (IsSolarMemoryMapId(maps[i]) || IsSolarMemoryEventId(mapData[i]))
            {
                var repairSpec = SolarMemoryFixedNodeSpec.Event(i, layer, i);
                changed = RepairSolarMemorySyncIndex(maps, mapData, repairSpec) || changed;
            }
        }

        return changed;
    }

    private static bool RepairSolarMemorySyncIndex(string[] maps, string[] mapData, SolarMemoryFixedNodeSpec spec)
    {
        if (spec.SlotIndex < 0 || spec.SlotIndex >= maps.Length || spec.SlotIndex >= mapData.Length)
        {
            return false;
        }

        var expectedMapId = spec.MapId;
        var expectedNodeId = spec.NodeId;
        if (spec.IsEvent)
        {
            var eventIndex = SolarMemoryEventIndex(spec.Layer, spec.MapSlotIndex);
            expectedMapId = SunExpIds.SolarMemoryMapIds[eventIndex];
            expectedNodeId = SunExpIds.SolarMemoryFullEventIds[eventIndex];
        }

        var changed = false;
        if (maps[spec.SlotIndex] != expectedMapId)
        {
            maps[spec.SlotIndex] = expectedMapId;
            changed = true;
        }

        if (mapData[spec.SlotIndex] != expectedNodeId)
        {
            mapData[spec.SlotIndex] = expectedNodeId;
            changed = true;
        }

        if (changed)
        {
            SunExpLog.Info("[SolarMemoryMapSync] repaired index="
                + spec.SlotIndex
                + "; layer="
                + spec.Layer
                + "; slot="
                + spec.MapSlotIndex
                + "; map="
                + expectedMapId
                + "; node="
                + expectedNodeId);
        }

        return changed;
    }

    private static void EnsureSolarMemoryCurrentNodeForTransition(string source)
    {
        try
        {
            var mapManager = MapManager.Instance;
            var tree = mapManager?.MapTree;
            if (tree == null)
            {
                return;
            }

            if (IsUsableSolarMemoryMapNode(tree.currentNode))
            {
                EnsureSolarMemoryNodeDice(tree.currentNode, tree, source);
                GameSaveManager.UpdateNode(tree.currentNode);
                return;
            }

            var saveNode = GameSaveManager.GetNode();
            if (IsUsableSolarMemoryMapNode(saveNode))
            {
                EnsureSolarMemoryNodeDice(saveNode, tree, source);
                tree.currentNode = saveNode;
                GameSaveManager.UpdateNode(saveNode);
                SunExpLog.Info("[SolarMemoryMapSync] restored current node from save before transition; source=" + source + ".");
                return;
            }

            if (TryRestoreSolarMemoryCurrentNodeFromMapManager(source, false))
            {
                return;
            }

            if (mapManager?.ModeMapManager is NormalMapManager manager
                && EnsureSolarMemoryMapState(manager, source, false)
                && IsUsableSolarMemoryMapNode(tree.currentNode))
            {
                EnsureSolarMemoryNodeDice(tree.currentNode, tree, source);
                GameSaveManager.UpdateNode(tree.currentNode);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] transition current node repair failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    private static bool TryRestoreSolarMemoryCurrentNodeFromMapManager(string source, bool clientOnly = true)
    {
        var mapManager = MapManager.Instance;
        return mapManager != null
            && TryRestoreSolarMemoryCurrentNodeFromSyncArrays(mapManager.mapList, mapManager.mapData, source, clientOnly);
    }

    private static bool TryRestoreSolarMemoryCurrentNodeFromSyncArrays(string[]? maps, string[]? mapData, string source, bool clientOnly = true)
    {
        try
        {
            if ((clientOnly && !IsClientOnlyPlayer())
                || HasSolarMemoryCurrentNodeReady()
                || maps == null
                || mapData == null)
            {
                return false;
            }

            var tree = MapManager.Instance?.MapTree;
            var count = Math.Min(maps.Length, mapData.Length);
            if (tree == null || count <= 0)
            {
                return false;
            }

            var first = BuildSolarMemorySyncedNodeChain(tree, maps, mapData, count);
            if (first == null)
            {
                return false;
            }

            tree.currentNode = first;
            GameSaveManager.UpdateNode(first);
            SunExpLog.Info("[SolarMemoryMapSync] restored client current node from sync arrays; source="
                + source
                + "; count="
                + count
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] failed to restore client current node from "
                + source
                + ": "
                + ex.Message);
            return false;
        }
    }

    private static MapTree.Node? BuildSolarMemorySyncedNodeChain(MapTree tree, string[] maps, string[] mapData, int count)
    {
        MapTree.Node? first = null;
        MapTree.Node? previous = null;
        for (var i = 0; i < count; i++)
        {
            var node = CreateSolarMemorySyncedNode(tree, maps[i], mapData[i], i);
            if (first == null)
            {
                first = node;
            }
            else
            {
                previous?.SetChild(0, node);
            }

            previous = node;
        }

        return first;
    }

    private static MapTree.Node CreateSolarMemorySyncedNode(MapTree tree, string? mapId, string? nodeId, int index)
    {
        var data = CreateSolarMemorySyncedNodeData(mapId, nodeId);
        var type = data == null ? "null" : Field(data, "Note");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = data == null ? "null" : Field(data, "Type");
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            type = "Map";
        }

        return new MapTree.Node(type)
        {
            type = type,
            data = data,
            NodeDice = SyncedNodeDice(tree, index)
        };
    }

    private static Dictionary<string, string>? CreateSolarMemorySyncedNodeData(string? mapId, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return null;
        }

        var normalizedMapId = mapId!;
        var row = MapRow(normalizedMapId);
        var data = row == null ? new Dictionary<string, string>() : new Dictionary<string, string>(row);
        data["Id"] = normalizedMapId;
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            data["NodeId"] = nodeId!;
        }
        else if (!data.ContainsKey("NodeId"))
        {
            data["NodeId"] = normalizedMapId;
        }

        if (!data.ContainsKey("Type") || string.IsNullOrWhiteSpace(data["Type"]))
        {
            data["Type"] = IsSolarMemoryEventId(nodeId) || IsSolarMemoryMapId(normalizedMapId) ? "Event" : "Fight";
        }

        if (!data.ContainsKey("Level") || string.IsNullOrWhiteSpace(data["Level"]))
        {
            data["Level"] = "-1";
        }

        return data;
    }

    private static Dice SyncedNodeDice(MapTree tree, int index)
    {
        return tree.treedice ?? Dice.Default;
    }

    private static bool HasSolarMemoryCurrentNodeReady()
    {
        try
        {
            var currentNode = MapManager.Instance?.MapTree?.currentNode;
            var saveNode = GameSaveManager.GetNode();
            return currentNode != null
                && saveNode != null
                && (IsUsableSolarMemoryMapNode(currentNode) || IsUsableSolarMemoryMapNode(saveNode));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUsableSolarMemoryMapNode(MapTree.Node node)
    {
        return node.data != null || node.childrens != null;
    }

    private static void EnsureSolarMemoryNodeDice(MapTree.Node? node, MapTree tree, string source)
    {
        if (node == null || node.NodeDice != null)
        {
            return;
        }

        node.NodeDice = tree.treedice ?? Dice.Default;
        SunExpLog.Debug("[SolarMemoryMapSync] repaired current node dice from " + source + ".");
    }

    private static void ClearSolarFinalePendingBattle(string source)
    {
        if (PlayerApi.GetGameVar(SunExpIds.SolarFinalePendingSaintBattleKey, "") == "")
        {
            return;
        }

        PlayerApi.SetGameVar(SunExpIds.SolarFinalePendingSaintBattleKey, "");
        SunExpLog.Info("[SolarMemoryFightAbort] cleared pending saint battle from " + source + ".");
    }

    private static void CloseSolarMemoryTransientUi(string source)
    {
        try
        {
            SolarMemorySetupFlowRuntime.ClosePreparationWindows();
            SolarMemoryBlessingPickerRuntime.Close();
            CloseExistingPackWindow();
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExpSolarMemoryStarterDeck", source, "[SolarMemoryFightAbort]");
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryOriginSetup", source, "[SolarMemoryFightAbort]");
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryBlessingSetup", source, "[SolarMemoryFightAbort]");
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryBlessingPicker", source, "[SolarMemoryFightAbort]");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] transient UI cleanup failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    private static bool IsClientOnlyPlayer()
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            return playerManager != null && !playerManager.isServer;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSolarMemoryMapId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return SunExpIds.SolarMemoryMapIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || SunExpIds.SolarMemoryShortMapIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || string.Equals(id, "SunExp_sunexp_solar_memory_start", StringComparison.Ordinal)
            || string.Equals(id, "solar_memory_start", StringComparison.Ordinal);
    }

    private static bool IsSolarMemoryEventId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return SunExpIds.SolarMemoryFullEventIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || SunExpIds.SolarMemoryEventIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || string.Equals(id, "SunExp_sunexp_Sub_solar_memory_start", StringComparison.Ordinal)
            || string.Equals(id, "Sub_solar_memory_start", StringComparison.Ordinal);
    }

    private static void FinishSolarMemoryAfterFinalLayer(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager)
            {
                return;
            }

            if (manager.Level < SunExpIds.SolarMemoryMaxLayer * 6
                && !IsSolarFinaleLayerActive(manager))
            {
                return;
            }

            CompleteSolarMemoryRun(manager, "NormalMapManager.ReadyToChangeMap", 32);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory settlement failed", ex);
        }
    }

    private static void CompleteSolarMemoryRun(NormalMapManager manager, string source, int levelForNativeFlow)
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "1");
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleFinalLayerEnteredKey, "0");
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateOpenedKey, "");
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateResolvedKey, "");
        PlayerApi.SetGameVar(SunExpIds.SolarFinalePendingSaintBattleKey, "");
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleCompletedKey, "1");
        manager.Level = levelForNativeFlow;
        SunExpLog.Info("[SolarMemory] third layer complete from "
            + source
            + "; routing directly to settlement at native level "
            + levelForNativeFlow
            + ".");
    }

    private static void SettleLegacySolarFinaleBeforeMapItems(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager
                || !IsSolarFinaleLayerActive(manager))
            {
                return;
            }

            CompleteSolarMemoryRun(manager, "NormalMapManager.MapItemInit", SunExpIds.SolarMemoryMaxLayer * 6);
            ShowSolarMemorySettlement();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory legacy finale settlement failed", ex);
        }
    }

    private static bool IsSolarFinaleLayerActive(NormalMapManager? manager = null)
    {
        manager ??= MapManager.Instance?.ModeMapManager as NormalMapManager;
        return PlayerApi.GetGameVar(SunExpIds.SolarFinaleFinalLayerEnteredKey, "0") == "1"
            || (manager != null && manager.Level >= SunExpIds.SolarFinaleMapLevel);
    }

    public static void StartSolarFinaleSaintBattle()
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                PlayerApi.EndEvent();
                return;
            }

            var mapManager = MapManager.Instance;
            var tree = mapManager?.MapTree;
            if (mapManager == null || tree == null)
            {
                SunExpLog.Warn("[SolarFinale] unable to start saint battle: MapManager or MapTree missing.");
                OpenSolarFinaleEndingEvent();
                return;
            }

            var node = SolarMemoryMapNodePoolFactory.CreateFixedBossNode(tree, SunExpIds.SolarBossSaintWunaMapId);
            tree.currentNode = node;
            GameSaveManager.UpdateNode(node);
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateResolvedKey, "1");
            PlayerApi.SetGameVar(SunExpIds.SolarFinalePendingSaintBattleKey, "in_progress");
            UIManager.Instance?.CloseUI("EventUI");
            mapManager.CmdNextMap();
            SunExpLog.Info("[SolarFinale] starting fixed saint battle node.");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale saint battle start failed", ex);
            OpenSolarFinaleEndingEvent();
        }
    }

    public static void OpenSolarFinaleEndingEvent()
    {
        try
        {
            UIManager.Instance?.CloseUI("MapSelectUI");
            UIManager.Instance?.CloseUI("EventUI");
            UIManager.Instance?.ShowEventUI(SunExpIds.SolarFinaleFullEndingEventId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale ending event failed", ex);
            UIManager.Instance?.ShowUI<GameExitUI>("GameExitUI", true);
        }
    }

    public static void ShowSolarMemorySettlement()
    {
        try
        {
            UIManager.Instance?.CloseUI("MapSelectUI");
            UIManager.Instance?.CloseUI("EventUI");
            UIManager.Instance?.ShowUI<GameExitUI>("GameExitUI", true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory settlement UI failed", ex);
        }
    }

    private static void SettleSolarMemoryBossAfterWin(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            var levelId = FightManager.Instance?.level ?? "";
            if (string.Equals(levelId, SunExpIds.SolarBossSecondSunLevelId, StringComparison.Ordinal))
            {
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "1");
                if (RoleDeckHasCard(SunExpIds.BlazingCrownCollapseCardId))
                {
                    SunExpLog.Info("[SolarMemoryBoss] second sun defeated; blazing crown collapse found, continuing memory.");
                    return;
                }

                CompleteSolarMemoryRunForSettlement("Fight_Win.ResetStates:second_sun_without_key_card");
                return;
            }

            if (string.Equals(levelId, SunExpIds.SolarBossSaintWunaLevelId, StringComparison.Ordinal))
            {
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintDefeatedKey, "1");
                CompleteSolarMemoryRunForSettlement("Fight_Win.ResetStates:saint_wuna");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory boss win settlement failed", ex);
        }
    }

    private static void CompleteSolarMemoryRunForSettlement(string source)
    {
        if (MapManager.Instance?.ModeMapManager is NormalMapManager manager)
        {
            CompleteSolarMemoryRun(manager, source, 32);
        }

        UIManager.Instance?.CloseUI("FightUI");
        ShowSolarMemorySettlement();
    }

    private static bool RoleDeckHasCard(string cardId)
    {
        var role = RoleTable.Instance;
        if (role == null || string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        return role.cardList.Any(card => IsCardId(card, cardId));
    }

    private static bool IsCardId(DataConfig? card, string expectedFullId)
    {
        var id = CardId(card);
        return string.Equals(id, expectedFullId, StringComparison.Ordinal)
            || string.Equals(id, ShortModId(expectedFullId), StringComparison.Ordinal);
    }

    private static string ShortModId(string id)
    {
        const string prefix = "SunExp_sunexp_";
        return id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : id;
    }

    private static void FinishSolarFinaleSaintBattle()
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinalePendingSaintBattleKey, "");
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintDefeatedKey, "1");
        if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(SunExpIds.SolarFinaleEndingKey, "")))
        {
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, "stars");
        }

        OpenSolarFinaleEndingEvent();
        SunExpLog.Info("[SolarFinale] saint battle finished; opening ending event.");
    }

    private static MapTree.Node CreateSolarMemoryEventNode(MapTree tree, int layer, int mapSlotIndex)
    {
        var eventIndex = SolarMemoryEventIndex(layer, mapSlotIndex);
        var mapId = SunExpIds.SolarMemoryMapIds[eventIndex];
        var shortMapId = SunExpIds.SolarMemoryShortMapIds[eventIndex];
        var eventId = SunExpIds.SolarMemoryFullEventIds[eventIndex];
        var data = Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, mapId)
            ?? Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, shortMapId);
        var node = new MapTree.Node("普通事件");
        node.type = "普通事件";
        node.data = data == null ? new Dictionary<string, string>() : new Dictionary<string, string>(data);
        node.data["Id"] = mapId;
        node.data["Type"] = "Event";
        node.data["Note"] = "普通事件";
        node.data["NodeId"] = eventId;
        node.data["Level"] = "-1";
        node.NodeDice = Dice.Default;
        return node;
    }

    private static MapTree.Node CreateBossChainNode(MapTree tree, int indexInSegment, int segment)
    {
        return tree.TypeGenerate("首领");
    }

    private static bool IsBreakNode(MapTree.Node node)
    {
        if (node?.data == null)
        {
            return false;
        }

        return (node.data.TryGetValue("NodeId", out var nodeId) && nodeId.Contains("Breaks"))
            || (node.data.TryGetValue("Id", out var id) && id.Contains("Breaks"));
    }

    public static bool IsSolarMemoryRun()
    {
        return GameSaveManager.GetValue<string>(SunExpIds.SolarMemoryModeKey) == "1";
    }

    private static List<Dictionary<string, string>> VisibleCardPacks()
    {
        return Singleton<GameConfigManager>.Instance.GetTable(DataType.CardPack).Getlines()
            .Where(pack => !Singleton<GameRuntimeData>.Instance.IsLocked(pack["Id"]) && pack["Id"] != "cardpack_13")
            .ToList();
    }

    private static HashSet<string> InitialPackSelection()
    {
        var visible = VisibleCardPacks().Select(pack => pack["Id"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = Singleton<GameRuntimeData>.Instance.UseCardPack
            .Where(visible.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            selected.UnionWith(visible.Take(6));
        }

        return selected;
    }

    public static List<string> CurrentPackSelection()
    {
        var playerPacks = SolarMemoryPlayerSetupState.SelectedPacks()
            .Where(IsValidPackForCurrentLobby)
            .ToList();
        if (playerPacks.Count > 0)
        {
            return playerPacks;
        }

        if (!PlayerApi.IsMultiplayerSession())
        {
            var saved = IsSolarMemoryRun() ? GameSaveManager.GetValue<string>(SunExpIds.SolarMemorySelectedPacksKey) : "";
            if (!string.IsNullOrWhiteSpace(saved))
            {
                var savedPacks = saved.Split('|')
                    .Where(IsValidPackForCurrentLobby)
                    .ToList();
                if (savedPacks.Count > 0)
                {
                    return savedPacks;
                }
            }
        }

        var selected = Singleton<GameRuntimeData>.Instance.UseCardPack
            .Where(IsValidPackForCurrentLobby)
            .ToList();
        if (selected.Count == 0)
        {
            selected.AddRange(VisibleCardPacks().Take(6).Select(pack => pack["Id"]));
        }

        return selected;
    }

    private static bool IsValidPackForCurrentLobby(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && (!string.Equals(id, "cardpack_13", StringComparison.OrdinalIgnoreCase) || GameCompatibilityApi.ShouldEnableOnlineCardPack());
    }

    public static bool IsSolarMemoryEventCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        if (ContainsEventMarker(cardId))
        {
            return true;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return IsSolarMemoryEventCard(data) || HasLocalizedEventCardType(cardId);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSolarMemoryEventCard(IDictionary<string, string> data)
    {
        var id = Field(data, "Id");
        if (ContainsEventCardIdMarker(id))
        {
            return true;
        }

        return ContainsEventTypeMarker(Field(data, "Type"))
            || ContainsEventTypeMarker(Field(data, "Note"))
            || HasLocalizedEventCardType(id)
            || ContainsSolarEventScriptMarker(Field(data, "Tag"))
            || ContainsSolarEventScriptMarker(Field(data, "Action"))
            || ContainsSolarEventScriptMarker(Field(data, "InitScript"))
            || ContainsSolarEventScriptMarker(Field(data, "UseScript"));
    }

    private static bool HasLocalizedEventCardType(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return ContainsEventTypeMarker(data.Localize("Type"))
                || ContainsEventTypeMarker(data.Localize("Note"));
        }
        catch
        {
            return false;
        }
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value : "";
    }

    private static bool ContainsEventMarker(string value)
    {
        return ContainsEventCardIdMarker(value) || ContainsEventTypeMarker(value) || ContainsSolarEventScriptMarker(value);
    }

    private static bool ContainsEventCardIdMarker(string value)
    {
        return value.IndexOf("solar_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("solar_memory_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("SolarMemoryEvent", StringComparison.OrdinalIgnoreCase) >= 0
            || value.StartsWith("event_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("card_event", StringComparison.OrdinalIgnoreCase)
            || value.IndexOf("_event_", StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("事件");
    }

    private static bool ContainsEventTypeMarker(string value)
    {
        return value.Equals("Event", StringComparison.OrdinalIgnoreCase)
            || value.Equals("事件", StringComparison.Ordinal)
            || value.Equals("事件牌", StringComparison.Ordinal)
            || value.Equals("事件卡", StringComparison.Ordinal)
            || value.IndexOf("EventCard", StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("事件牌")
            || value.Contains("事件卡");
    }

    private static bool ContainsSolarEventScriptMarker(string value)
    {
        return value.IndexOf("solar_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("solar_memory_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("SolarMemoryEvent", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void FilterSolarMemoryCardPackCheck(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not List<Dictionary<string, string>> cards)
            {
                return;
            }

            var removed = RemoveEventCardData(cards);
            if (removed.Count > 0)
            {
                SunExpLog.Info("[SolarMemoryMode] removed event cards from CardPackCheck: " + string.Join("|", removed));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory CardPackCheck filter failed", ex);
        }
    }

    private static List<string> RemoveEventCardData(List<Dictionary<string, string>> cards)
    {
        var removed = new List<string>();
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var data = cards[i];
            if (data != null && IsSolarMemoryEventCard(data))
            {
                removed.Add(Field(data, "Id"));
                cards.RemoveAt(i);
            }
        }

        removed.Reverse();
        return removed;
    }

    public static int SanitizeSolarMemoryRoleCards(RoleTable? role, string source)
    {
        if (role == null)
        {
            return 0;
        }

        var removed = new List<string>();
        RemoveEventConfigs(role.cardList, removed);
        RemoveEventConfigs(role.UnCardList, removed);
        NormalizeSolarMemoryCardCounts(role);

        if (removed.Count > 0)
        {
            SunExpLog.Info("[SolarMemoryMode] sanitized event cards from " + source + ": " + string.Join("|", removed));
        }

        return removed.Count;
    }

    private static void RemoveEventConfigs(IList<DataConfig> cards, List<string> removed)
    {
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var config = cards[i];
            var id = CardId(config);
            if (IsSolarMemoryEventCard(id))
            {
                removed.Add(id);
                cards.RemoveAt(i);
            }
        }

        removed.Reverse();
    }

    private static string CardId(DataConfig? config)
    {
        if (config == null)
        {
            return "";
        }

        return Field(config.data, "Id");
    }

    private static void NormalizeSolarMemoryCardCounts(RoleTable role)
    {
        role.CardTopCount = Math.Max(role.CardTopCount, role.cardList.Count);
        role.CardBottomCount = Math.Min(role.CardBottomCount, role.cardList.Count);
        role.MaxAlCardCount = role.UnCardList == null ? 0 : Math.Min(role.MaxAlCardCount, role.UnCardList.Count);
    }

    public static void ClearSolarMemoryReservePool()
    {
        ClearSolarMemoryReservePool(RoleTable.Instance);
    }

    public static void ClearSolarMemoryReservePool(RoleTable? role)
    {
        if (role == null)
        {
            return;
        }

        SanitizeSolarMemoryRoleCards(role, "ClearSolarMemoryReservePool");
        role.UnCardList?.Clear();
        NormalizeSolarMemoryCardCounts(role);

        role.SpecialVarMap ??= new Dictionary<string, string>();
        role.SpecialVarMap[SunExpIds.SolarMemoryDeckConfiguredKey] = "1";
        if (ReferenceEquals(role, RoleTable.Instance))
        {
            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryDeckConfiguredKey, true);
        }

        UIManager.Instance?.ShowTip("\u65e5\u8000\u56de\u5fc6\u5907\u9009\u724c\u5df2\u6e05\u7a7a", null);
    }

    private static string PackName(Dictionary<string, string> pack)
    {
        if (pack.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return pack["Id"];
    }

    private static string PackCounts(string id)
    {
        var card = GameCompatibilityApi.GetItemsByPack(DataType.Card, id).Count;
        var relic = GameCompatibilityApi.GetItemsByPack(DataType.Relic, id).Count;
        var bless = GameCompatibilityApi.GetItemsByPack(DataType.Bless, id).Count;

        return "卡 " + card + " / 遗物 " + relic + " / 祝福 " + bless;
    }

    private static void RefreshPackRows(Dictionary<string, Image> rows, HashSet<string> selected, Text summary)
    {
        foreach (var pair in rows)
        {
            pair.Value.color = selected.Contains(pair.Key) ? RowSelectedColor : RowNormalColor;
        }

        summary.text = "已选择卡包：" + selected.Count + "。确认后将以普通冒险底层进入，并启用日耀回忆 Boss 连战地图。";
    }

    private static RectTransform CreatePanel(RectTransform parent, Vector2 size)
    {
        var panel = CreateRect("Panel", parent);
        panel.sizeDelta = size;
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = PanelColor;
        return panel;
    }

    private static RectTransform AddButton(RectTransform parent, string name, string label, Vector2 anchoredPosition, Vector2 size, Action action)
    {
        var rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.gameObject.AddComponent<Image>().color = ButtonColor;
        var button = rect.gameObject.AddComponent<Button>();
        button.onClick.AddListener(new UnityAction(action));
        AddText(rect, "Text", label, 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, Vector2.zero, size);
        return rect;
    }

    private static Text AddText(RectTransform parent, string name, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color,
        Vector2 anchoredPosition, Vector2 size)
    {
        var rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        var text = rect.gameObject.AddComponent<Text>();
        ConfigureText(text, value, fontSize, style, alignment, color);
        return text;
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

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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

    private static void BindUnityEvent(object target, string fieldName, Action action)
    {
        try
        {
            var unityEvent = target.GetType().GetField(fieldName)?.GetValue(target) as UnityEvent;
            if (unityEvent == null)
            {
                return;
            }

            unityEvent.RemoveAllListeners();
            unityEvent.AddListener(new UnityAction(action));
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Solar memory button bind failed: " + ex.Message);
        }
    }

    private sealed class SolarMemoryFixedNodeSpec
    {
        private SolarMemoryFixedNodeSpec(int slotIndex, int layer, int mapSlotIndex, bool isEvent, string mapId, string nodeId)
        {
            SlotIndex = slotIndex;
            Layer = layer;
            MapSlotIndex = mapSlotIndex;
            IsEvent = isEvent;
            MapId = mapId;
            NodeId = nodeId;
        }

        public int SlotIndex { get; }

        public int Layer { get; }

        public int MapSlotIndex { get; }

        public bool IsEvent { get; }

        public string MapId { get; }

        public string NodeId { get; }

        public static SolarMemoryFixedNodeSpec Event(int slotIndex, int layer, int mapSlotIndex)
        {
            var eventIndex = SolarMemoryEventIndex(layer, mapSlotIndex);
            return new SolarMemoryFixedNodeSpec(
                slotIndex,
                layer,
                mapSlotIndex,
                true,
                SunExpIds.SolarMemoryMapIds[eventIndex],
                SunExpIds.SolarMemoryFullEventIds[eventIndex]);
        }

        public static SolarMemoryFixedNodeSpec Boss(int slotIndex, int layer, string mapId, string levelId)
        {
            return new SolarMemoryFixedNodeSpec(slotIndex, layer, slotIndex, false, mapId, levelId);
        }
    }

    private static void CloseExistingPackWindow()
    {
        CloseWindow(PackWindowName);
    }

    private static void CloseWindow(string name)
    {
        SunExpUiSafety.DisableRaycastsAndDestroyByName(name, "CloseWindow", "[SolarMemory]");
    }
}
