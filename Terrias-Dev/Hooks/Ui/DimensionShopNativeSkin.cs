using System;
using System.Collections.Generic;
using AuraUi.Shared;
using AuraGameData.Shared.GameApi;
using Michsky.MUIP;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class DimensionShopNativeSkin : IDisposable
{
    private const string NativeShopResourcePath = "UI/ShopUI";
    private const string GeneratedPrefix = "TerriasDimensionShop_";
    private const string OfferCurrencyIconPath = "val/Icon";
    private const string BackpackGoldIconPath = "icon";
    private const string DefaultBackpackHint = "\u9f20\u6807\u53f3\u952e\u53ef\u4ee5\u51fa\u552e\u5361\u724c";
    private static readonly Color InsufficientColor = new(0.95f, 0.22f, 0.24f, 1f);
    private static readonly Color TruthColor = new(0.96f, 0.86f, 0.46f, 1f);

    private readonly GameObject shell;
    private readonly Transform shopRoot;
    private readonly GameObject offerTemplate;
    private readonly GameObject heldCardTemplate;
    private readonly GameObject heldRelicTemplate;
    private readonly Transform heldCardRoot;
    private readonly Transform heldRelicRoot;
    private readonly GameObject? heldRelicNullTemplate;
    private readonly TMP_Text goldBalanceText;
    private readonly TMP_Text truthBalanceText;
    private readonly Sprite? truthCurrencySprite;
    private readonly DimensionShopNativeHintPresenter hintPresenter;
    private readonly AuraUiNativeButtonBinding refreshButton;
    private readonly AuraUiNativeButtonBinding exitButton;
    private readonly Action<int> buyCard;
    private readonly Action<int> buyRelic;
    private readonly Action<string> sellCard;
    private readonly Action<string> sellRelic;
    private readonly Action<string> unequipRelic;
    private readonly List<GameObject> generated = new();
    private readonly HashSet<string> interactionLayoutWarnings = new(StringComparer.Ordinal);

    private bool disposed;
    private long overlayGeneration;

    private DimensionShopNativeSkin(
        GameObject shell,
        Transform shopRoot,
        GameObject offerTemplate,
        GameObject heldCardTemplate,
        GameObject heldRelicTemplate,
        Transform heldCardRoot,
        Transform heldRelicRoot,
        GameObject? heldRelicNullTemplate,
        TMP_Text goldBalanceText,
        TMP_Text truthBalanceText,
        Sprite? truthCurrencySprite,
        DimensionShopNativeHintPresenter hintPresenter,
        AuraUiNativeButtonBinding refreshButton,
        AuraUiNativeButtonBinding exitButton,
        Action<int> buyCard,
        Action<int> buyRelic,
        Action<string> sellCard,
        Action<string> sellRelic,
        Action<string> unequipRelic)
    {
        this.shell = shell;
        this.shopRoot = shopRoot;
        this.offerTemplate = offerTemplate;
        this.heldCardTemplate = heldCardTemplate;
        this.heldRelicTemplate = heldRelicTemplate;
        this.heldCardRoot = heldCardRoot;
        this.heldRelicRoot = heldRelicRoot;
        this.heldRelicNullTemplate = heldRelicNullTemplate;
        this.goldBalanceText = goldBalanceText;
        this.truthBalanceText = truthBalanceText;
        this.truthCurrencySprite = truthCurrencySprite;
        this.hintPresenter = hintPresenter;
        this.refreshButton = refreshButton;
        this.exitButton = exitButton;
        this.buyCard = buyCard;
        this.buyRelic = buyRelic;
        this.sellCard = sellCard;
        this.sellRelic = sellRelic;
        this.unequipRelic = unequipRelic;
    }

    public static bool TryCreate(
        Transform parent,
        Action<int> buyCard,
        Action<int> buyRelic,
        Action<string> sellCard,
        Action<string> sellRelic,
        Action<string> unequipRelic,
        Action refresh,
        Action leave,
        out DimensionShopNativeSkin? view)
    {
        view = null;
        GameObject? shell = null;
        try
        {
            var prefab = TerriasResourceCache.Load<GameObject>(
                NativeShopResourcePath,
                loadFromMod: false,
                category: "dimension.shop.native.shell");
            var source = prefab?.GetComponent<ShopUI>();
            if (prefab == null
                || source == null
                || source.ShopTran == null
                || source.ItemPrefab == null
                || source.SellCardPrefab == null
                || source.TopRelicPrefab == null)
            {
                TerriasLog.Warn("[DimensionShop] native ShopUI prefab or serialized templates are unavailable.");
                return false;
            }

            var shopPath = RelativePath(prefab.transform, source.ShopTran);
            var offerPath = RelativePath(prefab.transform, source.ItemPrefab.transform);
            var cardTemplatePath = RelativePath(prefab.transform, source.SellCardPrefab.transform);
            var relicTemplatePath = RelativePath(prefab.transform, source.TopRelicPrefab.transform);
            var nullTemplateSource = source.TopRelicPrefab.transform.parent.Find("NullPrefab");
            var nullTemplatePath = nullTemplateSource == null
                ? ""
                : RelativePath(prefab.transform, nullTemplateSource);

            shell = CreateShell(parent);
            CloneRootVisualTree(prefab.transform, shell.transform);
            NeutralizeNativeTree(shell, removeTutorial: true);

            var shopRoot = Require(shell.transform, shopPath);
            var offerTemplate = Require(shell.transform, offerPath).gameObject;
            var heldCardTemplate = Require(shell.transform, cardTemplatePath).gameObject;
            var heldRelicTemplate = Require(shell.transform, relicTemplatePath).gameObject;
            if (offerTemplate.transform.Find(OfferCurrencyIconPath)?.GetComponent<Image>() == null)
            {
                throw new InvalidOperationException(
                    "Native ShopUI product currency icon is missing: " + OfferCurrencyIconPath);
            }
            var heldCardRoot = heldCardTemplate.transform.parent;
            var heldRelicRoot = heldRelicTemplate.transform.parent;
            var heldRelicNullTemplate = string.IsNullOrWhiteSpace(nullTemplatePath)
                ? null
                : shell.transform.Find(nullTemplatePath)?.gameObject;
            var goldBalanceText = RequireText(shell.transform, "Content/Backpack/Money/text");
            var hintText = RequireText(shell.transform, "Content/Mouse/Text");
            var refreshRoot = Require(shell.transform, "Content/Refresh");
            var exitRoot = Require(shell.transform, "ExitButton");

            offerTemplate.SetActive(false);
            heldCardTemplate.SetActive(false);
            heldRelicTemplate.SetActive(false);
            if (heldRelicNullTemplate != null)
            {
                heldRelicNullTemplate.SetActive(false);
            }

            ConfigureOfferGrid(shopRoot);
            ConfigureResponsiveLayout(shell.transform);
            var truthCurrencySprite = DimensionShopGameApi.TruthCurrencySprite();
            if (truthCurrencySprite == null)
            {
                TerriasLog.Warn("[DimensionShop] native Truth Crystal currency icon is unavailable; price layout retained with its fallback glyph.");
            }
            else
            {
                TerriasLog.Info("[DimensionShop] native Truth Crystal currency icon resolved: " + truthCurrencySprite.name + ".");
            }

            var truthBalanceText = ConfigureCurrencyRow(
                Require(shell.transform, "Content/Backpack/Money"),
                goldBalanceText,
                truthCurrencySprite);
            var hintPresenter = hintText.gameObject.GetComponent<DimensionShopNativeHintPresenter>()
                                ?? hintText.gameObject.AddComponent<DimensionShopNativeHintPresenter>();
            hintPresenter.Configure(hintText, DefaultBackpackHint);
            ReplaceHeader(shell.transform, "Content/List View Custom/Title", "\u6b21\u5143\u5546\u5e97");
            ReplaceHeader(shell.transform, "Content/Backpack/Title", "\u80cc\u5305");

            var refreshButton = BindNativeButton(
                refreshRoot,
                "\u5237\u65b0",
                refresh,
                interactable: true);
            var exitButton = BindNativeButton(
                exitRoot,
                label: null,
                action: leave,
                interactable: true);
            ApplyPortraitOverride(shell.transform);
            shell.SetActive(true);
            ReplaceHeader(shell.transform, "Content/List View Custom/Title", "\u6b21\u5143\u5546\u5e97");
            ReplaceHeader(shell.transform, "Content/Backpack/Title", "\u80cc\u5305");
            hintPresenter.RestoreDefault();

            view = new DimensionShopNativeSkin(
                shell,
                shopRoot,
                offerTemplate,
                heldCardTemplate,
                heldRelicTemplate,
                heldCardRoot,
                heldRelicRoot,
                heldRelicNullTemplate,
                goldBalanceText,
                truthBalanceText,
                truthCurrencySprite,
                hintPresenter,
                refreshButton,
                exitButton,
                buyCard,
                buyRelic,
                sellCard,
                sellRelic,
                unequipRelic);
            TerriasLog.Info("[DimensionShop] native ShopUI visual shell cloned successfully.");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[DimensionShop] native ShopUI visual shell failed; using fallback panel: " + ex.Message);
            if (shell != null)
            {
                shell.SetActive(false);
                UnityEngine.Object.Destroy(shell);
            }

            return false;
        }
    }

    public void Render(DimensionShopViewState state, bool busy)
    {
        if (disposed)
        {
            return;
        }

        overlayGeneration = DimensionShopGameApi.BeginNativeOverlayGeneration();
        ClearGenerated();
        goldBalanceText.text = state.Gold.ToString();
        truthBalanceText.text = state.Truth.ToString();
        truthBalanceText.color = TruthColor;
        for (var slot = 0; slot < state.Cards.Count; slot++)
        {
            var capturedSlot = slot;
            CreateOffer(state.Cards[slot], DataType.Card, () => buyCard(capturedSlot), busy);
        }

        for (var slot = 0; slot < state.Relics.Count; slot++)
        {
            var capturedSlot = slot;
            CreateOffer(state.Relics[slot], DataType.Relic, () => buyRelic(capturedSlot), busy);
        }
        RenderHeldCards(state.HeldCards);
        RenderHeldRelics(state.HeldRelics);
        RebuildInteractiveLayout();

        refreshButton.SetLabel("\u5237\u65b0 " + state.RefreshPrice);
        refreshButton.SetTextColor(state.CanRefresh ? TruthColor : InsufficientColor);
        refreshButton.SetInteractable(state.CanRefresh && !busy);
        hintPresenter.SetDefault(DefaultBackpackHint);
    }

    public void SetHint(string value)
    {
        if (!disposed)
        {
            hintPresenter.ShowTransient(value ?? "");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        overlayGeneration = DimensionShopGameApi.BeginNativeOverlayGeneration();
        ClearGenerated();
        refreshButton.Unbind();
        exitButton.Unbind();
        DimensionShopGameApi.HideFloatingWindow();
        shell.SetActive(false);
        UnityEngine.Object.Destroy(shell);
    }

    private void CreateOffer(DimensionShopItemView item, DataType type, Action purchase, bool busy)
    {
        if (string.IsNullOrWhiteSpace(item.Id)
            || item.State == DimensionShopItemState.Empty
            || item.State == DimensionShopItemState.Unavailable)
        {
            TerriasLog.Debug("[DimensionShop] native offer omitted because no native DataConfig is available: type="
                            + type
                            + ", state="
                            + item.State
                            + ".");
            return;
        }

        var holder = UnityEngine.Object.Instantiate(offerTemplate, shopRoot, false);
        holder.name = GeneratedPrefix + "Offer_" + type;
        generated.Add(holder);
        holder.SetActive(false);
        NeutralizeNativeTree(holder, removeTutorial: false);
        var nativeItem = AuraUiNativeGameItemAdapter.AdoptShopItem(holder);
        nativeItem.ItemType = type.ToString();
        var nativeConfig = AuraGameDataHostApi.Materialize(type, item.Id).Instance as DataConfig;
        if (nativeConfig == null)
        {
            UnityEngine.Object.Destroy(holder);
            return;
        }
        nativeItem.Init(nativeConfig);
        holder.name = GeneratedPrefix + "Offer_" + type;
        LogNativeComponentTopology("offer-" + type, holder, nativeItem, overlayGeneration);

        var hasTerminalOverlay = HasTerminalOverlay(item.State);
        var priceButton = BindNativeButton(
            Require(holder.transform, "val"),
            item.Price.ToString(),
            () =>
            {
                if (item.CanBuy && !busy)
                {
                    purchase();
                }
            },
            !busy && !hasTerminalOverlay);
        SetPrice(holder.transform, item, truthCurrencySprite);
        priceButton.SetTextColor(
            item.State == DimensionShopItemState.InsufficientTruth
                ? InsufficientColor
                : TruthColor);
        if (hasTerminalOverlay)
        {
            ClearPrice(holder.transform);
            CreateStatusOverlay(OfferVisual(holder.transform, type), item.Status);
            priceButton.SetInteractable(false);
        }

        holder.SetActive(true);
    }

    private static AuraUiNativeButtonBinding BindNativeButton(
        Transform target,
        string? label,
        Action action,
        bool interactable)
    {
        var manager = target.GetComponent<ButtonManager>()
                      ?? throw new InvalidOperationException(
                          "Native ShopUI ButtonManager is missing: " + target.name);
        if (!AuraUiNativeButtonBinding.TryBind(
                manager,
                label,
                new UnityAction(action.Invoke),
                interactable,
                out var binding,
                out var failureReason)
            || binding == null)
        {
            throw new InvalidOperationException(failureReason);
        }

        return binding;
    }

    private static void SetPrice(Transform holder, DimensionShopItemView item, Sprite? currencySprite)
    {
        var priceRoot = holder.Find("val");
        if (priceRoot != null)
        {
            priceRoot.gameObject.SetActive(true);
        }

        var value = item.Price.ToString();
        var color = item.State == DimensionShopItemState.InsufficientTruth ? InsufficientColor : TruthColor;
        foreach (var path in new[] { "val/Normal/Title", "val/Hlight/Title", "val/Disabled/Title" })
        {
            var text = holder.Find(path)?.GetComponent<TMP_Text>();
            if (text == null)
            {
                continue;
            }

            text.text = value;
            text.color = color;
        }

        var currencyIcon = holder.Find(OfferCurrencyIconPath)?.GetComponent<Image>();
        if (currencyIcon != null)
        {
            currencyIcon.gameObject.SetActive(currencySprite != null);
            if (currencySprite != null)
            {
                currencyIcon.sprite = currencySprite;
                currencyIcon.preserveAspect = true;
            }
        }
    }

    private static void ClearPrice(Transform holder)
    {
        foreach (var path in new[] { "val/Normal/Title", "val/Hlight/Title", "val/Disabled/Title" })
        {
            var text = holder.Find(path)?.GetComponent<TMP_Text>();
            if (text != null)
            {
                text.text = "";
            }
        }

        var currencyIcon = holder.Find(OfferCurrencyIconPath)?.GetComponent<Image>();
        if (currencyIcon != null)
        {
            currencyIcon.sprite = null;
            currencyIcon.gameObject.SetActive(false);
        }

        var priceRoot = holder.Find("val");
        if (priceRoot != null)
        {
            priceRoot.gameObject.SetActive(false);
        }
    }

    private static Transform OfferVisual(Transform holder, DataType type)
    {
        return Require(holder, type == DataType.Card ? "CardItem" : "Item");
    }

    private void RenderHeldCards(IReadOnlyList<DimensionShopHeldItemView> cards)
    {
        foreach (var item in cards)
        {
            var holder = UnityEngine.Object.Instantiate(heldCardTemplate, heldCardRoot, false);
            holder.name = GeneratedPrefix + "HeldCard";
            generated.Add(holder);
            holder.SetActive(false);
            NeutralizeNativeTree(holder, removeTutorial: false);
            var nativeItem = AuraUiNativeGameItemAdapter.AdoptSellItem(holder, null, null);
            ConfigureHeldCardInteraction(nativeItem, item, sellCard);
            nativeItem.ItemType = DataType.Card.ToString();
            nativeItem.Init(item.Equipped, NativeConfig(item, DataType.Card));
            nativeItem.canClick = true;
            holder.name = GeneratedPrefix + "HeldCard";
            LogNativeComponentTopology("held-card", holder, nativeItem, overlayGeneration);
            holder.SetActive(true);
        }
    }

    private void RenderHeldRelics(IReadOnlyList<DimensionShopHeldItemView> relics)
    {
        var displayed = 0;
        foreach (var item in relics)
        {
            if (displayed >= 6)
            {
                break;
            }

            var holder = UnityEngine.Object.Instantiate(heldRelicTemplate, heldRelicRoot, false);
            holder.name = GeneratedPrefix + "HeldRelic";
            generated.Add(holder);
            holder.SetActive(false);
            NeutralizeNativeTree(holder, removeTutorial: false);
            var nativeItem = AuraUiNativeGameItemAdapter.AdoptRelicItem(holder, null, null);
            ConfigureHeldRelicInteraction(nativeItem, item, sellRelic, unequipRelic);
            nativeItem.Init(NativeConfig(item, DataType.Relic));
            AuraUiNativeGameItemAdapter.ApplyButtonIcon(
                nativeItem.GetComponent<ButtonManager>(),
                nativeItem.itemIcon);
            nativeItem.ifEquipped = item.Equipped;
            nativeItem.IsSelf = true;
            nativeItem.canClick = true;
            holder.name = GeneratedPrefix + "HeldRelic";
            LogNativeComponentTopology("held-relic", holder, nativeItem, overlayGeneration);
            holder.SetActive(true);
            displayed++;
        }

        if (heldRelicNullTemplate == null)
        {
            return;
        }

        for (var i = displayed; i < 6; i++)
        {
            var empty = CreateVisualHolder(heldRelicNullTemplate, GeneratedPrefix + "HeldRelicEmpty", heldRelicRoot);
            generated.Add(empty);
            empty.SetActive(false);
            CloneAllChildren(heldRelicNullTemplate.transform, empty.transform);
            MakeReadOnly(empty);
            empty.SetActive(true);
        }
    }

    private void ClearGenerated()
    {
        foreach (var item in generated)
        {
            if (item == null)
            {
                continue;
            }

            item.SetActive(false);
            UnityEngine.Object.Destroy(item);
        }

        generated.Clear();
    }

    private void RebuildInteractiveLayout()
    {
        Canvas.ForceUpdateCanvases();
        foreach (var root in new[] { shopRoot, heldCardRoot, heldRelicRoot })
        {
            if (root is RectTransform rect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
        }

        Canvas.ForceUpdateCanvases();
        foreach (var item in generated)
        {
            if (item == null)
            {
                continue;
            }

            var hasInvalidPointerSurface = false;
            foreach (var surface in item.GetComponentsInChildren<AuraUiPointerSurface>(true))
            {
                if (!surface.HasValidHitArea())
                {
                    hasInvalidPointerSurface = true;
                    break;
                }
            }

            var hasInvalidNativeButton = false;
            foreach (var binding in item.GetComponentsInChildren<AuraUiNativeButtonBinding>(true))
            {
                if (!binding.HasValidHitArea())
                {
                    hasInvalidNativeButton = true;
                    break;
                }
            }

            if ((hasInvalidPointerSurface || hasInvalidNativeButton)
                && interactionLayoutWarnings.Add(item.name))
            {
                TerriasLog.Warn("[DimensionShop] interactive native item has an invalid layout rect: " + item.name + ".");
            }
        }
    }

    private static GameObject CreateShell(Transform parent)
    {
        var shell = new GameObject("NativeShopShell", typeof(RectTransform), typeof(CanvasGroup));
        shell.SetActive(false);
        var rect = shell.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return shell;
    }

    private static void CloneRootVisualTree(Transform source, Transform destination)
    {
        for (var i = 0; i < source.childCount; i++)
        {
            var child = source.GetChild(i);
            var clone = UnityEngine.Object.Instantiate(child.gameObject, destination, false);
            clone.name = child.name;
            clone.transform.SetSiblingIndex(i);
        }
    }

    private static GameObject CreateVisualHolder(GameObject template, string name, Transform parent)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        var rect = holder.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        var sourceRect = template.transform as RectTransform;
        if (sourceRect != null)
        {
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.pivot = sourceRect.pivot;
            rect.sizeDelta = sourceRect.sizeDelta;
            rect.anchoredPosition = sourceRect.anchoredPosition;
            rect.localScale = sourceRect.localScale;
            rect.localRotation = sourceRect.localRotation;
        }

        var sourceImage = template.GetComponent<Image>();
        if (sourceImage != null)
        {
            var image = holder.AddComponent<Image>();
            image.sprite = sourceImage.sprite;
            image.type = sourceImage.type;
            image.color = sourceImage.color;
            image.preserveAspect = sourceImage.preserveAspect;
            image.raycastTarget = false;
        }

        var sourceLayout = template.GetComponent<LayoutElement>();
        if (sourceLayout != null)
        {
            var layout = holder.AddComponent<LayoutElement>();
            layout.ignoreLayout = sourceLayout.ignoreLayout;
            layout.minWidth = sourceLayout.minWidth;
            layout.minHeight = sourceLayout.minHeight;
            layout.preferredWidth = sourceLayout.preferredWidth;
            layout.preferredHeight = sourceLayout.preferredHeight;
            layout.flexibleWidth = sourceLayout.flexibleWidth;
            layout.flexibleHeight = sourceLayout.flexibleHeight;
            layout.layoutPriority = sourceLayout.layoutPriority;
        }

        return holder;
    }

    private static void CloneAllChildren(Transform source, Transform destination)
    {
        for (var i = 0; i < source.childCount; i++)
        {
            var child = source.GetChild(i);
            var clone = UnityEngine.Object.Instantiate(child.gameObject, destination, false);
            clone.name = child.name;
        }
    }

    private static void NeutralizeNativeTree(GameObject root, bool removeTutorial)
    {
        if (removeTutorial)
        {
            foreach (var tutorial in root.GetComponentsInChildren<TutorialSpotlightUI>(true))
            {
                var tutorialRoot = tutorial.gameObject;
                tutorialRoot.SetActive(false);
                UnityEngine.Object.DestroyImmediate(tutorialRoot);
            }
        }

        AuraUiNativeButtonBinding.NeutralizeTree(root, disable: false);

        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            button.onClick = new Button.ButtonClickedEvent();
        }

        // CardItem installs its native hover animation in EventTrigger during
        // Awake. Keep those entries intact; purchase semantics are already
        // detached through ButtonManager/Unity Button event replacement.
    }

    private static void LogNativeComponentTopology(
        string kind,
        GameObject root,
        Witch.UI.Window.Item item,
        long overlayGeneration)
    {
        var raycastGraphics = 0;
        foreach (var graphic in item.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.raycastTarget)
            {
                raycastGraphics++;
            }
        }

        var enabledTooltipCount = 0;
        foreach (var tooltip in root.GetComponentsInChildren<KeywordDisplay>(true))
        {
            if (!tooltip.enabled)
            {
                continue;
            }

            enabledTooltipCount++;
            tooltip.OnShow += () => DimensionShopGameApi.VerifyTooltipVisible(kind, tooltip, overlayGeneration);
        }

        TerriasLog.InfoOnceAlways(
            "dimension-shop-native-component-" + kind,
            "[DimensionShop] native item component active: kind="
            + kind
            + ", componentType="
            + item.GetType().FullName
            + ", nativeBase="
            + item.GetType().BaseType?.FullName
            + ", eventTarget="
            + DescribeAnchorPath(root.transform, item.transform)
            + ", enabledTooltips="
            + enabledTooltipCount
            + ", buttonManagers="
            + root.GetComponentsInChildren<ButtonManager>(true).Length
            + ", raycastGraphics="
            + raycastGraphics
            + ".");
    }

    private static string DescribeAnchorPath(Transform root, Transform target)
    {
        try
        {
            var path = RelativePath(root, target);
            return string.IsNullOrWhiteSpace(path) ? "<root>" : path;
        }
        catch
        {
            return "<outside-root:" + target.name + ">";
        }
    }

    private static void MakeReadOnly(GameObject root)
    {
        NeutralizeNativeTree(root, removeTutorial: false);
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        var canvasGroup = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private static void ConfigureHeldCardInteraction(
        AuraUiSafeSellItem nativeItem,
        DimensionShopHeldItemView item,
        Action<string> sell)
    {
        var instanceId = item.InstanceId;
        var sellLabel = item.CanSell
            ? "sell".Localize("Button") + item.SellPrice
            : "<color=red>" + "sell".Localize("Button") + item.SellPrice + "</color>";
        nativeItem.ConfigureActions(
            DimensionShopGameApi.HideFloatingWindow,
            () =>
            {
                DimensionShopGameApi.HideTooltip();
                if (!DimensionShopGameApi.ShowCardSellMenu(
                        nativeItem.transform,
                        sellLabel,
                        () => sell(instanceId)))
                {
                    TerriasLog.WarnOnce(
                        "dimension-shop-held-card-menu",
                        "[DimensionShop] held card right-click was received but the native floating menu request was rejected.");
                }
            });
    }

    private static void ConfigureHeldRelicInteraction(
        AuraUiSafeRelicItem nativeItem,
        DimensionShopHeldItemView item,
        Action<string> sell,
        Action<string> unequip)
    {
        var instanceId = item.InstanceId;
        var sellLabel = item.CanSell
            ? "sell".Localize("Button") + item.SellPrice
            : "<color=red>" + "sell".Localize("Button") + item.SellPrice + "</color>";
        nativeItem.ConfigureActions(
            DimensionShopGameApi.HideFloatingWindow,
            () =>
            {
                DimensionShopGameApi.HideTooltip();
                if (!DimensionShopGameApi.ShowRelicMenu(
                        nativeItem.transform,
                        sellLabel,
                        () => sell(instanceId),
                        item.Equipped,
                        "Take off".Localize("Button"),
                        () => unequip(instanceId)))
                {
                    TerriasLog.WarnOnce(
                        "dimension-shop-held-relic-menu",
                        "[DimensionShop] held relic right-click was received but the native floating menu request was rejected.");
                }
            });
    }

    private static DataConfig NativeConfig(DimensionShopHeldItemView item, DataType type)
    {
        if (item.NativeConfig != null)
        {
            return item.NativeConfig;
        }

        TerriasLog.WarnOnce(
            "dimension-shop-native-config-fallback-" + type + "-" + item.Id,
            "[DimensionShop] exact held-item DataConfig was unavailable; reconstructing native presenter config: type="
            + type
            + ", id="
            + item.Id
            + ".");
        return AuraGameDataHostApi.Materialize(type, item.Id).Instance as DataConfig
            ?? throw new InvalidOperationException("Registered presenter definition is unavailable: " + type + ":" + item.Id);
    }

    private static TMP_Text ConfigureCurrencyRow(
        Transform moneyRoot,
        TMP_Text goldText,
        Sprite? truthSprite)
    {
        foreach (var layout in moneyRoot.GetComponents<LayoutGroup>())
        {
            UnityEngine.Object.DestroyImmediate(layout);
        }

        foreach (var behaviour in moneyRoot.GetComponents<MonoBehaviour>())
        {
            if (string.Equals(behaviour.GetType().Name, "LayoutGroupFix", StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(behaviour);
            }
        }

        const float goldWidth = 150f;
        const float gap = 24f;
        const float truthWidth = 196f;
        const float totalWidth = goldWidth + gap + truthWidth;
        var moneyRect = moneyRoot as RectTransform
                        ?? throw new InvalidOperationException("Native ShopUI money root is not a RectTransform.");
        var originalLeft = moneyRect.anchoredPosition.x - moneyRect.sizeDelta.x * moneyRect.pivot.x;
        moneyRect.sizeDelta = new Vector2(totalWidth, Mathf.Max(40f, moneyRect.sizeDelta.y));
        moneyRect.anchoredPosition = new Vector2(
            originalLeft + totalWidth * moneyRect.pivot.x,
            moneyRect.anchoredPosition.y);

        var goldIcon = moneyRoot.Find(BackpackGoldIconPath)?.GetComponent<Image>()
                       ?? throw new InvalidOperationException("Native ShopUI gold currency icon is missing.");
        var goldGroup = CreateCurrencyGroup(moneyRoot, GeneratedPrefix + "GoldCurrency", 0f, goldWidth);
        ConfigureCurrencyVisual(goldGroup, goldIcon, goldText, goldIcon.sprite);

        var truthGroup = CreateCurrencyGroup(
            moneyRoot,
            GeneratedPrefix + "TruthCurrency",
            goldWidth + gap,
            truthWidth);
        var truthIconObject = UnityEngine.Object.Instantiate(goldIcon.gameObject, truthGroup, false);
        truthIconObject.name = "Icon";
        NeutralizeNativeTree(truthIconObject, removeTutorial: false);
        var truthIcon = truthIconObject.GetComponent<Image>();
        var truthTextObject = UnityEngine.Object.Instantiate(goldText.gameObject, truthGroup, false);
        truthTextObject.name = "text";
        NeutralizeNativeTree(truthTextObject, removeTutorial: false);
        var truthText = truthTextObject.GetComponent<TMP_Text>();
        ConfigureCurrencyVisual(truthGroup, truthIcon, truthText, truthSprite);
        return truthText;
    }

    private static RectTransform CreateCurrencyGroup(Transform parent, string name, float x, float width)
    {
        var root = new GameObject(name, typeof(RectTransform));
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, 40f);
        return rect;
    }

    private static void ConfigureCurrencyVisual(
        RectTransform parent,
        Image icon,
        TMP_Text text,
        Sprite? sprite)
    {
        const float iconSize = 26f;
        var iconRect = icon.rectTransform;
        iconRect.SetParent(parent, false);
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);
        icon.gameObject.SetActive(sprite != null);
        if (sprite != null)
        {
            icon.sprite = sprite;
            icon.preserveAspect = true;
        }

        icon.raycastTarget = false;

        var textRect = text.rectTransform;
        textRect.SetParent(parent, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(iconSize + 8f, 0f);
        textRect.offsetMax = Vector2.zero;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
    }

    private static void ConfigureOfferGrid(Transform root)
    {
        foreach (var layout in root.GetComponents<LayoutGroup>())
        {
            if (!(layout is GridLayoutGroup))
            {
                UnityEngine.Object.DestroyImmediate(layout);
            }
        }

        var grid = root.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = root.gameObject.AddComponent<GridLayoutGroup>();
            var sourceRect = root.GetChild(0) as RectTransform;
            grid.cellSize = sourceRect == null || sourceRect.sizeDelta == Vector2.zero
                ? new Vector2(210f, 310f)
                : sourceRect.sizeDelta;
            grid.spacing = new Vector2(14f, 8f);
        }

        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
    }

    private static void ConfigureResponsiveLayout(Transform root)
    {
        var rect = root.parent as RectTransform;
        var width = rect != null && rect.rect.width > 0f ? rect.rect.width : Screen.width;
        var height = rect != null && rect.rect.height > 0f ? rect.rect.height : Screen.height;
        var aspect = height <= 0f ? 16f / 9f : width / height;
        var content = root.Find("Content") as RectTransform;
        if (content == null)
        {
            return;
        }

        if (aspect < 1.55f)
        {
            content.anchorMin = new Vector2(0.12f, content.anchorMin.y);
            content.anchorMax = new Vector2(0.985f, content.anchorMax.y);
            content.offsetMin = new Vector2(0f, content.offsetMin.y);
            content.offsetMax = new Vector2(0f, content.offsetMax.y);
            SetHorizontalAnchors(root.Find("Content/List View Custom") as RectTransform, 0.02f, 0.45f);
            SetHorizontalAnchors(root.Find("Content/Backpack") as RectTransform, 0.45f, 1f);
        }
    }

    private static void SetHorizontalAnchors(RectTransform? rect, float min, float max)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = new Vector2(min, rect.anchorMin.y);
        rect.anchorMax = new Vector2(max, rect.anchorMax.y);
        rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
        rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
    }

    private static void ApplyPortraitOverride(Transform root)
    {
        var config = DimensionShopConfigStore.Current;
        if (string.IsNullOrWhiteSpace(config.ShopkeeperPortraitResourcePath))
        {
            return;
        }

        var sprite = TerriasResourceCache.Load<Sprite>(
            config.ShopkeeperPortraitResourcePath,
            true,
            "dimension.shop.portrait");
        if (sprite == null)
        {
            TerriasLog.Warn("[DimensionShop] configured shopkeeper portrait is unavailable; native portrait retained.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ShopkeeperPortraitNodePath))
        {
            TerriasLog.Warn("[DimensionShop] shopkeeper portrait resource is configured without an explicit native node path; native portrait retained.");
            return;
        }

        var target = root.Find(config.ShopkeeperPortraitNodePath)?.GetComponent<Image>();
        if (target == null)
        {
            TerriasLog.Warn("[DimensionShop] shopkeeper portrait node is unavailable; native portrait retained.");
            return;
        }

        target.sprite = sprite;
        target.preserveAspect = true;
    }

    private static void CreateStatusOverlay(Transform parent, string status)
    {
        var overlay = new GameObject("StatusOverlay", typeof(RectTransform), typeof(Image));
        var rect = overlay.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = overlay.GetComponent<Image>();
        image.color = new Color(0.02f, 0.015f, 0.04f, 0.74f);
        image.raycastTarget = false;

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = string.IsNullOrWhiteSpace(status) ? "\u5df2\u552e\u7f44" : status;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 28f;
        text.color = TruthColor;
        text.raycastTarget = false;
    }

    private static bool HasTerminalOverlay(DimensionShopItemState state)
    {
        return state == DimensionShopItemState.Purchased
               || state == DimensionShopItemState.SoldOut
               || state == DimensionShopItemState.Owned
               || state == DimensionShopItemState.Empty
               || state == DimensionShopItemState.Unavailable;
    }

    private static void ReplaceHeader(Transform root, string path, string value)
    {
        var target = root.Find(path);
        var text = target?.GetComponent<TMP_Text>() ?? target?.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.text = value;
        }
    }

    private static Transform Require(Transform root, string path)
    {
        return root.Find(path) ?? throw new InvalidOperationException("Native ShopUI node is missing: " + path);
    }

    private static TMP_Text RequireText(Transform root, string path)
    {
        return Require(root, path).GetComponent<TMP_Text>()
               ?? throw new InvalidOperationException("Native ShopUI text is missing: " + path);
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (target == root)
        {
            return "";
        }

        var parts = new Stack<string>();
        var current = target;
        while (current != null && current != root)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        if (current != root)
        {
            throw new InvalidOperationException("Transform is outside native ShopUI root: " + target.name);
        }

        return string.Join("/", parts);
    }
}

internal sealed class DimensionShopNativeHintPresenter : MonoBehaviour
{
    private TMP_Text? target;
    private string defaultValue = "";
    private float restoreAt = -1f;

    public void Configure(TMP_Text text, string value)
    {
        target = text;
        defaultValue = value ?? "";
        RestoreDefault();
    }

    public void SetDefault(string value)
    {
        defaultValue = value ?? "";
        if (restoreAt < 0f && target != null)
        {
            target.text = defaultValue;
        }
    }

    public void ShowTransient(string value)
    {
        if (target == null || string.IsNullOrWhiteSpace(value))
        {
            RestoreDefault();
            return;
        }

        target.text = value;
        restoreAt = Time.unscaledTime + 2.4f;
    }

    public void RestoreDefault()
    {
        restoreAt = -1f;
        if (target != null)
        {
            target.text = defaultValue;
        }
    }

    private void Update()
    {
        if (restoreAt >= 0f && Time.unscaledTime >= restoreAt)
        {
            RestoreDefault();
        }
    }
}
