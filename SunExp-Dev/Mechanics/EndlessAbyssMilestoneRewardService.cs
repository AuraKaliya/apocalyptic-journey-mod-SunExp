using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class EndlessAbyssRelicOption
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public int Tier { get; set; }
}

public sealed class EndlessAbyssCardOption
{
    public IDataConfig Card { get; set; } = null!;

    public string InstanceId { get; set; } = "";

    public string Name { get; set; } = "";
}

public static class EndlessAbyssMilestoneRewardService
{
    private const string BurnoutTag = "Burnout";

    public static bool CanClaimCurrentFloor()
    {
        return CanClaim(TongtianTowerModeRuntimeCurrentFloor());
    }

    public static bool CanClaim(int floor)
    {
        floor = Math.Max(1, floor);
        return floor >= EndlessAbyssConfigStore.Current.Milestones.MinFloor
            && !EndlessAbyssRunLedger.Contains(Key(floor));
    }

    public static IReadOnlyList<EndlessAbyssRelicOption> RelicCandidates()
    {
        try
        {
            var rows = SunExpConfigIndex.Rows(DataType.Relic);
            var checkedRows = Singleton<GameConfigManager>.Instance.CardPackCheck(rows);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return checkedRows
                .Where(row =>
                {
                    var id = DictionaryUtil.Get(row, "Id");
                    var tier = DictionaryUtil.ParseInt(DictionaryUtil.Get(row, "Rarity"), -1);
                    return !string.IsNullOrWhiteSpace(id)
                        && !id.StartsWith("*", StringComparison.Ordinal)
                        && !SunExpIds.IsHiddenRelicId(id)
                        && tier >= 1
                        && tier <= 3
                        && seen.Add(id)
                        && !IsLocked(id);
                })
                .Select(row => new EndlessAbyssRelicOption
                {
                    Id = DictionaryUtil.Get(row, "Id"),
                    Name = DisplayName(row, DictionaryUtil.Get(row, "Id")),
                    Tier = DictionaryUtil.ParseInt(DictionaryUtil.Get(row, "Rarity"), 1)
                })
                .OrderBy(option => option.Tier)
                .ThenBy(option => option.Name, StringComparer.Ordinal)
                .ThenBy(option => option.Id, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssMilestone] relic candidates failed: " + ex.Message);
            return Array.Empty<EndlessAbyssRelicOption>();
        }
    }

    public static IReadOnlyList<EndlessAbyssCardOption> BurnoutCards()
    {
        return CurrentDeckCards()
            .Where(option => HasNativeTag(option.Card, BurnoutTag))
            .ToList();
    }

    public static IReadOnlyList<EndlessAbyssCardOption> ExtinctionTargets()
    {
        return CurrentDeckCards()
            .Where(option => !HasExtinction(option.Card))
            .ToList();
    }

    public static bool GrantRelic(int floor, string relicId, out string message)
    {
        message = "";
        if (!CanClaim(floor) || string.IsNullOrWhiteSpace(relicId))
        {
            message = "\u5f53\u524d\u91cc\u7a0b\u7891\u5df2\u7ed3\u7b97\u3002";
            return false;
        }

        try
        {
            PlayerApi.AddRelic(relicId);
            Claim(floor, "relic:" + relicId);
            message = "\u83b7\u5f97\u9057\u7269\uff1a" + RelicName(relicId);
            PlayerApi.ShowCaption(message);
            return true;
        }
        catch (Exception ex)
        {
            message = "\u9057\u7269\u83b7\u53d6\u5931\u8d25\uff1a" + ex.Message;
            return false;
        }
    }

