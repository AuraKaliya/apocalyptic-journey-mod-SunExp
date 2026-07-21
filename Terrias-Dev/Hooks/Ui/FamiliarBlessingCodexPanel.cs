using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;

namespace Terrias.Dll.Hooks.Ui;

public static class FamiliarBlessingCodexPanel
{
    private const string PanelName = "Terrias_FamiliarBlessingCodexPanel";
    private const string RegistryKey = "FamiliarBlessingCodex";
    private const string LogPrefix = "[FamiliarBlessingCodex]";
    private const float PoolPanelWidth = 224f;
    private const float CardHeight = 78f;
    private const float CardSpacing = 10f;

    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.76f);
    private static readonly Color WindowTint = new(0.025f, 0.028f, 0.075f, 0.99f);
    private static readonly Color HeaderTint = new(0.05f, 0.044f, 0.10f, 0.98f);
    private static readonly Color SectionTint = new(0.035f, 0.037f, 0.085f, 0.98f);
    private static readonly Color CardTint = new(0.075f, 0.07f, 0.14f, 0.98f);
    private static readonly Color PoolTint = new(0.075f, 0.07f, 0.14f, 0.96f);
    private static readonly Color SelectedPoolTint = new(0.18f, 0.13f, 0.24f, 0.98f);
    private static readonly Color Gold = new(0.88f, 0.78f, 0.48f);
    private static readonly Color Pale = new(0.92f, 0.88f, 0.72f);
    private static readonly Color Muted = new(0.72f, 0.68f, 0.62f);
    private static readonly Color CommonTier = new(0.30f, 0.42f, 0.34f, 0.98f);
    private static readonly Color RareTier = new(0.24f, 0.36f, 0.56f, 0.98f);
    private static readonly Color EpicTier = new(0.46f, 0.28f, 0.58f, 0.98f);
    private static readonly Color FinalTier = new(0.58f, 0.32f, 0.18f, 0.98f);

    private static GameObject? activePanel;
    private static Transform? poolContent;
    private static Transform? cardContent;
    private static Text? poolTitleText;
    private static IReadOnlyList<FamiliarBlessingCodexPool> pools = Array.Empty<FamiliarBlessingCodexPool>();
    private static string selectedPoolId = "";

    public static bool IsOpen => activePanel != null;

    public static void Open()
    {
        if (activePanel != null)
        {
            activePanel.transform.SetAsLastSibling();
            Refresh();
            return;
        }

        try
        {
            var parent = TerriasModalHost.ModalParent();
            if (parent == null)
            {
                TerriasLog.Warn(LogPrefix + " modal parent is unavailable.");
                return;
            }

            pools = FamiliarBlessingCodexService.Pools();
            SelectInitialPool();
            activePanel = TerriasModalHost.CreateFullscreenRoot(PanelName, parent, Backdrop);
            TerriasTransientUiRegistry.Register(RegistryKey, Close);

            var windowSize = ResolveWindowSize(parent);
            var window = TerriasUiComponents.CreateVerticalWindow(
                "Window",
                activePanel.transform,
                windowSize,
                TerriasUiSprites.Panel(LogPrefix),
                WindowTint,
                new RectOffset(22, 22, 16, 14),
                8f);

            CreateHeader(window.transform);
            CreateBody(window.transform, windowSize);
            CreateFooter(window.transform);
            Refresh();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Familiar blessing codex failed", ex);
            Close("FamiliarBlessingCodex.OpenFailed");
        }
    }

    public static void Close(string source)
    {
        ReleaseChildren(poolContent, "pool");
        ReleaseChildren(cardContent, "card");
        poolContent = null;
        cardContent = null;
        poolTitleText = null;
        pools = Array.Empty<FamiliarBlessingCodexPool>();
        selectedPoolId = "";
        TerriasModalHost.Close(ref activePanel, source, LogPrefix);
        TerriasTransientUiRegistry.Unregister(RegistryKey);
    }

    private static void CreateHeader(Transform parent)
    {
        var header = TerriasUiComponents.CreatePanelSection(
            "Header",
            parent,
            TerriasUiSprites.Panel(LogPrefix),
            HeaderTint,
            50f,
            50f);
        TerriasUiComponents.ConfigureVerticalLayout(header, new RectOffset(12, 12, 6, 6), 0f);
        TerriasUiComponents.AddTextBlock(header.transform, "使魔祝福图鉴", 27, TextAnchor.MiddleCenter, Gold, 38f);
    }

    private static void CreateBody(Transform parent, Vector2 windowSize)
    {
        var body = TerriasUiComponents.CreateLayoutObject("Body", parent);
        var bodyElement = AuraUiComponents.EnsureLayoutElement(body);
        bodyElement.minHeight = 360f;
        bodyElement.flexibleHeight = 1f;
        TerriasUiComponents.ConfigureHorizontalLayout(
            body,
            new RectOffset(0, 0, 0, 0),
            14f,
            childForceExpandWidth: false,
            childForceExpandHeight: true,
            alignment: TextAnchor.UpperLeft);

        CreatePoolSection(body.transform);
        CreateCardSection(body.transform, windowSize);
    }

    private static void CreatePoolSection(Transform parent)
    {
        var section = TerriasUiComponents.CreatePanelSection(
            "PoolSection",
            parent,
            TerriasUiSprites.Panel(LogPrefix),
            SectionTint,
            360f,
            360f,
            1f);
        var element = AuraUiComponents.EnsureLayoutElement(section);
        element.minWidth = PoolPanelWidth;
        element.preferredWidth = PoolPanelWidth;
        element.flexibleWidth = 0f;
        TerriasUiComponents.ConfigureVerticalLayout(section, new RectOffset(10, 10, 10, 10), 8f);
        TerriasUiComponents.AddTextBlock(section.transform, "祝福池", 19, TextAnchor.MiddleLeft, Gold, 30f);
        poolContent = TerriasUiComponents.CreateVerticalScrollArea(
            section.transform,
            "BlessingPools",
            280f,
            1f,
            6f,
            22f,
            new Color(0f, 0f, 0f, 0.01f)).Content;
    }

    private static void CreateCardSection(Transform parent, Vector2 windowSize)
    {
        var section = TerriasUiComponents.CreatePanelSection(
            "CardSection",
            parent,
            TerriasUiSprites.Panel(LogPrefix),
            SectionTint,
            360f,
            360f,
            1f);
        var element = AuraUiComponents.EnsureLayoutElement(section);
        element.minWidth = 520f;
        element.flexibleWidth = 1f;
        TerriasUiComponents.ConfigureVerticalLayout(section, new RectOffset(12, 12, 10, 10), 8f);
        poolTitleText = TerriasUiComponents.AddTextBlock(section.transform, "", 20, TextAnchor.MiddleLeft, Gold, 30f);

        var rightWidth = windowSize.x - 44f - 14f - PoolPanelWidth - 24f;
        var cardWidth = Mathf.Max(250f, (rightWidth - CardSpacing) * 0.5f);
        cardContent = TerriasUiComponents.CreateUniformGridScrollArea(
            section.transform,
            "BlessingCards",
            280f,
            1f,
            2,
            new Vector2(cardWidth, CardHeight),
            new Vector2(CardSpacing, CardSpacing),
            new RectOffset(0, 0, 0, 0),
            28f,
            new Color(0f, 0f, 0f, 0.01f)).Content;
    }

    private static void CreateFooter(Transform parent)
    {
        var footer = TerriasUiComponents.CreateFooterRow(parent, 42f, new RectOffset(10, 10, 4, 4), 10f);
        TerriasUiBuilder.ApplyPanelImage(footer, TerriasUiSprites.Panel(LogPrefix), HeaderTint, true);
        TerriasUiComponents.AddTextBlock(footer.transform, "", 13, TextAnchor.MiddleLeft, Pale, 34f, 1f);
        TerriasUiComponents.CreateTextButton(
            footer.transform,
            "关闭",
            new Vector2(112f, 34f),
            TerriasUiSprites.Button(LogPrefix),
            HeaderTint,
            Pale,
            15,
            () => Close("FamiliarBlessingCodex.CloseButton"));
    }

    private static void Refresh()
    {
        pools = FamiliarBlessingCodexService.Pools();
        SelectInitialPool();
        RefreshPoolButtons();
        RefreshCards();
    }

    private static void SelectInitialPool()
    {
        if (pools.Any(pool => string.Equals(pool.Id, selectedPoolId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        selectedPoolId = pools.FirstOrDefault()?.Id ?? "";
    }

    private static void RefreshPoolButtons()
    {
        if (poolContent == null)
        {
            return;
        }

        ReleaseChildren(poolContent, "pool");
        foreach (var pool in pools)
        {
            var poolId = pool.Id;
            var selected = string.Equals(poolId, selectedPoolId, StringComparison.OrdinalIgnoreCase);
            TerriasUiComponents.CreateTextButton(
                poolContent,
                pool.Name + "  " + pool.Blessings.Count,
                new Vector2(PoolPanelWidth - 20f, 40f),
                null,
                selected ? SelectedPoolTint : PoolTint,
                selected ? Gold : Pale,
                14,
                () => SelectPool(poolId));
        }
    }

    private static void SelectPool(string poolId)
    {
        if (string.Equals(selectedPoolId, poolId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        selectedPoolId = poolId;
        RefreshPoolButtons();
        RefreshCards();
    }

    private static void RefreshCards()
    {
        if (cardContent == null)
        {
            return;
        }

        ReleaseChildren(cardContent, "card");
        var selected = pools.FirstOrDefault(pool => string.Equals(pool.Id, selectedPoolId, StringComparison.OrdinalIgnoreCase));
        if (poolTitleText != null)
        {
            poolTitleText.text = selected == null ? "暂无祝福" : selected.Name + "（" + selected.Blessings.Count + "）";
        }

        if (selected == null)
        {
            return;
        }

        foreach (var blessing in selected.Blessings)
        {
            TerriasUiComponents.CreateBadgeContentCard(
                cardContent,
                "Blessing-" + blessing.Id,
                blessing.TierLabel,
                blessing.Name,
                ParsedDescription(blessing),
                66f,
                22f,
                40f,
                TerriasUiSprites.Label(LogPrefix),
                CardTint,
                TierColor(blessing.Tier),
                Pale,
                Gold,
                Pale);
        }
    }

    private static string ParsedDescription(FamiliarBlessingCodexEntry blessing)
    {
        if (string.IsNullOrWhiteSpace(blessing.Description))
        {
            return "暂无效果说明。";
        }

        try
        {
            return blessing.Description.Description();
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " failed to parse blessing description for " + blessing.Id + ": " + ex.Message);
            return blessing.Description;
        }
    }

    private static Color TierColor(int tier)
    {
        return tier switch
        {
            1 => CommonTier,
            2 => RareTier,
            3 => EpicTier,
            _ => FinalTier
        };
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var available = new Vector2(Screen.width, Screen.height);
        if (parent is RectTransform rect && rect.rect.width > 0f && rect.rect.height > 0f)
        {
            available = rect.rect.size;
        }

        return new Vector2(
            Mathf.Min(1180f, Mathf.Max(860f, available.x - 70f)),
            Mathf.Min(780f, Mathf.Max(600f, available.y - 40f)));
    }

    private static void ReleaseChildren(Transform? content, string area)
    {
        if (content != null)
        {
            TerriasUiPool.ReleaseOrDestroyChildren(content, "FamiliarBlessingCodex." + area, LogPrefix);
        }
    }
}
