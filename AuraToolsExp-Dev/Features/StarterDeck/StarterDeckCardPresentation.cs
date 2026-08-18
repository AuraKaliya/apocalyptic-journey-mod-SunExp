using System;
using System.Collections.Generic;
using System.Linq;
using AuraMode.Shared;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckCardPresentation
{
    private static readonly Dictionary<string, Sprite?> cardIconCache = new(StringComparer.OrdinalIgnoreCase);

    internal static void ClearCache() => cardIconCache.Clear();

    public static string CardSortKey(string cardId)
    {
        try
        {
            var data = CardData(cardId);
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
        if (StarterDeckCardCatalog.TryGetCatalogCard(cardId, out var card) && card != null)
        {
            return string.IsNullOrWhiteSpace(card.DisplayName) ? cardId : card.DisplayName;
        }

        try
        {
            var data = CardData(cardId);
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

    public static string CardDisplayNameWithSpecialMarker(string cardId)
    {
        var name = CardDisplayName(cardId);
        return IsSpecialCardId(cardId) ? "\u3010*\u3011 " + name : name;
    }

    public static bool IsSpecialCardId(string cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId)
               && (cardId.StartsWith("*", StringComparison.Ordinal)
                   || cardId.IndexOf("_*", StringComparison.Ordinal) >= 0);
    }

    public static string CardShortInfo(string cardId)
    {
        if (StarterDeckCardCatalog.TryGetCatalogCard(cardId, out var card) && card != null)
        {
            var rarity = string.IsNullOrWhiteSpace(card.Rarity) ? "?" : "R" + card.Rarity;
            var cost = string.IsNullOrWhiteSpace(card.Cost) ? "?" : card.Cost;
            return rarity + " / Cost" + cost + " / " + cardId;
        }

        try
        {
            var data = CardData(cardId);
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
        if (StarterDeckCardCatalog.TryGetCatalogCard(cardId, out var card) && card != null)
        {
            return string.IsNullOrWhiteSpace(card.Rarity) ? "?" : "R" + card.Rarity;
        }

        try
        {
            var data = CardData(cardId);
            return data.TryGetValue("Rarity", out var rarity) && !string.IsNullOrWhiteSpace(rarity) ? "R" + rarity : "?";
        }
        catch
        {
            return "?";
        }
    }

    public static string CardCost(string cardId)
    {
        if (StarterDeckCardCatalog.TryGetCatalogCard(cardId, out var card) && card != null)
        {
            return string.IsNullOrWhiteSpace(card.Cost) ? "?" : card.Cost;
        }

        try
        {
            var data = CardData(cardId);
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
            var iconPath = "";
            if (StarterDeckCardCatalog.TryGetCatalogCard(cardId, out var card) && card != null)
            {
                iconPath = card.IconPath;
            }
            else
            {
                var data = CardData(cardId);
                if (data.TryGetValue("Icon", out var rawIconPath))
                {
                    iconPath = rawIconPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                sprite = AuraToolsResourceCache.Load<Sprite>(iconPath, true);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[CustomStart] failed to load card icon for " + cardId + ": " + ex.Message);
        }

        cardIconCache[cardId] = sprite;
        return sprite;
    }

    private static Dictionary<string, string> CardData(string cardId)
    {
        return AuraGameDataHostApi.CopyRow(DataType.Card, cardId)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
