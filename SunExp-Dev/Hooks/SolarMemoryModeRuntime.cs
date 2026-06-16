using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.GameApi;
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

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "ModeChoiceUI.Init", InjectEntry);
        RegisterAfter(modConfig, "ModeChoiceUI.DataUpdate", InjectEntry);
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
        RegisterAfter(modConfig, "NormalMapManager.ReadyToChangeMap", FinishSolarMemoryAfterFinalLayer);
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

            if (PlayerApi.GetGameVar(SunExpIds.SolarMemoryDeckConfiguredKey, "0") != "1")
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
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Solar memory hook registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Solar memory hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            SunExpLog.Debug("Solar memory hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Solar memory hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void InjectEntry(ModHookContext context)
    {
        try
        {
            if (context.Target is not ModeChoiceUI modeChoice)
            {
                return;
            }

            var modeList = modeChoice.transform.Find("ModeList");
            var template = modeList?.Find("SublimationMode") ?? modeList?.Find("NormalMode");
            if (modeList == null || template == null)
            {
                return;
            }

            var entry = modeList.Find(EntryObjectName)?.gameObject;
            if (entry == null)
            {
                entry = UnityEngine.Object.Instantiate(template.gameObject, modeList);
                entry.name = EntryObjectName;
                entry.transform.SetAsLastSibling();
                entry.transform.localScale = template.localScale;
            }

            ConfigureEntryUnlocked(entry.transform);
            ConfigureEntryTexts(entry.transform);
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
        ClearEntryStateImages(entry.Find("Normal"), normalTitle);
        ClearEntryStateImages(entry.Find("HighLighted"), highlightedTitle);
        ClearEntryStateImages(entry.Find("Pressed"), highlightedTitle);
        SetImageSprite(normalTitle, normalSprite);
        SetImageSprite(highlightedTitle, highlightedSprite);
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
        if (stateRoot == null || keep == null)
        {
            return;
        }

        foreach (var image in stateRoot.GetComponentsInChildren<Image>(true))
        {
            if (image.transform == keep)
            {
                continue;
            }

            image.sprite = null;
            image.enabled = false;
        }

        foreach (var rawImage in stateRoot.GetComponentsInChildren<RawImage>(true))
        {
            if (rawImage.transform == keep)
            {
                continue;
            }

            rawImage.texture = null;
            rawImage.enabled = false;
        }
    }

    private static void ConfigureEntryClick(GameObject entry, ModeChoiceUI modeChoice)
    {
        var switchButton = entry.GetComponent<SwitchButton>();
        if (switchButton != null)
        {
            switchButton.interactable = true;
            switchButton.onClick.RemoveAllListeners();
            switchButton.onClick.AddListener(new UnityAction(() => StartSolarMemoryRun(modeChoice, InitialPackSelection().ToList())));
        }

        foreach (var component in entry.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                BindUnityEvent(component, "onClick", () => StartSolarMemoryRun(modeChoice, InitialPackSelection().ToList()));
            }
        }

        foreach (var button in entry.GetComponentsInChildren<Button>(true))
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(new UnityAction(() => StartSolarMemoryRun(modeChoice, InitialPackSelection().ToList())));
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
                StartSolarMemoryRun(modeChoice, selected.ToList());
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

    private static void StartSolarMemoryRun(ModeChoiceUI modeChoice, List<string> selectedPacks)
    {
        try
        {
            var saveInfo = CreateSolarMemorySave(selectedPacks);
            SolarMemoryStarterDeckRuntime.CaptureSelectedPacks(selectedPacks);
            GameSaveManager.Select(saveInfo);
            GameEntryUI.selectedSave = saveInfo;
            LobbyManager.Instance?.SetLobbyModeType("Normal");

            if (PlayerManager.Instance == null)
            {
                StartLobby();
            }
            else if (!PlayerManager.Instance.isServer)
            {
                modeChoice.Close();
                UIManager.Instance.ShowUI<GameEntryUI>("GameEntryUI", true).Init();
                UIManager.Instance.GetUI<CaptionUI>("CaptionUI")
                    .ShowCaption("Only the host can start the game".Localize("GameEntryUI"), CaptionStyle.Top, 1f, 1.5f, 3);
                return;
            }

            modeChoice.Close();
            UIManager.Instance.ShowUI<GameEntryUI>("GameEntryUI", true).Init();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory run start failed", ex);
        }
    }

    private static SaveInfo CreateSolarMemorySave(List<string> selectedPacks)
    {
        var random = new System.Random((int)DateTime.Now.Ticks);
        var saveInfo = new SaveInfo
        {
            CreatedTime = DateTime.Now.ToString("yyyy-MM-dd,HH:mm"),
            Version = GameConfigManager.Version,
            isCheat = false,
            Name = "SunExpSolarMemory" + UnityEngine.Random.Range(0, 100000),
            roleTable = new Dictionary<string, RoleTable>(),
            mapTree = new MapTree(),
            HardTags = new List<DataConfig>(),
            startTime = DateTime.Now,
            modeType = "Normal",
            Seed = random.Next(0, (int)Math.Pow(10.0, 16.0) - 1).ToString()
        };

        saveInfo.ItemOpers.PlayerId = Singleton<GameConfigManager>.Instance.PlayerId;
        saveInfo.GameVars[SunExpIds.SolarMemoryModeKey] = "1";
        saveInfo.GameVars[SunExpIds.SolarMemorySelectedPacksKey] = string.Join("|", selectedPacks);
        saveInfo.GameVars[SunExpIds.SolarMemoryOriginPointsKey] = "50";
        saveInfo.GameVars[SunExpIds.SolarMemoryBlessPickCountKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryBlessSelectedIdsKey] = "";
        saveInfo.GameVars[SunExpIds.SolarMemoryDeckConfiguredKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryStarterDeckAppliedKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryStarterDeckModeKey] = "";
        saveInfo.GameVars[SunExpIds.SolarMemoryOriginConfiguredKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryBlessConfiguredKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemorySetupFinishedKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryPrepStepKey] = SolarMemoryPrepStep.DeckSelection.ToString();
        saveInfo.GameVars[SunExpIds.SolarMemoryPreparedKey] = "0";
        saveInfo.GameVars["MapScene1"] = (random.Next(0, 100) < 50 ? SceneType.Courtyard : SceneType.Forest).ToString();
        saveInfo.GameVars["MapScene2"] = SceneType.SlotMachScene.ToString();
        saveInfo.GameVars["MapScene3"] = (random.Next(0, 100) < 50 ? SceneType.Castle : SceneType.Chessboard).ToString();
        return saveInfo;
    }

    private static void StartLobby()
    {
        GameCompatibilityApi.StartLobby();
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
        return MapManager.Instance?.ModeMapManager is NormalMapManager manager
            ? SolarMemoryLayer(manager)
            : 0;
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
                if (args[i] is string[] maps && args[i + 1] is string[] mapData && RepairSolarMemoryMapArrays(maps, mapData))
                {
                    SunExpLog.Info("[SolarMemoryMapSync] map selection arrays repaired.");
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map selection repair failed", ex);
        }
    }

    private static bool RepairSolarMemoryMapArrays(string[] maps, string[] mapData)
    {
        if (maps.Length == 0 || mapData.Length == 0)
        {
            return false;
        }

        var count = Math.Min(maps.Length, mapData.Length);
        var layer = CurrentSolarMemoryLayer();
        var changed = RepairSolarMemorySyncIndex(maps, mapData, SolarMemoryOpeningSlotIndex, layer, SolarMemoryOpeningSlotIndex);
        if (count > SolarMemoryMidLayerSlotIndex)
        {
            changed = RepairSolarMemorySyncIndex(maps, mapData, SolarMemoryMidLayerSlotIndex, layer, SolarMemoryMidLayerSlotIndex) || changed;
        }

        for (var i = 0; i < count; i++)
        {
            if (i == SolarMemoryOpeningSlotIndex || i == SolarMemoryMidLayerSlotIndex)
            {
                continue;
            }

            if (IsSolarMemoryMapId(maps[i]) || IsSolarMemoryEventId(mapData[i]))
            {
                changed = RepairSolarMemorySyncIndex(maps, mapData, i, layer, i) || changed;
            }
        }

        return changed;
    }

    private static bool RepairSolarMemorySyncIndex(string[] maps, string[] mapData, int index, int layer, int mapSlotIndex)
    {
        var eventIndex = SolarMemoryEventIndex(layer, mapSlotIndex);
        var expectedMapId = SunExpIds.SolarMemoryMapIds[eventIndex];
        var expectedEventId = SunExpIds.SolarMemoryFullEventIds[eventIndex];
        var changed = false;
        if (maps[index] != expectedMapId)
        {
            maps[index] = expectedMapId;
            changed = true;
        }

        if (mapData[index] != expectedEventId)
        {
            mapData[index] = expectedEventId;
            changed = true;
        }

        if (changed)
        {
            SunExpLog.Info("[SolarMemoryMapSync] repaired index="
                + index
                + "; layer="
                + layer
                + "; slot="
                + mapSlotIndex
                + "; map="
                + expectedMapId
                + "; event="
                + expectedEventId);
        }

        return changed;
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
                || context.Target is not NormalMapManager manager
                || manager.Level < SunExpIds.SolarMemoryMaxLayer * 6)
            {
                return;
            }

            UIManager.Instance?.CloseUI("MapSelectUI");
            UIManager.Instance?.ShowUI<GameExitUI>("GameExitUI", true);
            SunExpLog.Debug("Solar memory final layer finished; showing settlement.");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory settlement failed", ex);
        }
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
        role.MaxAlCardCount = Math.Min(role.MaxAlCardCount, role.UnCardList.Count);
    }

    public static void ClearSolarMemoryReservePool()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        SanitizeSolarMemoryRoleCards(role, "ClearSolarMemoryReservePool");
        role.UnCardList.Clear();
        NormalizeSolarMemoryCardCounts(role);

        PlayerApi.SetGameVar(SunExpIds.SolarMemoryDeckConfiguredKey, "1");
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
        var card = 0;
        var relic = 0;
        var bless = 0;
        foreach (var pair in Singleton<GameConfigManager>.Instance.GetPackItems(id))
        {
            if (pair.Key == DataType.Card)
            {
                card += pair.Value.Count;
            }
            else if (pair.Key == DataType.Relic)
            {
                relic += pair.Value.Count;
            }
            else if (pair.Key == DataType.Bless)
            {
                bless += pair.Value.Count;
            }
        }

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

    private static void CloseExistingPackWindow()
    {
        CloseWindow(PackWindowName);
    }

    private static void CloseWindow(string name)
    {
        var root = GameObject.Find(name);
        if (root != null)
        {
            UnityEngine.Object.Destroy(root);
        }
    }
}
