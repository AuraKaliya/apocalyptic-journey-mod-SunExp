using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class BattleRewardApi
{
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
        if (rewardUi == null)
        {
            return false;
        }

        try
        {
            var candidates = BuildRandomRelicRewardRows();
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

    private static List<Dictionary<string, string>> BuildRandomRelicRewardRows()
    {
        var manager = Singleton<GameConfigManager>.Instance;
        var role = RoleTable.Instance;
        if (manager == null || role == null)
        {
            return new List<Dictionary<string, string>>();
        }

        var all = manager.GetTable(DataType.Relic)?.Getlines() ?? new List<Dictionary<string, string>>();
        if (all.Count == 0)
        {
            return new List<Dictionary<string, string>>();
        }

        var candidates = all
            .Where(row => row != null
                && DictionaryUtil.Get(row, "Rarity") != "4"
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
            .Where(row => row != null && DictionaryUtil.Get(row, "Rarity") != "4")
            .ToList();
    }
}
