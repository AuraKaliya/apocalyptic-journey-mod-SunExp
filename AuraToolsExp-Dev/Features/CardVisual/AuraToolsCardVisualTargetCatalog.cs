using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using AuraToolsExp.Dll.Features.Settings;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.CardVisual;

internal static class AuraToolsCardVisualTargetCatalog
{
    internal static IReadOnlyList<ToolboxSearchOption> Options(string mode)
    {
        if (!AuraGameDataHostApi.IsNativeCatalogReady)
        {
            return Array.Empty<ToolboxSearchOption>();
        }

        var cards = AuraGameDataHostApi.Table(DataType.Card).ToArray();
        return (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "pack" => PackOptions(cards),
            "rarity" => RarityOptions(cards),
            _ => CardOptions(cards)
        };
    }

    internal static string SelectionSummary(string mode, string value)
    {
        var cards = AuraToolsCardVisualRuntime.SelectCards(mode, value);
        if (cards.Count == 0)
        {
            return "尚未选择有效范围";
        }

        var examples = cards
            .Take(3)
            .Select(AuraToolsPlayerDisplay.CardName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        return "将应用到 "
               + cards.Count
               + " 张卡牌"
               + (examples.Length == 0
                   ? ""
                   : "：" + string.Join("、", examples)
                     + (cards.Count > examples.Length ? " 等" : ""));
    }

    private static IReadOnlyList<ToolboxSearchOption> CardOptions(
        IReadOnlyList<AuraGameDataSnapshot> cards)
    {
        var packs = AuraGameDataHostApi.Table(DataType.CardPack).ToArray();
        var entries = cards.Select(card => new
            {
                Card = card,
                Name = DisplayName(card, "未命名卡牌"),
                PackName = ResolvePackName(card, packs),
                QualifiedId = Qualify(card.OwnerModId, card.Id)
            })
            .ToArray();
        var duplicateNames = entries
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return entries
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.PackName, StringComparer.Ordinal)
            .ThenBy(entry => entry.Card.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                var context = string.IsNullOrWhiteSpace(entry.PackName)
                    ? ""
                    : " · " + entry.PackName;
                if (duplicateNames.Contains(entry.Name))
                {
                    context += "（"
                               + AuraToolsPlayerDisplay.OwnerName(
                                   entry.Card.OwnerModId)
                               + "）";
                }
                return new ToolboxSearchOption(
                    entry.QualifiedId,
                    entry.Name + context,
                    entry.Name
                    + " "
                    + entry.PackName
                    + " "
                    + entry.Card.Id
                    + " "
                    + entry.Card.OwnerModId);
            })
            .ToArray();
    }

    private static IReadOnlyList<ToolboxSearchOption> PackOptions(
        IReadOnlyList<AuraGameDataSnapshot> cards)
    {
        var packs = AuraGameDataHostApi.Table(DataType.CardPack)
            .Select(pack => new
            {
                Pack = pack,
                Name = DisplayName(pack, "未命名卡包"),
                Count = cards.Count(card =>
                    string.Equals(
                        card.OwnerModId,
                        pack.OwnerModId,
                        StringComparison.OrdinalIgnoreCase)
                    && BelongsToPack(card, pack.Id))
            })
            .Where(entry => entry.Count > 0)
            .ToArray();
        var duplicateNames = packs
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return packs
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Pack.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                var owner = duplicateNames.Contains(entry.Name)
                    ? " · "
                      + AuraToolsPlayerDisplay.OwnerName(entry.Pack.OwnerModId)
                    : "";
                return new ToolboxSearchOption(
                    Qualify(entry.Pack.OwnerModId, entry.Pack.Id),
                    entry.Name + owner + "（" + entry.Count + " 张）",
                    entry.Name
                    + " "
                    + entry.Pack.Id
                    + " "
                    + entry.Pack.OwnerModId);
            })
            .ToArray();
    }

    private static IReadOnlyList<ToolboxSearchOption> RarityOptions(
        IReadOnlyList<AuraGameDataSnapshot> cards)
    {
        return cards
            .Select(card => Field(card, "Rarity"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => NumericSortKey(group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ToolboxSearchOption(
                group.Key,
                "稀有度 " + group.Key + "（" + group.Count() + " 张）",
                "稀有度 " + group.Key))
            .ToArray();
    }

    private static string ResolvePackName(
        AuraGameDataSnapshot card,
        IReadOnlyList<AuraGameDataSnapshot> packs)
    {
        var packId = SplitPackIds(card).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(packId))
        {
            return "";
        }
        var pack = packs.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    packId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    candidate.OwnerModId,
                    card.OwnerModId,
                    StringComparison.OrdinalIgnoreCase))
                   ?? packs.FirstOrDefault(candidate => string.Equals(
                       candidate.Id,
                       packId,
                       StringComparison.OrdinalIgnoreCase));
        return pack == null ? "" : DisplayName(pack, "");
    }

    private static string DisplayName(
        AuraGameDataSnapshot snapshot,
        string fallback)
    {
        try
        {
            var fields = snapshot.Fields.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            var localized = fields.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized)
                && !string.Equals(
                    localized,
                    "Name",
                    StringComparison.OrdinalIgnoreCase))
            {
                return localized.Trim();
            }
            if (fields.TryGetValue("Name", out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch
        {
        }
        return fallback;
    }

    private static bool BelongsToPack(
        AuraGameDataSnapshot card,
        string packId)
    {
        return SplitPackIds(card).Any(value => string.Equals(
            value,
            packId,
            StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SplitPackIds(
        AuraGameDataSnapshot card)
    {
        return Field(card, "PackBelong")
            .Split(
                new[] { ',', ';', '|' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0);
    }

    private static string Field(
        AuraGameDataSnapshot snapshot,
        string name)
    {
        return snapshot.Fields.TryGetValue(name, out var value)
            ? value?.Trim() ?? ""
            : "";
    }

    private static string Qualify(string ownerModId, string id)
    {
        return (ownerModId ?? "").Trim() + ":" + (id ?? "").Trim();
    }

    private static int NumericSortKey(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : int.MaxValue;
    }
}