    public static bool GrantRandomOtherDimensionCard(int floor, out string message)
    {
        message = "";
        if (!CanClaim(floor))
        {
            message = "\u5f53\u524d\u91cc\u7a0b\u7891\u5df2\u7ed3\u7b97\u3002";
            return false;
        }

        var config = EndlessAbyssConfigStore.Current.Rewards;
        var ids = EndlessAbyssRewardPoolService.CardIds(config.OtherDimensionCardPoolId).ToList();
        if (ids.Count == 0)
        {
            ids = config.OtherDimensionCardIds
                .Select(CardApi.ResolveCardId)
                .Where(id => !string.IsNullOrWhiteSpace(id) && SunExpConfigIndex.Row(DataType.Card, id) != null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (ids.Count == 0)
        {
            message = "\u5f02\u6b21\u5143\u5361\u6c60\u4e3a\u7a7a\u3002";
            return false;
        }

        var cardId = ids[PickIndex(ids.Count)];
        try
        {
            if (!PlayerApi.TryAddCardToDeck(cardId, out var grantedCardId, out var error))
            {
                message = "\u5f02\u6b21\u5143\u5361\u83b7\u53d6\u5931\u8d25\uff1a" + error;
                return false;
            }

            TongtianTowerCardAffixService.NormalizeOwnedCards("EndlessAbyssMilestone.OtherDimensionCard");
            GameSaveManager.UpdateRoles(RoleTable.Instance);
            Claim(floor, "other-dimension:" + grantedCardId);
            message = "\u83b7\u5f97\u5f02\u6b21\u5143\u5361\uff1a" + CardName(grantedCardId);
            PlayerApi.ShowCaption(message);
            return true;
        }
        catch (Exception ex)
        {
            message = "\u5f02\u6b21\u5143\u5361\u83b7\u53d6\u5931\u8d25\uff1a" + ex.Message;
            return false;
        }
    }

    public static bool RemoveBurnout(int floor, IDataConfig card, out string message)
    {
        message = "";
        if (!CanClaim(floor) || card == null)
        {
            message = "\u5f53\u524d\u91cc\u7a0b\u7891\u5df2\u7ed3\u7b97\u3002";
            return false;
        }

        if (!CardMutationService.RemoveNativeTags(card, BurnoutTag))
        {
            message = "\u8be5\u5361\u6ca1\u6709\u53ef\u6e05\u9664\u7684\u711a\u6bc1\u3002";
            return false;
        }

        GameSaveManager.UpdateRoles(RoleTable.Instance);
        Claim(floor, "remove-burnout:" + card.InstanceID);
        message = "\u5df2\u6e05\u9664\u711a\u6bc1\uff1a" + CardDisplayName(card);
        PlayerApi.ShowCaption(message);
        return true;
    }

    public static bool AddExtinction(int floor, IDataConfig card, out string message)
    {
        message = "";
        if (!CanClaim(floor) || card == null)
        {
            message = "\u5f53\u524d\u91cc\u7a0b\u7891\u5df2\u7ed3\u7b97\u3002";
            return false;
        }

        if (HasExtinction(card))
        {
            message = "\u8be5\u5361\u5df2\u7ecf\u62e5\u6709\u7edd\u706d\u3002";
            return false;
        }

        if (card is DataConfig dataConfig)
        {
            TongtianTowerOriginService.AttachExtinctionEnchTag(dataConfig);
        }
        else
        {
            message = "\u8be5\u5361\u6682\u4e0d\u652f\u6301\u7edd\u706d\u9644\u7740\u3002";
            return false;
        }

        GameSaveManager.UpdateRoles(RoleTable.Instance);
        Claim(floor, "add-extinction:" + card.InstanceID);
        message = "\u5df2\u6dfb\u52a0\u7edd\u706d\uff1a" + CardDisplayName(card);
        PlayerApi.ShowCaption(message);
        return true;
    }

    private static IReadOnlyList<EndlessAbyssCardOption> CurrentDeckCards()
    {
        try
        {
            return (RoleTable.Instance?.cardList ?? Enumerable.Empty<IDataConfig>())
                .Where(card => card != null)
                .Select(card => new EndlessAbyssCardOption
                {
                    Card = card,
                    InstanceId = card.InstanceID ?? "",
                    Name = CardDisplayName(card)
                })
                .OrderBy(option => option.Name, StringComparer.Ordinal)
                .ThenBy(option => option.InstanceId, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssMilestone] card scan failed: " + ex.Message);
            return Array.Empty<EndlessAbyssCardOption>();
        }
    }

    private static bool HasNativeTag(IDataConfig card, string tag)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.Vars, "Tag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.data, "Tag"), tag);
    }

    private static bool HasExtinction(IDataConfig card)
    {
        try
        {
            return RoleTable.Instance?.enchasedDict != null
                && !string.IsNullOrWhiteSpace(card.InstanceID)
                && RoleTable.Instance.enchasedDict.ContainsKey(card.InstanceID);
        }
        catch
        {
            return false;
        }
    }

    private static void Claim(int floor, string source)
    {
        EndlessAbyssRunLedger.TryClaim(Key(floor), "milestone:" + source);
    }

    private static string Key(int floor)
    {
        return "milestone:floor:" + Math.Max(1, floor);
    }

    private static int TongtianTowerModeRuntimeCurrentFloor()
    {
        return Math.Max(1, GameSaveManager.GetValue<int>(SunExpIds.TongtianTowerFloorKey));
    }

    private static bool IsLocked(string id)
    {
        try
        {
            return Singleton<GameRuntimeData>.Instance != null
                && Singleton<GameRuntimeData>.Instance.IsLocked(id);
        }
        catch
        {
            return false;
        }
    }

    private static string RelicName(string relicId)
    {
        var row = SunExpConfigIndex.Row(DataType.Relic, relicId);
        return row == null ? relicId : DisplayName(row, relicId);
    }

    private static string CardName(string cardId)
    {
        try
        {
            var row = SunExpConfigIndex.Row(DataType.Card, CardApi.ResolveCardId(cardId))
                      ?? SunExpConfigIndex.Row(DataType.Card, cardId);
            return row == null ? cardId : DisplayName(row, cardId);
        }
        catch
        {
            return cardId;
        }
    }

    private static string CardDisplayName(IDataConfig card)
    {
        var name = DictionaryUtil.Get(card?.Vars, "Name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        name = DictionaryUtil.Get(card?.data, "Name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return DictionaryUtil.Get(card?.data, "Id", card?.InstanceID ?? "");
    }

    private static string DisplayName(IDictionary<string, string>? data, string fallback)
    {
        if (data == null)
        {
            return fallback ?? "";
        }

        var name = DictionaryUtil.Get(data, "Name");
        return string.IsNullOrWhiteSpace(name) ? fallback ?? "" : name;
    }

    private static int PickIndex(int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        try
        {
            return Math.Abs((MapManager.Instance?.NowDice ?? Dice.Default).Roll().Value) % count;
        }
        catch
        {
            return Math.Abs(Environment.TickCount) % count;
        }
    }
}
