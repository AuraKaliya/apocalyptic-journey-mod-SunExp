using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraGameData.Shared.GameApi;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.CardRefresh;

internal static class CardChoiceRefreshNativeApi
{
    private const int ChoiceCount = 3;
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo?[] ItemFields =
    {
        typeof(CardChoiceUI).GetField("Item1", InstanceFlags),
        typeof(CardChoiceUI).GetField("Item2", InstanceFlags),
        typeof(CardChoiceUI).GetField("Item3", InstanceFlags)
    };

    private static readonly FieldInfo? SelectedField = typeof(CardChoiceUI).GetField("isSelected", InstanceFlags);
    private static readonly FieldInfo? ChoiceDataField = typeof(CardChoiceItem).GetField("dataConfig", InstanceFlags);
    private static readonly ConstructorInfo? DiceCopyConstructor = typeof(Dice).GetConstructor(
        InstanceFlags,
        binder: null,
        new[] { typeof(Dice) },
        modifiers: null);

    public static bool Compatible => ItemFields.All(itemField => itemField != null)
                                     && SelectedField != null
                                     && ChoiceDataField != null
                                     && DiceCopyConstructor != null;

    public static bool IsBattleRewardContext()
    {
        try
        {
            return UIManager.Instance?.GetUI<BattleRewardsUI>("BattleRewardsUI") != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSelected(CardChoiceUI ui)
    {
        try
        {
            return SelectedField?.GetValue(ui) is not bool selected || selected;
        }
        catch
        {
            return true;
        }
    }

    public static bool TryGetItems(CardChoiceUI ui, out GameObject[] items)
    {
        items = new GameObject[ChoiceCount];
        if (!Compatible)
        {
            return false;
        }

        try
        {
            for (var i = 0; i < ChoiceCount; i++)
            {
                if (ItemFields[i]!.GetValue(ui) is not GameObject item || item == null)
                {
                    return false;
                }

                items[i] = item;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySetItems(CardChoiceUI ui, IReadOnlyList<GameObject> items)
    {
        if (!Compatible || items == null || items.Count != ChoiceCount)
        {
            return false;
        }

        try
        {
            for (var i = 0; i < ChoiceCount; i++)
            {
                if (items[i] == null)
                {
                    return false;
                }

                ItemFields[i]!.SetValue(ui, items[i]);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static List<string> CurrentChoiceIds(IEnumerable<GameObject> items)
    {
        var ids = new List<string>(ChoiceCount);
        foreach (var itemObject in items ?? Array.Empty<GameObject>())
        {
            try
            {
                var item = itemObject?.GetComponent<CardChoiceItem>();
                if (item == null || ChoiceDataField?.GetValue(item) is not IDataConfig config)
                {
                    continue;
                }

                if (config.data != null
                    && config.data.TryGetValue("Id", out var id)
                    && !string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
            catch
            {
            }
        }

        return ids;
    }

    public static Dice? CloneCurrentDice()
    {
        try
        {
            var dice = MapManager.Instance?.NowDice;
            return dice == null ? null : DiceCopyConstructor?.Invoke(new object[] { dice }) as Dice;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryDrawChoices(Dice dice, IReadOnlyCollection<string> currentIds, out List<string> cardIds)
    {
        cardIds = new List<string>(ChoiceCount);
        if (dice == null)
        {
            return false;
        }

        try
        {
            var manager = Singleton<GameConfigManager>.Instance;
            var runtime = Singleton<GameRuntimeData>.Instance;
            var rows = AuraGameDataHostApi.Rows(DataType.Card);
            if (manager == null || runtime == null || rows == null)
            {
                return false;
            }

            var eligible = rows
                .Where(row => row != null
                              && row.TryGetValue("Id", out var id)
                              && !string.IsNullOrWhiteSpace(id)
                              && (!row.TryGetValue("Type", out var type) || type != "诅咒")
                              && !runtime.IsLocked(id))
                .ToList();
            eligible = manager.CardPackCheck(eligible);
            var pool = CardRefreshPoolPolicy.PreferDifferentChoices(
                eligible,
                currentIds,
                ChoiceCount,
                row => row.TryGetValue("Id", out var id) ? id : "");
            if (pool.Count < ChoiceCount)
            {
                return false;
            }

            var drawn = new RandomPool(pool, dice).DrawByRarity(ChoiceCount);
            if (drawn.Count < ChoiceCount)
            {
                return false;
            }

            foreach (var row in drawn.Take(ChoiceCount))
            {
                if (!row.TryGetValue("Id", out var id) || string.IsNullOrWhiteSpace(id))
                {
                    return false;
                }

                cardIds.Add(id);
            }

            return cardIds.Count == ChoiceCount;
        }
        catch
        {
            cardIds.Clear();
            return false;
        }
    }
}
