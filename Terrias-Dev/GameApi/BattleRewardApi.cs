using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraGameData.Shared.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class BattleRewardApi
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool ReplaceWithRewardSpec(BattleRewardsUI? rewardUi, EndlessSeaRewardSpec spec, string source)
    {
        if (rewardUi == null || spec == null)
        {
            return false;
        }

        ClearGeneratedRewards(rewardUi, source);

        var applied = false;
        applied |= AppendRandomCardRewards(rewardUi, spec.CardChoices, source);
        for (var i = 0; i < spec.BlessingChoices; i++)
        {
            applied |= AppendBlessingReward(rewardUi, source);
        }

        foreach (var rarities in spec.RelicRarityGroups)
        {
            applied |= AppendRandomRelicReward(rewardUi, rarities, source);
        }

        return applied;
    }

    public static void ClearGeneratedRewards(BattleRewardsUI? rewardUi, string source)
    {
        if (rewardUi == null)
        {
            return;
        }

        try
        {
            var itemList = ReadMember<Transform>(rewardUi, "itemList");
            var template = ReadMember<GameObject>(rewardUi, "item1");
            if (itemList != null)
            {
                var children = new List<GameObject>();
                foreach (Transform child in itemList)
                {
                    if (template != null && child.gameObject == template)
                    {
                        continue;
                    }

                    if (string.Equals(child.name, "Text", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    children.Add(child.gameObject);
                }

                foreach (var child in children)
                {
                    child.SetActive(false);
                    UnityEngine.Object.Destroy(child);
                }
            }

            if (template != null)
            {
                template.SetActive(false);
            }

            WriteMember(rewardUi, "Money", 0);
            WriteMember(rewardUi, "CardCount", 0);
            if (ReadMember<object>(rewardUi, "RelicRewardList") is IList relicRewards)
            {
                relicRewards.Clear();
            }

            SunExpLog.Debug("[BattleRewardApi] cleared generated rewards from "
                + source
                + "; snapshot="
                + GeneratedRewardSnapshot(rewardUi));
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[BattleRewardApi] clear generated rewards failed from " + source + ": " + ex.Message);
        }
    }

    public static string GeneratedRewardSnapshot(BattleRewardsUI? rewardUi)
    {
        if (rewardUi == null)
        {
            return "ui=<null>";
        }

        try
        {
            var itemList = ReadMember<Transform>(rewardUi, "itemList");
            var template = ReadMember<GameObject>(rewardUi, "item1");
            var children = 0;
            var activeChildren = 0;
            if (itemList != null)
            {
                foreach (Transform child in itemList)
                {
                    if (template != null && child.gameObject == template)
                    {
                        continue;
                    }

                    if (string.Equals(child.name, "Text", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    children++;
                    if (child.gameObject.activeSelf)
                    {
                        activeChildren++;
                    }
                }
            }

            var relicCount = ReadMember<object>(rewardUi, "RelicRewardList") is IList relicRewards ? relicRewards.Count : 0;
            return "children="
                + children
                + ", activeChildren="
                + activeChildren
                + ", money="
                + ReadMember<int>(rewardUi, "Money")
                + ", cardCount="
                + ReadMember<int>(rewardUi, "CardCount")
                + ", relicRewards="
                + relicCount;
        }
        catch (Exception ex)
        {
            return "snapshotError=" + ex.Message;
        }
    }

    public static bool AppendRandomCardRewards(BattleRewardsUI? rewardUi, int count, string source)
    {
        if (rewardUi == null || count <= 0)
        {
            return false;
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                rewardUi.RandomSetCard();
            }

            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[BattleRewardApi] random card reward failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool AppendRandomRelicReward(BattleRewardsUI? rewardUi, string source)
    {
        return AppendRandomRelicReward(rewardUi, Array.Empty<int>(), source);
    }

    public static bool AppendRandomRelicReward(BattleRewardsUI? rewardUi, IReadOnlyCollection<int> rarities, string source)
    {
        if (rewardUi == null)
        {
            return false;
        }

        try
        {
            var candidates = BuildRandomRelicRewardRows(rarities);
            if (candidates.Count == 0)
            {
                SunExpLog.Warn("[BattleRewardApi] random relic reward skipped; no candidates from " + source);
                return false;
            }

            rewardUi.RandomSetRelic(candidates);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[BattleRewardApi] random relic reward failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool AppendBlessingReward(BattleRewardsUI? rewardUi, string source)
    {
        if (rewardUi == null)
        {
            return false;
        }

        try
        {
            rewardUi.RandomAddBless();
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[BattleRewardApi] random blessing reward failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool IsCurrentBattleReward()
    {
        try
        {
            var levelId = FightManager.Instance?.level;
            if (!string.IsNullOrWhiteSpace(levelId))
            {
                return true;
            }

            var data = MapManager.Instance?.MapTree?.currentNode?.data;
            return string.Equals(DictionaryUtil.Get(data, "Type"), "Fight", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static List<Dictionary<string, string>> BuildRandomRelicRewardRows(IReadOnlyCollection<int> rarities)
    {
        var manager = Singleton<GameConfigManager>.Instance;
        var role = RoleTable.Instance;
        if (manager == null || role == null)
        {
            return new List<Dictionary<string, string>>();
        }

        var all = AuraGameDataHostApi.CopyTableForHostInterop(DataType.Relic);
        if (all.Count == 0)
        {
            return new List<Dictionary<string, string>>();
        }

        var raritySet = rarities == null || rarities.Count == 0
            ? new HashSet<int>()
            : rarities.ToHashSet();
        bool RarityAllowed(Dictionary<string, string> row)
        {
            if (raritySet.Count == 0)
            {
                return DictionaryUtil.Get(row, "Rarity") != "4";
            }

            var rarity = DictionaryUtil.ParseInt(DictionaryUtil.Get(row, "Rarity"), 0);
            return raritySet.Contains(rarity);
        }

        var candidates = all
            .Where(row => row != null
                && RarityAllowed(row)
                && !SunExpIds.IsHiddenRelicId(DictionaryUtil.Get(row, "Id"))
                && !role.relicGets.ContainsKey(DictionaryUtil.Get(row, "Id"))
                && !Singleton<GameRuntimeData>.Instance.IsLocked(DictionaryUtil.Get(row, "Id")))
            .ToList();
        candidates = manager.CardPackCheck(candidates);

        if (candidates.Count > 0)
        {
            return candidates;
        }

        if (role.relicGets.Count < all.Count)
        {
            return candidates;
        }

        return all
            .Where(row => row != null
                && RarityAllowed(row)
                && !SunExpIds.IsHiddenRelicId(DictionaryUtil.Get(row, "Id")))
            .ToList();
    }

    private static T? ReadMember<T>(object instance, string name)
    {
        var type = instance.GetType();
        var field = type.GetField(name, InstanceFlags);
        if (field != null)
        {
            return field.GetValue(instance) is T value ? value : default;
        }

        var property = type.GetProperty(name, InstanceFlags);
        return property != null && property.GetValue(instance, null) is T propertyValue ? propertyValue : default;
    }

    private static void WriteMember(object instance, string name, object value)
    {
        var type = instance.GetType();
        var field = type.GetField(name, InstanceFlags);
        if (field != null)
        {
            field.SetValue(instance, value);
            return;
        }

        var property = type.GetProperty(name, InstanceFlags);
        if (property != null && property.CanWrite)
        {
            property.SetValue(instance, value, null);
        }
    }
}
