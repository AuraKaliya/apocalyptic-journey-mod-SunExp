using System;
using Michsky.MUIP;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI;
using Witch.UI.Window;

namespace AuraUi.Shared;

/// <summary>
/// Adopts the game's serialized item components while keeping consumer-owned
/// actions outside the native purchase and settlement implementations.
/// Consumers still initialize the returned component through the native Init
/// method so the host owns card/relic visuals, tooltip data, and localization.
/// </summary>
public static class AuraUiNativeGameItemAdapter
{
    public static ShopItem AdoptShopItem(GameObject root)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var item = root.GetComponent<ShopItem>()
                   ?? root.GetComponentInChildren<ShopItem>(true)
                   ?? throw new InvalidOperationException("native ShopItem is missing under " + root.name);
        Prepare(item);
        return item;
    }

    public static AuraUiSafeSellItem AdoptSellItem(
        GameObject root,
        Action? onLeftClick,
        Action? onRightClick)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var source = root.GetComponent<SellItem>()
                     ?? root.GetComponentInChildren<SellItem>(true)
                     ?? throw new InvalidOperationException("native SellItem is missing under " + root.name);
        var eventTarget = source.gameObject;
        var tooltip = EnsureTooltip(source);
        source.enabled = false;

        var item = eventTarget.AddComponent<AuraUiSafeSellItem>();
        item.keywordDisplay = tooltip;
        item.ConfigureActions(onLeftClick, onRightClick);
        Prepare(item);
        return item;
    }

    public static AuraUiSafeRelicItem AdoptRelicItem(
        GameObject root,
        Action? onLeftClick,
        Action? onRightClick)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var source = root.GetComponent<RelicItemConfig>()
                     ?? root.GetComponentInChildren<RelicItemConfig>(true)
                     ?? throw new InvalidOperationException("native RelicItemConfig is missing under " + root.name);
        var eventTarget = source.gameObject;
        var tooltip = EnsureTooltip(source);
        source.enabled = false;

        var item = eventTarget.AddComponent<AuraUiSafeRelicItem>();
        item.keywordDisplay = tooltip;
        item.ConfigureActions(onLeftClick, onRightClick);
        Prepare(item);
        return item;
    }

    public static void ApplyButtonIcon(ButtonManager? manager, Sprite? sprite)
    {
        if (manager == null)
        {
            return;
        }

        manager.enableIcon = sprite != null;
        manager.SetIcon(sprite);
        manager.UpdateUI();
    }

    private static void Prepare(Item item)
    {
        item.keywordDisplay = EnsureTooltip(item);
        item.keywordDisplay.enabled = true;
    }

    private static KeywordDisplay EnsureTooltip(Item item)
    {
        return item.keywordDisplay
               ?? item.GetComponent<KeywordDisplay>()
               ?? item.gameObject.AddComponent<KeywordDisplay>();
    }
}

/// <summary>
/// A native SellItem presenter whose initialization and tooltip lifecycle are
/// inherited from the game. Only click meaning is supplied by the consumer.
/// </summary>
public sealed class AuraUiSafeSellItem : SellItem
{
    private Action? leftAction;
    private Action? rightAction;

    public void ConfigureActions(Action? onLeftClick, Action? onRightClick)
    {
        leftAction = onLeftClick;
        rightAction = onRightClick;
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (!canClick)
        {
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            ShowFloatingWindow();
        }
        else if (eventData.button == PointerEventData.InputButton.Left)
        {
            leftAction?.Invoke();
        }
    }

    public override void ShowFloatingWindow()
    {
        rightAction?.Invoke();
    }

    public override void OnDestroy()
    {
        leftAction = null;
        rightAction = null;
        base.OnDestroy();
    }
}

/// <summary>
/// A native RelicItemConfig presenter with consumer-owned click actions. The
/// native component still initializes ButtonManager icons and KeywordDisplay.
/// </summary>
public sealed class AuraUiSafeRelicItem : RelicItemConfig
{
    private Action? leftAction;
    private Action? rightAction;

    public void ConfigureActions(Action? onLeftClick, Action? onRightClick)
    {
        leftAction = onLeftClick;
        rightAction = onRightClick;
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (!canClick || !IsSelf)
        {
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            ShowFloatingWindow();
        }
        else if (eventData.button == PointerEventData.InputButton.Left)
        {
            leftAction?.Invoke();
        }
    }

    public override void ShowFloatingWindow()
    {
        rightAction?.Invoke();
    }

    public override void OnTransformParentChanged()
    {
        // The consumer owns equipment state. Native visual initialization may
        // reorder the icon, but a presentation-only parent change must never
        // toggle RoleTable equipment state.
    }

    public override void OnDestroy()
    {
        leftAction = null;
        rightAction = null;
        base.OnDestroy();
    }
}
