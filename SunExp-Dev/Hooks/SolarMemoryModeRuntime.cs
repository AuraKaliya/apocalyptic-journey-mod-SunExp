using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
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
    private static readonly Color PanelColor = new(0.11f, 0.09f, 0.08f, 0.96f);
    private static readonly Color AccentColor = new(0.84f, 0.55f, 0.2f, 1f);
    private static readonly Color RowNormalColor = new(0.18f, 0.16f, 0.14f, 0.92f);
    private static readonly Color RowSelectedColor = new(0.38f, 0.24f, 0.11f, 0.96f);
    private static readonly Color ButtonColor = new(0.29f, 0.21f, 0.16f, 0.96f);
    private static Font? cachedFont;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "ModeChoiceUI.Init", InjectEntry);
        RegisterAfter(modConfig, "ModeChoiceUI.DataUpdate", InjectEntry);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RewriteSolarMemoryMap);
        RegisterAfter(modConfig, "NormalMapManager.ReadyToChangeMap", FinishSolarMemoryAfterFirstLayer);
    }

    public static void OpenOriginWindow()
    {
        try
        {
            CloseWindow("SunExp_SolarMemoryOriginWindow");
            var parent = UIManager.Instance?.upperCanvasTf ?? UIManager.Instance?.canvasTf;
            if (parent == null || RoleTable.Instance == null)
            {
                return;
            }

            var root = CreateRect("SunExp_SolarMemoryOriginWindow", parent);
            Stretch(root);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var panel = CreatePanel(root, new Vector2(620f, 430f));
            AddText(panel, "Title", "本源加点", 30, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white,
                new Vector2(34f, -38f), new Vector2(320f, 48f));
            var summary = AddText(panel, "Summary", "", 18, FontStyle.Normal, TextAnchor.MiddleRight, new Color(1f, 0.86f, 0.64f, 1f),
                new Vector2(260f, -42f), new Vector2(320f, 40f));

            var valueTexts = new Dictionary<string, Text>(StringComparer.Ordinal);
            var names = new[]
            {
                Tuple.Create("Strength", "魔力"),
                Tuple.Create("Lucky", "精神"),
                Tuple.Create("Perceive", "感知"),
                Tuple.Create("Wisdom", "幸运")
            };

            for (var i = 0; i < names.Length; i++)
            {
                var item = names[i];
                var y = -105f - i * 64f;
                AddText(panel, item.Item1 + "Name", item.Item2, 21, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white,
                    new Vector2(48f, y), new Vector2(160f, 48f));
                valueTexts[item.Item1] = AddText(panel, item.Item1 + "Value", "", 22, FontStyle.Bold, TextAnchor.MiddleCenter,
                    new Color(1f, 0.82f, 0.46f, 1f), new Vector2(250f, y), new Vector2(100f, 48f));
                AddButton(panel, item.Item1 + "Add", "+", new Vector2(420f, 430f + y - 48f), new Vector2(86f, 42f), () =>
                {
                    AddOriginPoint(item.Item1, valueTexts, summary);
                });
            }

            RefreshOriginTexts(valueTexts, summary);
            AddButton(panel, "Close", "关闭", new Vector2(470f, 28f), new Vector2(110f, 42f), () => CloseWindow("SunExp_SolarMemoryOriginWindow"));
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
            if (BlessingPickCount() >= 5)
            {
                UIManager.Instance?.ShowTip("祝福挑选已完成", null);
                return;
            }

            OpenBlessingStep();
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
                ConfigureSolarMemoryReservePool();
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

            ConfigureEntryTexts(entry.transform);
            ConfigureEntryClick(entry, modeChoice);
            entry.SetActive(true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory entry injection failed", ex);
        }
    }

    private static void ConfigureEntryTexts(Transform entry)
    {
        SetTmpText(entry.Find("Text/Text"), SunExpIds.SolarMemoryDescription + "\n" + SunExpIds.SolarMemorySubtitle);

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
            ConfigureText(text, SunExpIds.SolarMemoryTitle, 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        }
        else
        {
            var text = title.GetComponent<Text>();
            if (text != null)
            {
                text.text = SunExpIds.SolarMemoryTitle;
            }
        }
    }

    private static void ConfigureEntryClick(GameObject entry, ModeChoiceUI modeChoice)
    {
        var switchButton = entry.GetComponent<SwitchButton>();
        if (switchButton != null)
        {
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
        saveInfo.GameVars[SunExpIds.SolarMemoryOriginPointsKey] = "3";
        saveInfo.GameVars[SunExpIds.SolarMemoryBlessPickCountKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryDeckConfiguredKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryStarterDeckAppliedKey] = "0";
        saveInfo.GameVars[SunExpIds.SolarMemoryStarterDeckModeKey] = "";
        saveInfo.GameVars[SunExpIds.SolarMemoryPreparedKey] = "0";
        saveInfo.GameVars["MapScene1"] = (random.Next(0, 100) < 50 ? SceneType.Courtyard : SceneType.Forest).ToString();
        saveInfo.GameVars["MapScene2"] = SceneType.SlotMachScene.ToString();
        saveInfo.GameVars["MapScene3"] = (random.Next(0, 100) < 50 ? SceneType.Castle : SceneType.Chessboard).ToString();
        return saveInfo;
    }

    private static void StartLobby()
    {
        if (!LobbyManager.ShouldUseSteamLobby())
        {
            LobbyManager.Instance.StartLocalHost();
            return;
        }

        LobbyManager.Instance?.TryCreateSteamLobby(4);
    }

    private static void RewriteSolarMemoryMap(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not NormalMapManager manager)
            {
                return;
            }

            var tree = manager.MapTree;
            if (tree == null || tree.DefaultNode.Count == 0 || manager.Level != 0)
            {
                return;
            }

            var segmentSize = 2 + GameSaveManager.GetValue<int>(GameVar.ExLockDes);
            var start = 0;
            if (start < 0 || start >= tree.DefaultNode.Count)
            {
                return;
            }

            tree.DefaultNode[start] = CreateSolarMemoryStartNode(tree);

            for (var i = start + 1; i < Math.Min(tree.DefaultNode.Count, start + segmentSize); i++)
            {
                tree.DefaultNode[i] = CreateBossChainNode(tree, i - start, manager.Level / 6);
            }

            RewriteSelectNodesToBosses(tree);

            SunExpLog.Debug("Solar memory map segment rewritten at level " + manager.Level);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map rewrite failed", ex);
        }
    }

    private static void FinishSolarMemoryAfterFirstLayer(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not NormalMapManager manager || manager.Level < 6)
            {
                return;
            }

            UIManager.Instance?.CloseUI("MapSelectUI");
            UIManager.Instance?.ShowUI<GameExitUI>("GameExitUI", true);
            SunExpLog.Debug("Solar memory first layer finished; showing settlement.");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory settlement failed", ex);
        }
    }

    private static MapTree.Node CreateSolarMemoryStartNode(MapTree tree)
    {
        var data = Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, SunExpIds.SolarMemoryMapId)
            ?? Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, SunExpIds.SolarMemoryShortMapId);
        var node = tree.TypeGenerate("普通事件");
        node.type = "普通事件";
        node.data = data == null ? new Dictionary<string, string>() : new Dictionary<string, string>(data);
        node.data["Id"] = SunExpIds.SolarMemoryMapId;
        node.data["Type"] = "Event";
        node.data["Note"] = "普通事件";
        node.data["NodeId"] = SunExpIds.SolarMemoryFullEventId;
        node.data["Level"] = "-1";
        return node;
    }

    private static MapTree.Node CreateBossChainNode(MapTree tree, int indexInSegment, int segment)
    {
        return tree.TypeGenerate("首领");
    }

    private static void RewriteSelectNodesToBosses(MapTree tree)
    {
        for (var i = 0; i < tree.SelectNode.Count; i++)
        {
            if (IsBreakNode(tree.SelectNode[i]))
            {
                continue;
            }

            tree.SelectNode[i] = tree.TypeGenerate("首领");
        }
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
            && (!string.Equals(id, "cardpack_13", StringComparison.OrdinalIgnoreCase) || GameConfigManager.ShouldEnableOnlineCardPack());
    }

    private static void OpenBlessingStep()
    {
        var parent = UIManager.Instance?.upperCanvasTf ?? UIManager.Instance?.canvasTf;
        if (parent == null)
        {
            return;
        }

        var current = BlessingPickCount();
        if (current >= 5)
        {
            UIManager.Instance?.ShowTip("祝福挑选已完成", null);
            return;
        }

        new BlessingChoiceGenerator().CreateBlessUI(parent, () =>
        {
            var next = BlessingPickCount() + 1;
            PlayerApi.SetGameVar(SunExpIds.SolarMemoryBlessPickCountKey, Math.Min(5, next).ToString());
            if (next < 5)
            {
                OpenBlessingStep();
            }
            else
            {
                UIManager.Instance?.ShowTip("祝福挑选完成", null);
            }
        });
    }

    private static int BlessingPickCount()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarMemoryBlessPickCountKey, "0")));
    }

    public static void ConfigureSolarMemoryReservePool()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        var cardIds = SelectedPackCardIds();
        role.UnCardList.Clear();
        role.CardTopCount = Math.Max(role.CardTopCount, Math.Max(role.cardList.Count, cardIds.Count * 2));
        role.CardBottomCount = Math.Min(role.CardBottomCount, role.cardList.Count);
        role.MaxAlCardCount = Math.Max(0, cardIds.Count * 3);

        foreach (var cardId in cardIds)
        {
            role.UnCardList.Add(new DataConfig(cardId, DataType.Card));
            role.UnCardList.Add(new DataConfig(cardId, DataType.Card));
        }

        PlayerApi.SetGameVar(SunExpIds.SolarMemoryDeckConfiguredKey, "1");
        UIManager.Instance?.ShowTip("\u65e5\u8000\u56de\u5fc6\u5907\u9009\u724c\u5df2\u52a0\u5165\u5361\u5305\u724c x2", null);
    }

    private static List<string> SelectedPackCardIds()
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packId in CurrentPackSelection())
        {
            foreach (var pair in Singleton<GameConfigManager>.Instance.GetPackItems(packId))
            {
                if (pair.Key != DataType.Card)
                {
                    continue;
                }

                foreach (var card in pair.Value)
                {
                    if (card.TryGetValue("Id", out var id) && !string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        return ids;
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

    private static void AddOriginPoint(string key, Dictionary<string, Text> valueTexts, Text summary)
    {
        var points = OriginPoints();
        if (points <= 0)
        {
            UIManager.Instance?.ShowTip("本次回忆的本源点数已经用完", null);
            return;
        }

        if (RoleTable.Instance == null || !RoleTable.Instance.VarsMap.ContainsKey(key))
        {
            return;
        }

        RoleTable.Instance.UseVarsChanges(key, 1);
        PlayerApi.SetGameVar(SunExpIds.SolarMemoryOriginPointsKey, Math.Max(0, points - 1).ToString());
        RefreshOriginTexts(valueTexts, summary);
    }

    private static int OriginPoints()
    {
        var text = PlayerApi.GetGameVar(SunExpIds.SolarMemoryOriginPointsKey, "3");
        return Math.Max(0, DictionaryUtil.ParseInt(text));
    }

    private static void RefreshOriginTexts(Dictionary<string, Text> valueTexts, Text summary)
    {
        if (RoleTable.Instance == null)
        {
            return;
        }

        foreach (var pair in valueTexts)
        {
            pair.Value.text = RoleTable.Instance.VarsMap.TryGetValue(pair.Key, out var value) ? value.ToString() : "0";
        }

        summary.text = "剩余点数：" + OriginPoints();
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
