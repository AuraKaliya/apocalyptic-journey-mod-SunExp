using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using Witch;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

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
        return CanClaim(EndlessSeaModeRuntimeCurrentFloor());
    }

    public static bool CanClaim(int floor)
    {
        floor = Math.Max(1, floor);
        var key = Key(floor);
        return floor >= EndlessAbyssConfigStore.Current.Milestones.MinFloor
            && !EndlessAbyssRunLedger.Contains(key)
            && !EndlessAbyssRunLedger.ContainsPrefix(ResultPrefix(floor));
    }

    public static IReadOnlyList<EndlessAbyssRelicOption> RelicCandidates()
    {
        try
        {
            var rows = TerriasConfigIndex.Rows(DataType.Relic);
            var checkedRows = Singleton<GameConfigManager>.Instance.CardPackCheck(rows);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return checkedRows
                .Where(row =>
                {
                    var id = DictionaryUtil.Get(row, "Id");
                    var tier = DictionaryUtil.ParseInt(DictionaryUtil.Get(row, "Rarity"), -1);
                    return !string.IsNullOrWhiteSpace(id)
                        && !id.StartsWith("*", StringComparison.Ordinal)
                        && !TerriasIds.IsHiddenRelicId(id)
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
            TerriasLog.Warn("[EndlessAbyssMilestone] relic candidates failed: " + ex.Message);
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
        var resolution = new EndlessAbyssMilestoneResolution
        {
            Floor = Math.Max(1, floor),
            Kind = EndlessAbyssMilestoneRewardKind.Relic,
            RelicId = relicId ?? "",
            Source = "GrantRelic",
            Token = Guid.NewGuid().ToString("N")
        };
        return ApplyResolution(resolution, "EndlessAbyssMilestone.GrantRelic", broadcast: false, out message);
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
                .Where(id => !string.IsNullOrWhiteSpace(id) && TerriasConfigIndex.Row(DataType.Card, id) != null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (ids.Count == 0)
        {
            message = "\u5f02\u6b21\u5143\u5361\u6c60\u4e3a\u7a7a\u3002";
            return false;
        }

        var cardId = ids[PickIndex(ids.Count)];
        var resolution = new EndlessAbyssMilestoneResolution
        {
            Floor = Math.Max(1, floor),
            Kind = EndlessAbyssMilestoneRewardKind.OtherDimensionCard,
            CardId = cardId,
            Source = "GrantOtherDimensionCard",
            Token = Guid.NewGuid().ToString("N")
        };
        return ApplyResolution(resolution, "EndlessAbyssMilestone.OtherDimensionCard", broadcast: false, out message);
    }

    public static bool RemoveBurnout(int floor, IDataConfig card, out string message)
    {
        message = "";
        var resolution = CardResolution(
            floor,
            EndlessAbyssMilestoneRewardKind.RemoveBurnout,
            card,
            "RemoveBurnout");
        return ApplyResolution(resolution, "EndlessAbyssMilestone.RemoveBurnout", broadcast: false, out message);
    }

    public static bool AddExtinction(int floor, IDataConfig card, out string message)
    {
        message = "";
        var resolution = CardResolution(
            floor,
            EndlessAbyssMilestoneRewardKind.AddExtinction,
            card,
            "AddExtinction");
        return ApplyResolution(resolution, "EndlessAbyssMilestone.AddExtinction", broadcast: false, out message);
    }

    public static bool ApplyNetworkResolution(EndlessAbyssMilestoneResolution? resolution, string source)
    {
        if (resolution == null)
        {
            return false;
        }

        return ApplyResolution(resolution, source, broadcast: false, out _);
    }

    private static bool ApplyResolution(
        EndlessAbyssMilestoneResolution resolution,
        string source,
        bool broadcast,
        out string message)
    {
        message = "";
        var floor = Math.Max(1, resolution?.Floor ?? 1);
        if (resolution == null || !CanClaim(floor))
        {
            message = "\u5f53\u524d\u91cc\u7a0b\u7891\u5df2\u7ed3\u7b97\u3002";
            return true;
        }

        try
        {
            var success = resolution.Kind switch
            {
                EndlessAbyssMilestoneRewardKind.Relic => ApplyRelicResolution(floor, resolution, out message),
                EndlessAbyssMilestoneRewardKind.OtherDimensionCard => ApplyOtherDimensionResolution(floor, resolution, out message),
                EndlessAbyssMilestoneRewardKind.RemoveBurnout => ApplyRemoveBurnoutResolution(floor, resolution, out message),
                EndlessAbyssMilestoneRewardKind.AddExtinction => ApplyAddExtinctionResolution(floor, resolution, out message),
                _ => UnknownResolution(resolution, out message)
            };

            if (success && broadcast)
            {
                BroadcastResolution(resolution, source);
            }

            return success;
        }
        catch (Exception ex)
        {
            message = "\u91cc\u7a0b\u7891\u5956\u52b1\u7ed3\u7b97\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002";
            TerriasLog.Warn("[EndlessAbyssMilestone] resolution failed from "
                + source
                + ": "
                + ex.Message);
            return false;
        }
    }

    private static bool ApplyRelicResolution(int floor, EndlessAbyssMilestoneResolution resolution, out string message)
    {
        if (string.IsNullOrWhiteSpace(resolution.RelicId))
        {
            message = "\u9057\u7269\u7ed3\u7b97\u7f3a\u5c11 ID\u3002";
            return false;
        }

        PlayerApi.AddRelic(resolution.RelicId);
        Claim(floor, "relic:" + resolution.RelicId);
        message = "\u83b7\u5f97\u9057\u7269\uff1a" + RelicName(resolution.RelicId);
        PlayerApi.ShowCaption(message);
        return true;
    }

    private static bool ApplyOtherDimensionResolution(int floor, EndlessAbyssMilestoneResolution resolution, out string message)
    {
        if (string.IsNullOrWhiteSpace(resolution.CardId))
        {
            message = "\u5f02\u6b21\u5143\u5361\u7ed3\u7b97\u7f3a\u5c11 ID\u3002";
            return false;
        }

        if (!PlayerApi.TryAddCardToDeck(resolution.CardId, out var grantedCardId, out var error))
        {
            message = "\u5f02\u6b21\u5143\u5361\u83b7\u53d6\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002";
            TerriasLog.Warn("[EndlessAbyssMilestone] other-dimension card grant failed; card="
                + resolution.CardId
                + "; error="
                + error);
            return false;
        }

        EndlessSeaCardAffixService.NormalizeOwnedCards("EndlessAbyssMilestone.OtherDimensionCard");
        EndlessSeaCardAffixService.TryPersistCurrentRole("EndlessAbyssMilestone.OtherDimensionCard");
        Claim(floor, "other-dimension:" + grantedCardId);
        message = "\u83b7\u5f97\u5f02\u6b21\u5143\u5361\uff1a" + CardName(grantedCardId);
        PlayerApi.ShowCaption(message);
        return true;
    }

    private static bool ApplyRemoveBurnoutResolution(int floor, EndlessAbyssMilestoneResolution resolution, out string message)
    {
        var card = ResolveDeckCard(resolution, BurnoutCards);
        if (card == null)
        {
            Claim(floor, "remove-burnout:none");
            message = "\u5f53\u524d\u5361\u7ec4\u6ca1\u6709\u53ef\u6e05\u9664\u711a\u6bc1\u7684\u5361\u3002";
            PlayerApi.ShowCaption(message);
            return true;
        }

        if (!CardMutationService.RemoveNativeTags(card, BurnoutTag))
        {
            Claim(floor, "remove-burnout:unchanged");
            message = "\u8be5\u5361\u6ca1\u6709\u53ef\u6e05\u9664\u7684\u711a\u6bc1\u3002";
            PlayerApi.ShowCaption(message);
            return true;
        }

        EndlessSeaCardAffixService.TryPersistCurrentRole("EndlessAbyssMilestone.RemoveBurnout");
        Claim(floor, "remove-burnout:" + card.InstanceID);
        message = "\u5df2\u6e05\u9664\u711a\u6bc1\uff1a" + CardDisplayName(card);
        PlayerApi.ShowCaption(message);
        return true;
    }

    private static bool ApplyAddExtinctionResolution(int floor, EndlessAbyssMilestoneResolution resolution, out string message)
    {
        var card = ResolveDeckCard(resolution, ExtinctionTargets);
        if (card == null)
        {
            Claim(floor, "add-extinction:none");
            message = "\u5f53\u524d\u5361\u7ec4\u6ca1\u6709\u53ef\u6dfb\u52a0\u7edd\u706d\u7684\u5361\u3002";
            PlayerApi.ShowCaption(message);
            return true;
        }

        if (HasExtinction(card))
        {
            Claim(floor, "add-extinction:unchanged");
            message = "\u8be5\u5361\u5df2\u7ecf\u62e5\u6709\u7edd\u706d\u3002";
            PlayerApi.ShowCaption(message);
            return true;
        }

        if (card is not DataConfig dataConfig)
        {
            message = "\u8be5\u5361\u6682\u4e0d\u652f\u6301\u7edd\u706d\u9644\u7740\u3002";
            return false;
        }

        OriginMilestoneService.AttachExtinctionEnchTag(dataConfig);
        EndlessSeaCardAffixService.TryPersistCurrentRole("EndlessAbyssMilestone.AddExtinction");
        Claim(floor, "add-extinction:" + card.InstanceID);
        message = "\u5df2\u6dfb\u52a0\u7edd\u706d\uff1a" + CardDisplayName(card);
        PlayerApi.ShowCaption(message);
        return true;
    }

    private static bool UnknownResolution(EndlessAbyssMilestoneResolution resolution, out string message)
    {
        message = "\u672a\u77e5\u7684\u91cc\u7a0b\u7891\u5956\u52b1\uff1a" + (resolution.Kind ?? "");
        return false;
    }

    private static IDataConfig? ResolveDeckCard(
        EndlessAbyssMilestoneResolution resolution,
        Func<IReadOnlyList<EndlessAbyssCardOption>> candidatesProvider)
    {
        var candidates = candidatesProvider();
        var byInstance = candidates.FirstOrDefault(option =>
            !string.IsNullOrWhiteSpace(resolution.CardInstanceId)
            && string.Equals(option.InstanceId, resolution.CardInstanceId, StringComparison.Ordinal));
        if (byInstance?.Card != null)
        {
            return byInstance.Card;
        }

        var byBase = candidates.FirstOrDefault(option =>
            !string.IsNullOrWhiteSpace(resolution.CardBaseId)
            && string.Equals(CardConfigApi.Id(option.Card), resolution.CardBaseId, StringComparison.Ordinal));
        return byBase?.Card ?? candidates.FirstOrDefault()?.Card;
    }

    private static EndlessAbyssMilestoneResolution CardResolution(
        int floor,
        string kind,
        IDataConfig card,
        string source)
    {
        return new EndlessAbyssMilestoneResolution
        {
            Floor = Math.Max(1, floor),
            Kind = kind,
            CardInstanceId = card?.InstanceID ?? "",
            CardBaseId = CardConfigApi.Id(card),
            CardId = CardConfigApi.Id(card),
            Source = source,
            Token = Guid.NewGuid().ToString("N")
        };
    }

    private static void BroadcastResolution(EndlessAbyssMilestoneResolution resolution, string source)
    {
        if (!TerriasNetworkRuntime.IsMultiplayerSession() || TerriasNetworkRuntime.IsClientOnly())
        {
            return;
        }

        var snapshot = EndlessSeaStateSnapshot.Capture(source + ":milestone-resolution");
        TerriasNetworkRuntime.Send(
            new RpcEndlessAbyssMilestoneResolution(resolution, snapshot, source),
            source);
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
            TerriasLog.Warn("[EndlessAbyssMilestone] card scan failed: " + ex.Message);
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
        EndlessAbyssRunLedger.TryClaim(ResultKey(floor, source), "milestone-result:" + source);
        EndlessAbyssRunLedger.TryClaim(Key(floor), "milestone:" + source);
    }

    private static string Key(int floor)
    {
        return "milestone:player:" + PlayerScopeKey() + ":floor:" + Math.Max(1, floor);
    }

    private static string ResultKey(int floor, string source)
    {
        return ResultPrefix(floor) + Sanitize(source);
    }

    private static string ResultPrefix(int floor)
    {
        return Key(floor) + ":result:";
    }

    private static string PlayerScopeKey()
    {
        var playerId = TerriasNetworkRuntime.LocalPlayerId();
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return Sanitize(playerId);
        }

        var roleId = RoleTable.Instance?.Id ?? "";
        return string.IsNullOrWhiteSpace(roleId) ? "solo" : Sanitize(roleId);
    }

    private static string Sanitize(string value)
    {
        var clean = new string((value ?? "")
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    private static int EndlessSeaModeRuntimeCurrentFloor()
    {
        return Math.Max(1, GameSaveManager.GetValue<int>(TerriasIds.EndlessSeaFloorKey));
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
        var row = TerriasConfigIndex.Row(DataType.Relic, relicId);
        return row == null ? relicId : DisplayName(row, relicId);
    }

    private static string CardName(string cardId)
    {
        try
        {
            var row = TerriasConfigIndex.Row(DataType.Card, CardApi.ResolveCardId(cardId))
                      ?? TerriasConfigIndex.Row(DataType.Card, cardId);
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
