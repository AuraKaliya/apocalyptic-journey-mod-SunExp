using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public static class DimensionShopPanel
{
    private const string PanelName = "SunExp_DimensionShopPanel";
    private static readonly Color WindowTint = new(0.035f, 0.035f, 0.055f, 0.985f);
    private static readonly Color HeaderTint = new(0.075f, 0.055f, 0.08f, 0.98f);
    private static readonly Color ItemTint = new(0.07f, 0.075f, 0.095f, 0.98f);
    private static readonly Color Accent = new(0.32f, 0.86f, 0.76f);
    private static readonly Color Crystal = new(0.6f, 0.88f, 1f);
    private static readonly Color SoftText = new(0.92f, 0.94f, 0.96f);
    private static readonly Color MutedText = new(0.64f, 0.67f, 0.72f);
    private static GameObject? activePanel;
    private static Transform? productRoot;
    private static Text? balanceText;
    private static Text? hintText;
    private static Button? refreshButton;
    private static DimensionShopNativeSkin? nativeSkin;
    private static bool busy;

    public static bool IsOpen => activePanel != null;

    public static void Open(string source)
    {
        if (activePanel != null)
        {
            Render();
            return;
        }

        try
        {
            var parent = SunExpModalHost.NativeUiParent();
            if (parent == null)
            {
                SunExpLog.Warn("[DimensionShop] native UI parent is unavailable from " + source + ".");
                DimensionShopGameApi.AdvanceMap();
                return;
            }

            activePanel = SunExpModalHost.CreateNativeFullscreenRoot(
                PanelName,
                new Color(0f, 0f, 0f, 0.72f));
            if (activePanel == null)
            {
                SunExpLog.Warn("[DimensionShop] native UI root could not be created from " + source + ".");
                DimensionShopGameApi.AdvanceMap();
                return;
            }
            SunExpTransientUiRegistry.Register("DimensionShop", Close);
            if (DimensionShopNativeSkin.TryCreate(
                    activePanel.transform,
                    () => Purchase(DimensionShopService.BuyCard),
                    () => Purchase(DimensionShopService.BuyRelic),
                    SellCard,
                    SellRelic,
                    UnequipRelic,
                    Refresh,
                    Leave,
                    out nativeSkin))
            {
                Render();
                SunExpLog.Info(nativeSkin != null
                    ? "[DimensionShop] opened with native ShopUI skin from " + source + "."
                    : "[DimensionShop] native ShopUI render was incompatible; opened fallback panel from " + source + ".");
                return;
            }

            CreateFallbackPanel();
            Render();
            SunExpLog.Info("[DimensionShop] opened from " + source + ".");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShop] panel open failed", ex);
            Close("DimensionShop.OpenFailed");
            DimensionShopGameApi.AdvanceMap();
        }
    }

    public static void Close(string source)
    {
        nativeSkin?.Dispose();
        nativeSkin = null;
        if (productRoot != null)
        {
            SunExpUiPool.ReleaseOrDestroyChildren(productRoot, "DimensionShop.Close", "[DimensionShop]");
        }

        productRoot = null;
        balanceText = null;
        hintText = null;
        refreshButton = null;
        busy = false;
        SunExpModalHost.Close(ref activePanel, source, "[DimensionShop]");
        SunExpTransientUiRegistry.Unregister("DimensionShop");
    }

    private static void CreateHeader(Transform parent)
    {
        var header = SunExpUiComponents.CreatePanelSection(
            "Header",
            parent,
            SunExpUiSprites.Panel("[DimensionShop]"),
            HeaderTint,
            88f,
            88f);
        SunExpUiComponents.ConfigureHorizontalLayout(
            header,
            new RectOffset(18, 18, 10, 10),
            16f,
            childForceExpandHeight: true);
        SunExpUiComponents.AddTextBlock(
            header.transform,
            "\u6b21\u5143\u5546\u5e97",
            30,
            TextAnchor.MiddleLeft,
            Accent,
            58f,
            1f);
        balanceText = SunExpUiComponents.AddTextBlock(
            header.transform,
            "",
            19,
            TextAnchor.MiddleRight,
            Crystal,
            58f,
            0f,
            240f);
    }

    private static Transform CreateProductRoot(Transform parent)
    {
        var root = SunExpUiComponents.CreatePanelSection(
            "Products",
            parent,
            SunExpUiSprites.Panel("[DimensionShop]"),
            new Color(0.018f, 0.02f, 0.032f, 0.94f),
            390f,
            390f,
            1f);
        SunExpUiComponents.ConfigureHorizontalLayout(
            root,
            new RectOffset(18, 18, 18, 18),
            18f,
            childControlWidth: true,
            childControlHeight: true,
            childForceExpandWidth: true,
            childForceExpandHeight: true);
        return root.transform;
    }

    private static void CreateFooter(Transform parent)
    {
        var footer = SunExpUiComponents.CreateFooterRow(parent, 64f, new RectOffset(6, 6, 6, 6), 12f);
        hintText = SunExpUiComponents.AddTextBlock(
            footer.transform,
            "",
            14,
            TextAnchor.MiddleLeft,
            SoftText,
            46f,
            1f);
        refreshButton = SunExpUiComponents.CreateTextButton(
            footer.transform,
            "\u5237\u65b0",
            new Vector2(128f, 48f),
            SunExpUiSprites.Button("[DimensionShop]"),
            new Color(0.055f, 0.09f, 0.1f, 1f),
            SoftText,
            17,
            Refresh);
        SunExpUiComponents.CreateTextButton(
            footer.transform,
            "\u79bb\u5f00",
            new Vector2(112f, 48f),
            SunExpUiSprites.Button("[DimensionShop]"),
            new Color(0.1f, 0.06f, 0.075f, 1f),
            SoftText,
            17,
            Leave);
    }

    private static void Render()
    {
        var view = DimensionShopService.View();
        if (nativeSkin != null)
        {
            try
            {
                nativeSkin.Render(view, busy);
                return;
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[DimensionShop] native ShopUI render failed; switching to fallback panel: " + ex.Message);
                nativeSkin.Dispose();
                nativeSkin = null;
                CreateFallbackPanel();
            }
        }

        if (productRoot == null)
        {
            return;
        }

        SunExpUiPool.ReleaseOrDestroyChildren(productRoot, "DimensionShop.Render", "[DimensionShop]");
        balanceText!.text = "\u771f\u7406\u4e4b\u6676  " + view.Truth;
        CreateProductCard(productRoot, "\u5361\u724c", view.Card, DimensionShopService.BuyCard);
        CreateProductCard(productRoot, "\u9057\u7269", view.Relic, DimensionShopService.BuyRelic);
        SetHint("\u5237\u65b0\u6d88\u8017 "
                + view.RefreshPrice
                + " \u679a\u771f\u7406\u4e4b\u6676\uff0c\u5df2\u5237\u65b0 "
                + view.RefreshCount
                + " \u6b21\u3002");

        if (refreshButton != null)
        {
            refreshButton.interactable = view.CanRefresh && !busy;
        }
    }

    private static void CreateFallbackPanel()
    {
        if (activePanel == null || productRoot != null)
        {
            return;
        }

        var parent = activePanel.transform.parent ?? activePanel.transform;
        var window = SunExpUiComponents.CreateVerticalWindow(
            "Window",
            activePanel.transform,
            ResolveWindowSize(parent),
            SunExpUiSprites.Panel("[DimensionShop]"),
            WindowTint,
            new RectOffset(24, 24, 20, 18),
            14f);

        CreateHeader(window.transform);
        productRoot = CreateProductRoot(window.transform);
        CreateFooter(window.transform);
    }

    private static void CreateProductCard(
        Transform parent,
        string kind,
        DimensionShopItemView item,
        BuyAction purchase)
    {
        var card = SunExpUiComponents.CreateLayoutObject("Product-" + kind, parent);
        var element = card.AddComponent<LayoutElement>();
        element.minWidth = 280f;
        element.flexibleWidth = 1f;
        element.minHeight = 350f;
        element.flexibleHeight = 1f;
        SunExpUiBuilder.ApplyPanelImage(card, SunExpUiSprites.Panel("[DimensionShop]"), ItemTint, true);
        SunExpUiComponents.ConfigureVerticalLayout(
            card,
            new RectOffset(16, 16, 12, 14),
            8f,
            childForceExpandHeight: false,
            alignment: TextAnchor.UpperCenter);

        SunExpUiComponents.AddTextBlock(card.transform, kind, 16, TextAnchor.MiddleCenter, Accent, 28f);
        CreateIcon(card.transform, item.IconPath);
        SunExpUiComponents.AddTextBlock(
            card.transform,
            string.IsNullOrWhiteSpace(item.Name) ? "\u6682\u65e0\u5546\u54c1" : item.Name,
            22,
            TextAnchor.MiddleCenter,
            SoftText,
            38f);
        SunExpUiComponents.AddTextBlock(
            card.transform,
            item.Description,
            14,
            TextAnchor.UpperLeft,
            string.IsNullOrWhiteSpace(item.Description) ? MutedText : SoftText,
            96f,
            1f);
        SunExpUiComponents.AddTextBlock(
            card.transform,
            string.IsNullOrWhiteSpace(item.Status) ? "\u53ef\u8d2d\u4e70" : item.Status,
            15,
            TextAnchor.MiddleCenter,
            item.CanBuy ? Accent : MutedText,
            30f);

        var button = SunExpUiComponents.CreateTextButton(
            card.transform,
            "\u8d2d\u4e70  " + item.Price,
            new Vector2(156f, 46f),
            SunExpUiSprites.Button("[DimensionShop]"),
            new Color(0.055f, 0.09f, 0.1f, 1f),
            SoftText,
            17,
            () => Purchase(purchase));
        button.interactable = item.CanBuy && !busy;
    }

    private static void CreateIcon(Transform parent, string path)
    {
        var root = SunExpUiComponents.CreateLayoutObject("Icon", parent);
        var element = root.AddComponent<LayoutElement>();
        element.minHeight = 126f;
        element.preferredHeight = 126f;
        element.flexibleWidth = 1f;
        var image = root.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.96f);
        image.preserveAspect = true;
        image.raycastTarget = false;
        if (!string.IsNullOrWhiteSpace(path))
        {
            image.sprite = SunExpResourceCache.Load<Sprite>(path, true, "dimension.shop.item");
        }
    }

    private static void Purchase(BuyAction action)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        try
        {
            action(out var message);
            busy = false;
            Render();
            SetHint(message);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShop] purchase action failed", ex);
            SetHint("\u7ed3\u7b97\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002");
        }
        finally
        {
            busy = false;
        }
    }

    private static void Refresh()
    {
        if (busy)
        {
            return;
        }

        busy = true;
        try
        {
            DimensionShopService.Refresh(out var message);
            busy = false;
            Render();
            SetHint(message);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShop] refresh failed", ex);
            SetHint("\u5237\u65b0\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002");
        }
        finally
        {
            busy = false;
        }
    }

    private static void SellCard(string instanceId)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        try
        {
            DimensionShopGameApi.HideFloatingWindow();
            DimensionShopService.SellCard(instanceId, out var message);
            busy = false;
            Render();
            SetHint(message);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShop] card sale failed", ex);
            SetHint("\u5361\u724c\u51fa\u552e\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002");
        }
        finally
        {
            busy = false;
        }
    }

    private static void SellRelic(string instanceId)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        try
        {
            DimensionShopGameApi.HideFloatingWindow();
            DimensionShopService.SellRelic(instanceId, out var message);
            busy = false;
            Render();
            SetHint(message);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShop] relic sale failed", ex);
            SetHint("\u9057\u7269\u51fa\u552e\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002");
        }
        finally
        {
            busy = false;
        }
    }

    private static void UnequipRelic(string instanceId)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        try
        {
            DimensionShopGameApi.HideFloatingWindow();
            DimensionShopService.UnequipRelic(instanceId, out var message);
            busy = false;
            Render();
            SetHint(message);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShop] relic unequip failed", ex);
            SetHint("\u9057\u7269\u8131\u4e0b\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002");
        }
        finally
        {
            busy = false;
        }
    }

    private static void Leave()
    {
        if (busy)
        {
            return;
        }

        busy = true;
        Close("DimensionShop.Leave");
        DimensionShopGameApi.AdvanceMap();
    }

    private static void SetHint(string value)
    {
        nativeSkin?.SetHint(value);
        if (hintText != null)
        {
            hintText.text = value ?? "";
        }
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var rect = parent as RectTransform;
        var width = rect != null && rect.rect.width > 0f ? rect.rect.width : 1280f;
        var height = rect != null && rect.rect.height > 0f ? rect.rect.height : 720f;
        return new Vector2(Mathf.Clamp(width * 0.76f, 760f, 980f), Mathf.Clamp(height * 0.82f, 570f, 700f));
    }

    private delegate bool BuyAction(out string message);
}
