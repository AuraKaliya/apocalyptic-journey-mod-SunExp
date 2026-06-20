using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckRuntime
{
    private const string AppliedKey = "AuraTools.StarterDeckApplied";
    private const string Owner = "AuraTools.StarterDeck";
    private const string Scope = "AuraTools.WorldSimulation";
    private const string Mode = "AuraTools.WorldSimulation";
    private const string LegacyMode = "aura-world-simulation";
    public const float CardInfoHeaderHeight = 40f;
    public const float CardImageColumnWidth = 44f;
    public const float CardIconSize = 34f;
    public const float CardRarityColumnWidth = 70f;
    public const float CardCostColumnWidth = 56f;
    public const float CardActionColumnWidth = 120f;
    private static readonly Dictionary<string, Sprite?> cardIconCache = new(StringComparer.OrdinalIgnoreCase);
    private const string SunExpSolarMemoryModeKey = "SunExp_SolarMemoryMode";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", ApplyStarterDeckAfterRoleInit);
    }

    public static List<string> BuildAllCandidateCardIds()
    {
        return BuildSelectablePacks()
            .SelectMany(CardIdsFromPack)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    private static void ApplyStarterDeckAfterRoleInit(ModHookContext context)
    {
        try
        {
            if (!AuraToolsConfigService.Root.MatchExperience.Enabled
                || !AuraToolsConfigService.MatchExperience.StarterDeck.Enabled)
            {
                return;
            }

            var roleTable = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as RoleTable
                : RoleTable.Instance;
            if (roleTable == null)
            {
                return;
            }

            if (ShouldSkipForExternalOwner(roleTable) || IsApplied(roleTable))
            {
                return;
            }

            var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
            var deck = settings.CardIds
                .Where(IsValidCard)
                .Take(settings.DeckSize)
                .ToList();
            if (deck.Count != settings.DeckSize)
            {
                AuraToolsLog.Warn("[StarterDeck] skipped: preset is incomplete. "
                                  + deck.Count + "/" + settings.DeckSize);
                return;
            }

            var originalDeckCount = roleTable.cardList.Count;
            if (!StarterDeckArbiterRuntime.ApplyDeck(roleTable, deck, CreateClaim(settings.DeckSize)))
            {
                return;
            }

            AuraToolsLog.Info("[StarterDeck] applied world-simulation preset; originalDeck="
                              + originalDeckCount
                              + ", deck=" + roleTable.cardList.Count
                              + ", cards=" + string.Join("|", deck));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[StarterDeck] failed to apply preset", ex);
        }
    }

    private static bool ShouldSkipForExternalOwner(RoleTable roleTable)
    {
        if (IsSunExpSolarMemoryRun())
        {
            AuraToolsLog.Info("[StarterDeck] skipped: SunExp Solar Memory owns this run.");
            return true;
        }

        if (roleTable.SpecialVarMap == null)
        {
            return false;
        }

        if (StarterDeckArbiterRuntime.IsOwnedByOther(roleTable, Owner, out var owner))
        {
            AuraToolsLog.Info("[StarterDeck] skipped: starter deck owner=" + owner + ".");
            return true;
        }

        if (roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey + ".Mode", out var legacyMode)
            && string.Equals(legacyMode, "sunexp-solar-memory", StringComparison.OrdinalIgnoreCase))
        {
            AuraToolsLog.Info("[StarterDeck] skipped: CardPackExp compatibility owner is SunExp Solar Memory.");
            return true;
        }

        return false;
    }

    private static bool IsSunExpSolarMemoryRun()
    {
        try
        {
            return GameSaveManager.GetValue<string>(SunExpSolarMemoryModeKey) == "1";
        }
        catch
        {
            return false;
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            AuraToolsLog.Info("Hook registered: " + target);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static bool IsApplied(RoleTable roleTable)
    {
        if (StarterDeckArbiterRuntime.HasApplied(roleTable, AppliedKey, Owner))
        {
            return true;
        }

        return roleTable.SpecialVarMap != null
               && roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey, out var oldValue)
               && oldValue == "1"
               && roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey + ".Mode", out var legacyMode)
               && legacyMode.StartsWith("aura-", StringComparison.OrdinalIgnoreCase);
    }

    private static StarterDeckClaim CreateClaim(int deckSize)
    {
        return new StarterDeckClaim
        {
            Owner = Owner,
            Scope = Scope,
            ModeId = Mode,
            Source = "config",
            State = StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = AppliedKey,
            AppliedModeKey = AppliedKey + ".Mode",
            AppliedMode = LegacyMode,
            LegacyMode = LegacyMode,
            DeckSize = deckSize,
            SourceName = "AuraTools.WorldSimulation.StarterDeck"
        };
    }

    private static List<string> BuildSelectablePacks()
    {
        try
        {
            return Singleton<GameConfigManager>.Instance.GetTable(DataType.CardPack)
                .Getlines()
                .Where(row => row.TryGetValue("Id", out var id)
                              && IsValidPackForCurrentLobby(id)
                              && !Singleton<GameRuntimeData>.Instance.IsLocked(id))
                .Select(row => row["Id"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id)
                .ToList();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[StarterDeck] failed to list card packs: " + ex.Message);
            return new List<string>();
        }
    }

    private static IEnumerable<string> CardIdsFromPack(string packId)
    {
        foreach (var pair in Singleton<GameConfigManager>.Instance.GetPackItems(packId))
        {
            if (pair.Key != DataType.Card)
            {
                continue;
            }

            foreach (var card in pair.Value)
            {
                if (card.TryGetValue("Id", out var id))
                {
                    yield return id;
                }
            }
        }
    }

    private static bool IsValidPackForCurrentLobby(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
               && (!string.Equals(id, "cardpack_13", StringComparison.OrdinalIgnoreCase)
                   || GameConfigManager.ShouldEnableOnlineCardPack());
    }

    private static bool IsValidCard(string cardId)
    {
        try
        {
            return new DataConfig(cardId, DataType.Card).data != null;
        }
        catch
        {
            return false;
        }
    }

    private static string CardSortKey(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var rarity = data.TryGetValue("Rarity", out var r) ? r : "9";
            var cost = data.TryGetValue("Expend", out var c) ? c : "9";
            return rarity.PadLeft(2, '0') + "|" + cost.PadLeft(2, '0') + "|" + cardId;
        }
        catch
        {
            return "99|99|" + cardId;
        }
    }

    public static string CardDisplayName(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }

            return data.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : cardId;
        }
        catch
        {
            return cardId;
        }
    }

    public static string CardShortInfo(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var rarity = data.TryGetValue("Rarity", out var r) ? "R" + r : "R?";
            var cost = data.TryGetValue("Expend", out var c) ? c : "?";
            return rarity + " / 费 " + cost + " / " + cardId;
        }
        catch
        {
            return cardId;
        }
    }

    public static string CardRarity(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return data.TryGetValue("Rarity", out var rarity) && !string.IsNullOrWhiteSpace(rarity) ? "R" + rarity : "?";
        }
        catch
        {
            return "?";
        }
    }

    public static string CardCost(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return data.TryGetValue("Expend", out var cost) && !string.IsNullOrWhiteSpace(cost) ? cost : "?";
        }
        catch
        {
            return "?";
        }
    }

    public static Sprite? TryLoadCardIcon(string cardId)
    {
        if (cardIconCache.TryGetValue(cardId, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            if (data.TryGetValue("Icon", out var iconPath) && !string.IsNullOrWhiteSpace(iconPath))
            {
                sprite = ResourceLoader.Load<Sprite>(iconPath, true);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[StarterDeck] failed to load card icon for " + cardId + ": " + ex.Message);
        }

        cardIconCache[cardId] = sprite;
        return sprite;
    }
}

public static class AuraToolsStarterDeckEditor
{
    private static readonly List<string> editingDeck = new();
    private static Transform? selectedContent;
    private static Text? counterText;
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        editingDeck.Clear();
        editingDeck.AddRange(AuraToolsConfigService.MatchExperience.StarterDeck.CardIds);

        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.StarterDeckEditor", parent, "【世界推演】开局卡组配置");
        var candidates = AuraToolsStarterDeckRuntime.BuildAllCandidateCardIds();

        var body = Settings.AuraToolsUi.CreateLayout("Body", window.transform);
        var bodyElement = body.AddComponent<LayoutElement>();
        bodyElement.flexibleHeight = 1f;
        bodyElement.minHeight = 420f;
        var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 12f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;

        var candidatePanel = CreateColumn(body.transform, "全部可选卡牌", out _);
        foreach (var cardId in candidates)
        {
            CreateCandidateRow(candidatePanel, cardId);
        }

        var selectedPanel = CreateColumn(body.transform, "当前预设", out counterText);
        selectedContent = selectedPanel;

        var footer = Settings.AuraToolsUi.CreateLayout("Footer", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(footer, Settings.AuraToolsUi.FooterHeight);
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 10f;
        footerLayout.childControlHeight = true;
        footerLayout.childControlWidth = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;
        hintText = Settings.AuraToolsUi.AddText(footer.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(footer.transform, "自动填充", () =>
        {
            editingDeck.Clear();
            editingDeck.AddRange(candidates.Take(AuraToolsConfigService.MatchExperience.StarterDeck.DeckSize));
            RefreshSelected();
        });
        Settings.AuraToolsUi.AddButton(footer.transform, "清空", () =>
        {
            editingDeck.Clear();
            RefreshSelected();
        });
        Settings.AuraToolsUi.AddButton(footer.transform, "保存", Save);

        RefreshSelected();
    }

    private static Transform CreateColumn(Transform parent, string title, out Text? counter)
    {
        var column = Settings.AuraToolsUi.CreateLayout("Column-" + title, parent);
        column.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var layout = column.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = Settings.AuraToolsUi.CreateLayout("Header", column.transform);
        Settings.AuraToolsUi.SetFixedHeight(header, Settings.AuraToolsUi.ColumnHeaderHeight);
        Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(10, 10, 2, 2);
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddText(header.transform, title, Settings.AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
        counter = Settings.AuraToolsUi.AddText(header.transform, "", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleRight, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 0f, 110f);

        CreateCardInfoHeader(column.transform);
        return Settings.AuraToolsUi.CreateScroll(column.transform, title);
    }

    private static void CreateCandidateRow(Transform parent, string cardId)
    {
        var row = CreateRow(parent, "Candidate-" + cardId);
        CreateCardIconCell(row.transform, cardId, AuraToolsStarterDeckRuntime.CardCost(cardId));
        Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardDisplayName(cardId), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardRarity(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
        Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardCost(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
        Settings.AuraToolsUi.AddButton(row.transform, "添加", () =>
        {
            if (editingDeck.Count >= AuraToolsConfigService.MatchExperience.StarterDeck.DeckSize)
            {
                SetHint("预设已满，请先移除一张。");
                return;
            }

            editingDeck.Add(cardId);
            RefreshSelected();
        }, 70f, 30f);
    }

    private static void RefreshSelected()
    {
        if (selectedContent == null)
        {
            return;
        }

        Settings.AuraToolsUi.ClearChildren(selectedContent);
        for (var i = 0; i < editingDeck.Count; i++)
        {
            var index = i;
            var cardId = editingDeck[i];
            var row = CreateRow(selectedContent, "Selected-" + index);
            CreateCardIconCell(row.transform, cardId, (index + 1).ToString());
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardDisplayName(cardId), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardRarity(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardCost(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
            Settings.AuraToolsUi.AddButton(row.transform, "移除", () =>
            {
                if (index >= 0 && index < editingDeck.Count)
                {
                    editingDeck.RemoveAt(index);
                    RefreshSelected();
                }
            }, 70f, 30f);
        }

        var size = AuraToolsConfigService.MatchExperience.StarterDeck.DeckSize;
        if (counterText != null)
        {
            counterText.text = editingDeck.Count + "/" + size;
            counterText.color = editingDeck.Count == size ? new Color(0.58f, 0.94f, 0.62f) : Settings.AuraToolsUi.Text;
        }

        SetHint(editingDeck.Count == size ? "预设完整，可以保存。" : "需要配置满 " + size + " 张牌。");
    }

    private static void CreateCardInfoHeader(Transform parent)
    {
        var header = Settings.AuraToolsUi.CreateLayout("CardInfoHeader", parent);
        Settings.AuraToolsUi.SetFixedHeight(header, AuraToolsStarterDeckRuntime.CardInfoHeaderHeight);
        Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 0, 0);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        Settings.AuraToolsUi.AddText(header.transform, "卡图", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardImageColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "卡牌名称", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddText(header.transform, "稀有度", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "费用", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardActionColumnWidth);
    }

    private static void CreateCardIconCell(Transform parent, string cardId, string fallbackText)
    {
        var sprite = AuraToolsStarterDeckRuntime.TryLoadCardIcon(cardId);
        var cell = Settings.AuraToolsUi.CreateLayout("CardIcon", parent);
        var element = Settings.AuraToolsUi.EnsureLayoutElement(cell);
        element.minWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.preferredWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.minHeight = Settings.AuraToolsUi.TextMinHeight;
        element.preferredHeight = Settings.AuraToolsUi.TextMinHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        if (sprite == null)
        {
            Settings.AuraToolsUi.AddImage(cell, new Color(0.025f, 0.022f, 0.045f, 0.98f));
            Settings.AuraToolsUi.AddFillText(cell.transform, fallbackText, Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent);
            return;
        }

        var icon = Settings.AuraToolsUi.CreateRect("Image", cell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(AuraToolsStarterDeckRuntime.CardIconSize, AuraToolsStarterDeckRuntime.CardIconSize));
        var image = icon.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        var row = Settings.AuraToolsUi.CreateLayout(name, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, Settings.AuraToolsUi.DataRowHeight);
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static void Save()
    {
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        if (editingDeck.Count != settings.DeckSize)
        {
            SetHint("保存失败：需要正好 " + settings.DeckSize + " 张牌。");
            return;
        }

        settings.CardIds = editingDeck.ToList();
        AuraToolsConfigService.SaveMatchExperience();
        SetHint("已保存全局开局卡组预设。");
    }

    private static void SetHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }
}
