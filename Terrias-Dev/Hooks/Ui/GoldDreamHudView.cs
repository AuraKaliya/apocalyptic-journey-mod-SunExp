using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks.Ui;

public sealed class GoldDreamHudView : MonoBehaviour
{
    private const string FalseGoldRowName = "Terrias_FalseGold";
    private const string DebtRowName = "Terrias_Debt";
    private const string FalseGoldIconPath = "Mods/Terrias/ModResource/Images/Card/Gold/豪掷千金.png";
    private const string DebtIconPath = "Mods/Terrias/ModResource/Images/Card/Gold/空头支票.png";
    private static readonly Color CrimsonCoinTint = new(0.78f, 0.08f, 0.2f, 1f);
    private static readonly Color DebtTint = new(0.82f, 0.58f, 0.24f, 1f);
    private readonly List<RowSet> rowSets = new();
    private readonly HashSet<int> expandedWealthRoots = new();
    private TopBarUI? topBar;

    public void Bind(TopBarUI owner)
    {
        topBar = owner;
        EnsureRows();
    }

    public void ApplySnapshot(GoldDreamSnapshot snapshot)
    {
        EnsureRows();
        foreach (var rows in rowSets)
        {
            rows.FalseGold.SetActive(snapshot.Active);
            rows.Debt.SetActive(snapshot.Active);
            if (!snapshot.Active)
            {
                continue;
            }

            rows.FalseGoldValue.text = snapshot.FalseGold.ToString(CultureInfo.InvariantCulture);
            rows.DebtValue.text = "1:" + Compact(snapshot.DebtDueOne)
                + " 2:" + Compact(snapshot.DebtDueTwo)
                + " 3:" + Compact(snapshot.DebtDueThree);
        }
    }

    private void EnsureRows()
    {
        if (topBar == null || topBar.transform == null)
        {
            return;
        }

        EnsureRowsFor(topBar.transform.Find("Content/PlayerStatus/Wealth"));
        EnsureRowsFor(topBar.transform.Find("Content/PlayerStatusList/Wealth"));
    }

    private void EnsureRowsFor(Transform? wealth)
    {
        if (wealth == null || rowSets.Any(rows => rows.Wealth == wealth))
        {
            return;
        }

        var money = wealth.Find("Money") as RectTransform;
        if (money == null)
        {
            return;
        }

        var truth = wealth.Find("True") as RectTransform;
        var falseGold = CreateOrFindRow(wealth, money, truth, FalseGoldRowName, 1);
        var debt = CreateOrFindRow(wealth, money, truth, DebtRowName, 2);
        if (falseGold == null || debt == null)
        {
            return;
        }

        var falseValue = ConfigureValue(falseGold, 1f, 13f);
        var debtValue = ConfigureValue(debt, 0.94f, 12f);
        if (falseValue == null || debtValue == null)
        {
            return;
        }

        ConfigureIcon(falseGold, CrimsonCoinTint, LoadIcon("gold_dream.hud.false_gold", FalseGoldIconPath));
        ConfigureIcon(debt, DebtTint, LoadIcon("gold_dream.hud.debt", DebtIconPath));
        DisableRaycasts(falseGold);
        DisableRaycasts(debt);
        rowSets.Add(new RowSet(wealth, falseGold.gameObject, debt.gameObject, falseValue, debtValue));
    }

    private RectTransform? CreateOrFindRow(
        Transform wealth,
        RectTransform money,
        RectTransform? truth,
        string name,
        int ordinal)
    {
        if (wealth.Find(name) is RectTransform existing)
        {
            return existing;
        }

        var go = UnityEngine.Object.Instantiate(money.gameObject, wealth);
        go.name = name;
        var rect = go.transform as RectTransform;
        if (rect == null)
        {
            return null;
        }

        var layout = wealth.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            rect.SetSiblingIndex(Math.Min(wealth.childCount - 1, (truth ?? money).GetSiblingIndex() + ordinal));
            return rect;
        }

        var anchor = truth ?? money;
        var spacing = truth == null
            ? new Vector2(0f, -Math.Max(28f, money.rect.height))
            : truth.anchoredPosition - money.anchoredPosition;
        if (spacing.sqrMagnitude < 1f)
        {
            spacing = new Vector2(0f, -Math.Max(28f, money.rect.height));
        }

        rect.anchoredPosition = anchor.anchoredPosition + spacing * ordinal;
        if (wealth is RectTransform wealthRect && expandedWealthRoots.Add(wealth.GetInstanceID()))
        {
            wealthRect.sizeDelta += new Vector2(0f, Math.Abs(spacing.y) * 2f);
        }

        return rect;
    }

    private static TMP_Text? ConfigureValue(RectTransform row, float scale, float minimumSize)
    {
        var text = row.Find("val")?.GetComponent<TMP_Text>()
                   ?? row.GetComponentInChildren<TMP_Text>(true);
        if (text == null)
        {
            return null;
        }

        text.enableAutoSizing = true;
        text.fontSizeMax = Math.Max(minimumSize, text.fontSize * scale);
        text.fontSizeMin = minimumSize;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void ConfigureIcon(RectTransform row, Color tint, Sprite? replacement)
    {
        var icon = row.Find("Terrias_HudIcon")?.GetComponent<Image>();
        if (icon == null)
        {
            var iconObject = new GameObject("Terrias_HudIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(row, false);
            var rect = (RectTransform)iconObject.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(22f, 22f);
            rect.anchoredPosition = new Vector2(12f, 0f);
            icon = iconObject.GetComponent<Image>();
        }

        if (replacement != null)
        {
            icon.sprite = replacement;
        }

        icon.gameObject.SetActive(true);
        icon.enabled = true;
        icon.color = replacement == null ? tint : Color.white;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
    }

    private static Sprite? LoadIcon(string id, string fallback)
    {
        var path = VisualRegistry.TexturePath(id, fallback) ?? fallback;
        return TerriasResourceCache.Load<Sprite>(path, true, "gold-dream.hud");
    }

    private static void DisableRaycasts(RectTransform row)
    {
        foreach (var graphic in row.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        foreach (var selectable in row.GetComponentsInChildren<Selectable>(true))
        {
            selectable.interactable = false;
        }

        var group = row.GetComponent<CanvasGroup>() ?? row.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private static string Compact(int value)
    {
        var amount = Math.Max(0, value);
        if (amount >= 1_000_000_000)
        {
            return (amount / 1_000_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        }

        if (amount >= 1_000_000)
        {
            return (amount / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }

        return amount >= 1_000
            ? (amount / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K"
            : amount.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class RowSet
    {
        public RowSet(
            Transform wealth,
            GameObject falseGold,
            GameObject debt,
            TMP_Text falseGoldValue,
            TMP_Text debtValue)
        {
            Wealth = wealth;
            FalseGold = falseGold;
            Debt = debt;
            FalseGoldValue = falseGoldValue;
            DebtValue = debtValue;
        }

        public Transform Wealth { get; }

        public GameObject FalseGold { get; }

        public GameObject Debt { get; }

        public TMP_Text FalseGoldValue { get; }

        public TMP_Text DebtValue { get; }
    }
}
