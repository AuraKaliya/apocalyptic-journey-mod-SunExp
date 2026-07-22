using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public enum DimensionShopItemState
{
    Available,
    InsufficientTruth,
    Purchased,
    SoldOut,
    Owned,
    Empty,
    Unavailable
}

public sealed class DimensionShopItemView
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconPath { get; set; } = "";

    public int Price { get; set; }

    public string Status { get; set; } = "";

    public bool CanBuy { get; set; }

    public DimensionShopItemState State { get; set; }
}

public sealed class DimensionShopHeldItemView
{
    public DataConfig? NativeConfig { get; set; }

    public string InstanceId { get; set; } = "";

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string IconPath { get; set; } = "";

    public string Cost { get; set; } = "";

    public string Description { get; set; } = "";

    public string Tips { get; set; } = "";

    public string EnchantmentIconPath { get; set; } = "";

    public int Rarity { get; set; }

    public int SellPrice { get; set; }

    public bool CanSell { get; set; }

    public bool Equipped { get; set; }
}

public sealed class DimensionShopViewState
{
    public int Gold { get; set; }

    public int Truth { get; set; }

    public int RefreshPrice { get; set; }

    public int RefreshCount { get; set; }

    public bool CanRefresh { get; set; }

    public IReadOnlyList<DimensionShopItemView> Cards { get; set; } = Array.Empty<DimensionShopItemView>();

    public IReadOnlyList<DimensionShopItemView> Relics { get; set; } = Array.Empty<DimensionShopItemView>();

    public bool RelicPurchaseUsed { get; set; }

    public IReadOnlyList<DimensionShopHeldItemView> HeldCards { get; set; } = Array.Empty<DimensionShopHeldItemView>();

    public IReadOnlyList<DimensionShopHeldItemView> HeldRelics { get; set; } = Array.Empty<DimensionShopHeldItemView>();
}

public static class DimensionShopService
{
    private const int OfferCount = 3;
    private const string RunStateVersion = "2";
    private const string PlayerStateVersion = "2";
    private static readonly object Gate = new();
    private static bool transactionRunning;

    public static bool IsWorldSimulationRun()
    {
        try
        {
            var save = GameSaveManager.GetNowSave() ?? GameEntryUI.selectedSave;
            if (save != null)
            {
                return string.Equals(save.modeType, TerriasIds.NativeNormalModeType, StringComparison.OrdinalIgnoreCase)
                       && !IsFlagSet(save.GameVars, TerriasIds.SolarMemoryModeKey)
                       && !IsFlagSet(save.GameVars, TerriasIds.EndlessSeaModeKey);
            }

            return MapManager.Instance?.ModeMapManager is NormalMapManager
                   && GameSaveManager.GetValue<string>(TerriasIds.SolarMemoryModeKey) != "1"
                   && GameSaveManager.GetValue<string>(TerriasIds.EndlessSeaModeKey) != "1";
        }
        catch
        {
            return false;
        }
    }

    public static bool EnsureRunSnapshot(string source)
    {
        if (!IsWorldSimulationRun())
        {
            return false;
        }

        var save = GameSaveManager.GetNowSave() ?? GameEntryUI.selectedSave;
        if (save == null)
        {
            return false;
        }

        if (!AuraGameDataHostApi.IsNativeCatalogReady)
        {
            TerriasLog.Debug("[DimensionShop] deferred run snapshot until game-data catalog is ready: " + source);
            return false;
        }

        save.GameVars ??= new Dictionary<string, string>();
        if (IsFlagSet(save.GameVars, TerriasIds.DimensionShopRunInitializedKey)
            && save.GameVars.TryGetValue(TerriasIds.DimensionShopRunVersionKey, out var version)
            && string.Equals(version, RunStateVersion, StringComparison.Ordinal))
        {
            return true;
        }

        var cards = BuildCardPool();
        var relics = BuildRelicPool();
        save.GameVars[TerriasIds.DimensionShopRunSeedKey] = string.IsNullOrWhiteSpace(save.Seed)
            ? GameSaveManager.GetSeed().ToString()
            : save.Seed;
        save.GameVars[TerriasIds.DimensionShopCardPoolKey] = JoinIds(cards);
        save.GameVars[TerriasIds.DimensionShopRelicPoolKey] = JoinIds(relics);
        save.GameVars[TerriasIds.DimensionShopRunVersionKey] = RunStateVersion;
        save.GameVars[TerriasIds.DimensionShopRunInitializedKey] = "1";
        TerriasLog.Info("[DimensionShop] run snapshot initialized from "
                       + source
                       + "; cards="
                       + cards.Count
                       + "; relics="
                       + relics.Count
                       + ".");
        return true;
    }

    public static DimensionShopViewState View()
    {
        EnsurePlayerState("View");
        var config = DimensionShopConfigStore.Current;
        var cardIds = CurrentOffers(TerriasIds.DimensionShopCurrentCardsKey);
        var relicIds = CurrentOffers(TerriasIds.DimensionShopCurrentRelicsKey);
        var cardBought = CardBoughtSlots();
        var relicPurchaseUsed = IsPlayerFlagSet(TerriasIds.DimensionShopRelicPurchaseUsedKey);
        var truth = DimensionShopGameApi.TruthBalance();

        return new DimensionShopViewState
        {
            Gold = DimensionShopGameApi.GoldBalance(),
            Truth = truth,
            RefreshPrice = config.RefreshPrice,
            RefreshCount = PlayerInt(TerriasIds.DimensionShopRefreshCountKey),
            CanRefresh = truth >= config.RefreshPrice && (CardPool().Count > 0 || EligibleRelics().Count > 0),
            Cards = Enumerable.Range(0, OfferCount)
                .Select(slot => BuildItem(
                    DataType.Card,
                    cardIds[slot],
                    config.CardPrice,
                    truth,
                    cardBought[slot] ? DimensionShopItemState.Purchased : DimensionShopItemState.Available))
                .ToArray(),
            Relics = Enumerable.Range(0, OfferCount)
                .Select(slot => BuildRelicItem(relicIds[slot], config.RelicPrice, truth, relicPurchaseUsed))
                .ToArray(),
            RelicPurchaseUsed = relicPurchaseUsed,
            HeldCards = BuildHeldCards(),
            HeldRelics = BuildHeldRelics()
        };
    }

    public static bool BuyCard(int slot, out string message)
    {
        lock (Gate)
        {
            if (transactionRunning)
            {
                message = "\u5546\u5e97\u6b63\u5728\u7ed3\u7b97\uff0c\u8bf7\u7a0d\u5019\u3002";
                return false;
            }

            transactionRunning = true;
        }

        try
        {
            EnsurePlayerState("BuyCard");
            if (!IsOfferSlot(slot))
            {
                message = "\u5361\u724c\u8d27\u67b6\u4f4d\u7f6e\u65e0\u6548\u3002";
                return false;
            }

            var cardIds = CurrentOffers(TerriasIds.DimensionShopCurrentCardsKey);
            var boughtSlots = CardBoughtSlots();
            var cardId = cardIds[slot];
            if (string.IsNullOrWhiteSpace(cardId) || boughtSlots[slot])
            {
                message = "\u5f53\u524d\u5361\u724c\u5df2\u65e0\u6cd5\u8d2d\u4e70\u3002";
                return false;
            }

            var price = DimensionShopConfigStore.Current.CardPrice;
            if (!DimensionShopGameApi.TrySpendTruth(price))
            {
                message = "\u771f\u7406\u4e4b\u6676\u4e0d\u8db3\u3002";
                return false;
            }

            boughtSlots[slot] = true;
            SetCardBoughtSlots(boughtSlots);
            if (!DimensionShopGameApi.TryGrantCardToReserve(cardId, out var error))
            {
                boughtSlots[slot] = false;
                SetCardBoughtSlots(boughtSlots);
                DimensionShopGameApi.RefundTruth(price);
                message = error == "reserve is full"
                    ? "\u5361\u724c\u4ed3\u5e93\u5df2\u6ee1\uff0c\u65e0\u6cd5\u8d2d\u4e70\u3002"
                    : "\u5361\u724c\u53d1\u653e\u5931\u8d25\uff0c\u672a\u6263\u9664\u771f\u7406\u4e4b\u6676\u3002";
                return false;
            }

            DimensionShopGameApi.PersistRole("DimensionShop.BuyCard.State");
            message = "\u8d2d\u4e70\u6210\u529f\uff1a" + DisplayName(DataType.Card, cardId);
            return true;
        }
        finally
        {
            lock (Gate)
            {
                transactionRunning = false;
            }
        }
    }

    public static bool BuyRelic(int slot, out string message)
    {
        lock (Gate)
        {
            if (transactionRunning)
            {
                message = "\u5546\u5e97\u6b63\u5728\u7ed3\u7b97\uff0c\u8bf7\u7a0d\u5019\u3002";
                return false;
            }

            transactionRunning = true;
        }

        try
        {
            EnsurePlayerState("BuyRelic");
            if (!IsOfferSlot(slot))
            {
                message = "\u9057\u7269\u8d27\u67b6\u4f4d\u7f6e\u65e0\u6548\u3002";
                return false;
            }

            if (IsPlayerFlagSet(TerriasIds.DimensionShopRelicPurchaseUsedKey))
            {
                message = "\u672c\u5c40\u7684\u9057\u7269\u8d2d\u4e70\u673a\u4f1a\u5df2\u4f7f\u7528\u3002";
                return false;
            }

            var relicId = CurrentOffers(TerriasIds.DimensionShopCurrentRelicsKey)[slot];
            if (string.IsNullOrWhiteSpace(relicId))
            {
                message = "\u5f53\u524d\u6ca1\u6709\u53ef\u8d2d\u4e70\u7684\u9057\u7269\u3002";
                return false;
            }

            if (DimensionShopGameApi.HasRelic(relicId))
            {
                message = "\u4f60\u5df2\u7ecf\u643a\u5e26\u8be5\u9057\u7269\u3002";
                return false;
            }

            var price = DimensionShopConfigStore.Current.RelicPrice;
            if (!DimensionShopGameApi.TrySpendTruth(price))
            {
                message = "\u771f\u7406\u4e4b\u6676\u4e0d\u8db3\u3002";
                return false;
            }

            if (!DimensionShopGameApi.TryGrantRelicToWarehouse(relicId, out _))
            {
                DimensionShopGameApi.RefundTruth(price);
                message = "\u9057\u7269\u53d1\u653e\u5931\u8d25\uff0c\u672a\u6263\u9664\u771f\u7406\u4e4b\u6676\u3002";
                return false;
            }

            SetPlayerValue(TerriasIds.DimensionShopRelicPurchaseUsedKey, "1");
            SetPlayerValue(TerriasIds.DimensionShopPurchasedRelicIdKey, Canonical(relicId));
            SetPlayerValue(TerriasIds.DimensionShopBoughtRelicsKey, Canonical(relicId));
            DimensionShopGameApi.PersistRole("DimensionShop.BuyRelic.State");
            message = "\u8d2d\u4e70\u6210\u529f\uff1a" + DisplayName(DataType.Relic, relicId);
            return true;
        }
        finally
        {
            lock (Gate)
            {
                transactionRunning = false;
            }
        }
    }

    public static bool Refresh(out string message)
    {
        lock (Gate)
        {
            if (transactionRunning)
            {
                message = "\u5546\u5e97\u6b63\u5728\u7ed3\u7b97\uff0c\u8bf7\u7a0d\u5019\u3002";
                return false;
            }

            transactionRunning = true;
        }

        try
        {
            EnsurePlayerState("Refresh");
            var cards = CardPool();
            var relics = EligibleRelics();
            if (cards.Count == 0 && relics.Count == 0)
            {
                message = "\u5f53\u524d\u6ca1\u6709\u53ef\u5237\u65b0\u7684\u5546\u54c1\u3002";
                return false;
            }

            var price = DimensionShopConfigStore.Current.RefreshPrice;
            if (!DimensionShopGameApi.TrySpendTruth(price))
            {
                message = "\u771f\u7406\u4e4b\u6676\u4e0d\u8db3\u3002";
                return false;
            }

            var next = PlayerInt(TerriasIds.DimensionShopRefreshCountKey) + 1;
            var seed = RunSeed() + "|" + DimensionShopGameApi.LocalPlayerScope();
            SetPlayerValue(TerriasIds.DimensionShopRefreshCountKey, next.ToString());
            SetCurrentOffers(
                TerriasIds.DimensionShopCurrentCardsKey,
                DimensionShopRandom.Sample(cards, seed, "refresh.cards", next, OfferCount));
            SetCurrentOffers(
                TerriasIds.DimensionShopCurrentRelicsKey,
                DimensionShopRandom.Sample(relics, seed, "refresh.relics", next, OfferCount));
            SetCardBoughtSlots(new bool[OfferCount]);
            DimensionShopGameApi.PersistRole("DimensionShop.Refresh");
            message = "\u8d27\u67b6\u5df2\u5237\u65b0\u3002";
            return true;
        }
        finally
        {
            lock (Gate)
            {
                transactionRunning = false;
            }
        }
    }

    private static void EnsurePlayerState(string source)
    {
        EnsureRunSnapshot(source);
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        role.SpecialVarMap ??= new Dictionary<string, string>();
        if (role.SpecialVarMap.TryGetValue(TerriasIds.DimensionShopPlayerInitializedKey, out var initialized)
            && initialized == "1"
            && role.SpecialVarMap.TryGetValue(TerriasIds.DimensionShopPlayerVersionKey, out var version)
            && string.Equals(version, PlayerStateVersion, StringComparison.Ordinal))
        {
            return;
        }

        var seed = RunSeed() + "|" + DimensionShopGameApi.LocalPlayerScope();
        var oldCard = PlayerValue(TerriasIds.DimensionShopCurrentCardKey);
        var oldRelic = PlayerValue(TerriasIds.DimensionShopCurrentRelicKey);
        var cards = WithPreferred(
            DimensionShopRandom.Sample(CardPool(), seed, "initial.cards", 0, OfferCount),
            oldCard,
            CardPool());
        var eligibleRelics = EligibleRelics();
        var relics = WithPreferred(
            DimensionShopRandom.Sample(eligibleRelics, seed, "initial.relics", 0, OfferCount),
            oldRelic,
            eligibleRelics);
        SetCurrentOffers(TerriasIds.DimensionShopCurrentCardsKey, cards);
        SetCurrentOffers(TerriasIds.DimensionShopCurrentRelicsKey, relics);

        var migratedCardBought = new bool[OfferCount];
        migratedCardBought[0] = PlayerValue(TerriasIds.DimensionShopCardBoughtKey) == "1"
                                && !string.IsNullOrWhiteSpace(oldCard)
                                && string.Equals(cards[0], oldCard, StringComparison.Ordinal);
        SetCardBoughtSlots(migratedCardBought);
        if (BoughtRelics().Count > 0)
        {
            SetPlayerValue(TerriasIds.DimensionShopRelicPurchaseUsedKey, "1");
            SetPlayerValue(
                TerriasIds.DimensionShopPurchasedRelicIdKey,
                BoughtRelics().OrderBy(id => id, StringComparer.Ordinal).First());
        }
        else
        {
            SetPlayerValue(TerriasIds.DimensionShopRelicPurchaseUsedKey, "0");
            SetPlayerValue(TerriasIds.DimensionShopPurchasedRelicIdKey, "");
        }

        if (!role.SpecialVarMap.ContainsKey(TerriasIds.DimensionShopRefreshCountKey))
        {
            SetPlayerValue(TerriasIds.DimensionShopRefreshCountKey, "0");
        }

        SetPlayerValue(TerriasIds.DimensionShopPlayerVersionKey, PlayerStateVersion);
        SetPlayerValue(TerriasIds.DimensionShopPlayerInitializedKey, "1");
        DimensionShopGameApi.PersistRole("DimensionShop.PlayerInitialize");
    }

    private static DimensionShopItemView BuildRelicItem(string id, int price, int truth, bool purchaseUsed)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return EmptyItem(price, "\u6682\u65e0\u53ef\u8d2d\u4e70\u9057\u7269", DimensionShopItemState.Empty);
        }

        if (purchaseUsed)
        {
            return BuildItem(DataType.Relic, id, price, truth, DimensionShopItemState.SoldOut);
        }

        if (DimensionShopGameApi.HasRelic(id))
        {
            return BuildItem(DataType.Relic, id, price, truth, DimensionShopItemState.Owned);
        }

        return BuildItem(DataType.Relic, id, price, truth, DimensionShopItemState.Available);
    }

    public static bool SellCard(string instanceId, out string message)
    {
        lock (Gate)
        {
            if (transactionRunning)
            {
                message = "\u5546\u5e97\u6b63\u5728\u7ed3\u7b97\uff0c\u8bf7\u7a0d\u5019\u3002";
                return false;
            }

            transactionRunning = true;
        }

        try
        {
            var role = RoleTable.Instance;
            var card = FindHeldCard(role, instanceId);
            if (role == null || card == null)
            {
                message = "\u8be5\u5361\u724c\u5df2\u4e0d\u5728\u80cc\u5305\u4e2d\u3002";
                return false;
            }

            var equipped = role.cardList.Contains(card);
            if (!CanSellCard(role, card, equipped, out message))
            {
                return false;
            }

            var sellPrice = SellDisplayPrice(role, card);
            var baseGold = 20 * Math.Max(1, DictionaryUtil.GetInt(card.data, "Rarity", 1));
            role.cardList.Remove(card);
            role.UnCardList.Remove(card);
            role.Money += baseGold;
            DimensionShopGameApi.PersistRole("DimensionShop.SellCard");
            message = "\u51fa\u552e\u6210\u529f\uff1a" + CardName(card) + "\uff0c\u83b7\u5f97 " + sellPrice + " \u91d1\u5e01\u3002";
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] card sale failed", ex);
            message = "\u5361\u724c\u51fa\u552e\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002";
            return false;
        }
        finally
        {
            lock (Gate)
            {
                transactionRunning = false;
            }
        }
    }

    public static bool SellRelic(string instanceId, out string message)
    {
        lock (Gate)
        {
            if (transactionRunning)
            {
                message = "\u5546\u5e97\u6b63\u5728\u7ed3\u7b97\uff0c\u8bf7\u7a0d\u5019\u3002";
                return false;
            }

            transactionRunning = true;
        }

        try
        {
            var role = RoleTable.Instance;
            var relic = FindHeldRelic(role, instanceId);
            if (role == null || relic == null)
            {
                message = "\u8be5\u9057\u7269\u5df2\u4e0d\u5728\u80cc\u5305\u4e2d\u3002";
                return false;
            }

            var sellPrice = RelicSellDisplayPrice(role, relic);
            var baseGold = 70 * Math.Max(1, DictionaryUtil.GetInt(relic.data, "Rarity", 1));
            role.relicList.Remove(relic);
            role.WithoutArmedRelicList.Remove(relic);
            if (!string.IsNullOrWhiteSpace(relic.InstanceID))
            {
                role.enchasedDict?.Remove(relic.InstanceID);
            }

            role.Money += baseGold;
            DimensionShopGameApi.PersistRole("DimensionShop.SellRelic");
            message = "\u51fa\u552e\u6210\u529f\uff1a"
                      + ItemName(relic)
                      + "\uff0c\u83b7\u5f97 "
                      + sellPrice
                      + " \u91d1\u5e01\u3002";
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] relic sale failed", ex);
            message = "\u9057\u7269\u51fa\u552e\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002";
            return false;
        }
        finally
        {
            lock (Gate)
            {
                transactionRunning = false;
            }
        }
    }

    public static bool UnequipRelic(string instanceId, out string message)
    {
        lock (Gate)
        {
            if (transactionRunning)
            {
                message = "\u5546\u5e97\u6b63\u5728\u7ed3\u7b97\uff0c\u8bf7\u7a0d\u5019\u3002";
                return false;
            }

            transactionRunning = true;
        }

        try
        {
            var role = RoleTable.Instance;
            var relic = role?.relicList.FirstOrDefault(item =>
                string.Equals(item?.InstanceID, instanceId, StringComparison.Ordinal));
            if (role == null || relic == null)
            {
                message = "\u8be5\u9057\u7269\u5df2\u672a\u88c5\u5907\u3002";
                return false;
            }

            role.relicList.Remove(relic);
            role.WithoutArmedRelicList.Add(relic);
            DimensionShopGameApi.PersistRole("DimensionShop.UnequipRelic");
            message = "\u5df2\u8131\u4e0b\u9057\u7269\uff1a" + ItemName(relic) + "\u3002";
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] relic unequip failed", ex);
            message = "\u9057\u7269\u8131\u4e0b\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002";
            return false;
        }
        finally
        {
            lock (Gate)
            {
                transactionRunning = false;
            }
        }
    }

    private static DimensionShopItemView BuildItem(
        DataType type,
        string id,
        int price,
        int truth,
        DimensionShopItemState state)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return EmptyItem(
                price,
                type == DataType.Card ? "\u5361\u724c\u6c60\u4e3a\u7a7a" : "\u9057\u7269\u6c60\u4e3a\u7a7a",
                DimensionShopItemState.Empty);
        }

        var row = TerriasConfigIndex.Row(type, id);
        if (row == null)
        {
            return EmptyItem(price, "\u5546\u54c1\u6570\u636e\u4e0d\u53ef\u7528", DimensionShopItemState.Unavailable);
        }

        var description = Localized(row, "Description");
        var attribute = Localized(row, "AttributeText");
        if (!string.IsNullOrWhiteSpace(attribute) && !description.Contains(attribute))
        {
            description = string.IsNullOrWhiteSpace(description) ? attribute : description + "\n" + attribute;
        }

        var status = StatusText(state);
        if (state == DimensionShopItemState.Available && truth < price)
        {
            state = DimensionShopItemState.InsufficientTruth;
            status = "\u771f\u7406\u4e4b\u6676\u4e0d\u8db3";
        }

        return new DimensionShopItemView
        {
            Id = id,
            Name = Localized(row, "Name", id),
            Description = description,
            IconPath = DictionaryUtil.Get(row, "Icon"),
            Price = price,
            Status = status,
            CanBuy = state == DimensionShopItemState.Available,
            State = state
        };
    }

    private static DimensionShopItemView EmptyItem(int price, string status, DimensionShopItemState state)
    {
        return new DimensionShopItemView
        {
            Price = price,
            Status = status,
            CanBuy = false,
            State = state
        };
    }

    private static IReadOnlyList<DimensionShopHeldItemView> BuildHeldCards()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return Array.Empty<DimensionShopHeldItemView>();
        }

        var result = new List<DimensionShopHeldItemView>();
        AddHeldItems(result, role, role.cardList, equipped: true, DataType.Card);
        AddHeldItems(result, role, role.UnCardList, equipped: false, DataType.Card);
        return result;
    }

    private static IReadOnlyList<DimensionShopHeldItemView> BuildHeldRelics()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return Array.Empty<DimensionShopHeldItemView>();
        }

        var result = new List<DimensionShopHeldItemView>();
        AddHeldItems(result, role, role.relicList, equipped: true, DataType.Relic);
        AddHeldItems(result, role, role.WithoutArmedRelicList, equipped: false, DataType.Relic);
        return result;
    }

    private static void AddHeldItems(
        ICollection<DimensionShopHeldItemView> target,
        RoleTable role,
        IEnumerable<DataConfig>? source,
        bool equipped,
        DataType type)
    {
        if (source == null)
        {
            return;
        }

        foreach (var config in source)
        {
            if (config == null || config.data == null)
            {
                continue;
            }

            var row = config.data;

            var id = DictionaryUtil.Get(row, "Id");
            var rarity = Math.Max(1, DictionaryUtil.GetInt(row, "Rarity", 1));
            var instanceId = config.InstanceID ?? "";
            var enchantmentIcon = "";
            if (!string.IsNullOrWhiteSpace(instanceId)
                && role.enchasedDict != null
                && role.enchasedDict.TryGetValue(instanceId, out var enchantment))
            {
                enchantmentIcon = DictionaryUtil.Get(enchantment?.data, "Icon");
            }

            target.Add(new DimensionShopHeldItemView
            {
                NativeConfig = config,
                InstanceId = instanceId,
                Id = id,
                Name = SafeLocalizedField(config, "Name", id),
                IconPath = DictionaryUtil.Get(row, "Icon"),
                Cost = type == DataType.Card ? DictionaryUtil.Get(row, "Expend", "0") : "",
                Description = SafeItemDescription(config),
                Tips = SafeLocalizedField(config, "Tips"),
                EnchantmentIconPath = enchantmentIcon,
                Rarity = rarity,
                SellPrice = type == DataType.Card
                    ? SellDisplayPrice(role, config)
                    : RelicSellDisplayPrice(role, config),
                CanSell = type == DataType.Relic || CanSellCard(role, config, equipped, out _),
                Equipped = equipped
            });
        }
    }

    private static DataConfig? FindHeldCard(RoleTable? role, string instanceId)
    {
        if (role == null || string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        return role.cardList.FirstOrDefault(card => string.Equals(card?.InstanceID, instanceId, StringComparison.Ordinal))
               ?? role.UnCardList.FirstOrDefault(card => string.Equals(card?.InstanceID, instanceId, StringComparison.Ordinal));
    }

    private static DataConfig? FindHeldRelic(RoleTable? role, string instanceId)
    {
        if (role == null || string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        return role.relicList.FirstOrDefault(relic => string.Equals(relic?.InstanceID, instanceId, StringComparison.Ordinal))
               ?? role.WithoutArmedRelicList.FirstOrDefault(relic => string.Equals(relic?.InstanceID, instanceId, StringComparison.Ordinal));
    }

    private static bool CanSellCard(RoleTable role, DataConfig card, bool equipped, out string message)
    {
        var tags = DictionaryUtil.Get(card?.Vars, "Tag", DictionaryUtil.Get(card?.data, "Tag"));
        if (tags.IndexOf("Eternal", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            message = "\u8be5\u5361\u724c\u65e0\u6cd5\u79fb\u9664\u3002";
            return false;
        }

        if (equipped && role.cardList.Count <= role.CardBottomCount)
        {
            message = "\u4e3b\u724c\u7ec4\u5361\u724c\u6570\u91cf\u5df2\u8fbe\u4e0b\u9650\u3002";
            return false;
        }

        message = "";
        return true;
    }

    private static int SellDisplayPrice(RoleTable role, DataConfig card)
    {
        var rarity = Math.Max(1, DictionaryUtil.GetInt(card?.data, "Rarity", 1));
        return Math.Max(0, (int)(20f * rarity * role.MoneyCal));
    }

    private static int RelicSellDisplayPrice(RoleTable role, DataConfig relic)
    {
        var rarity = Math.Max(1, DictionaryUtil.GetInt(relic?.data, "Rarity", 1));
        return Math.Max(0, (int)(70f * rarity * role.MoneyCal));
    }

    private static string CardName(DataConfig card)
    {
        var id = DictionaryUtil.Get(card?.data, "Id");
        var row = card?.data as Dictionary<string, string>;
        return row == null ? DictionaryUtil.Get(card?.data, "Name", id) : Localized(row, "Name", id);
    }

    private static string ItemName(DataConfig item)
    {
        var id = DictionaryUtil.Get(item?.data, "Id");
        var row = item?.data as Dictionary<string, string>;
        return row == null ? DictionaryUtil.Get(item?.data, "Name", id) : Localized(row, "Name", id);
    }

    private static string SafeItemDescription(DataConfig item)
    {
        try
        {
            return item.Description() ?? "";
        }
        catch
        {
            return DictionaryUtil.Get(item?.data, "Description");
        }
    }

    private static string SafeLocalizedField(DataConfig item, string field, string fallback = "")
    {
        try
        {
            if (item?.data != null && item.data.ContainsKey(field))
            {
                return item.data.Localize(field);
            }
        }
        catch
        {
        }

        return DictionaryUtil.Get(item?.data, field, fallback);
    }

    private static string StatusText(DimensionShopItemState state)
    {
        return state switch
        {
            DimensionShopItemState.Purchased => "\u5df2\u8d2d\u4e70",
            DimensionShopItemState.SoldOut => "\u5df2\u552e\u7f44",
            DimensionShopItemState.Owned => "\u5df2\u62e5\u6709",
            _ => ""
        };
    }

    private static List<string> BuildCardPool()
    {
        var config = DimensionShopConfigStore.Current;
        var packs = new HashSet<string>(config.CardPackIds, StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(config.ExcludeCardIds.Select(CardApi.ResolveCardId), StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in TerriasConfigIndex.Rows(DataType.Card))
        {
            var id = DictionaryUtil.Get(row, "Id");
            var pack = DictionaryUtil.Get(row, "PackBelong");
            if (string.IsNullOrWhiteSpace(id)
                || id.StartsWith("*", StringComparison.Ordinal)
                || !packs.Any(source => CardPackMatches(pack, source)))
            {
                continue;
            }

            var resolved = CardApi.ResolveCardId(id);
            if (!string.IsNullOrWhiteSpace(resolved) && !excluded.Contains(resolved))
            {
                result.Add(resolved);
            }
        }

        foreach (var id in config.IncludeCardIds.Select(CardApi.ResolveCardId))
        {
            if (!string.IsNullOrWhiteSpace(id)
                && !excluded.Contains(id)
                && TerriasConfigIndex.Row(DataType.Card, id) != null)
            {
                result.Add(id);
            }
        }

        return result.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private static List<string> BuildRelicPool()
    {
        return TerriasConfigIndex.Rows(DataType.Relic)
            .Where(row =>
            {
                var rarity = DictionaryUtil.GetInt(row, "Rarity", 0);
                return rarity == 3 || rarity == 4;
            })
            .Select(row => Canonical(DictionaryUtil.Get(row, "Id")))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> CardPool()
    {
        return RunIds(TerriasIds.DimensionShopCardPoolKey, DataType.Card);
    }

    private static List<string> RelicPool()
    {
        return RunIds(TerriasIds.DimensionShopRelicPoolKey, DataType.Relic);
    }

    private static List<string> EligibleRelics()
    {
        return RelicPool()
            .Where(id => !DimensionShopGameApi.HasRelic(id))
            .ToList();
    }

    private static List<string> RunIds(string key, DataType type)
    {
        EnsureRunSnapshot("RunIds:" + key);
        var save = GameSaveManager.GetNowSave() ?? GameEntryUI.selectedSave;
        if (save?.GameVars == null || !save.GameVars.TryGetValue(key, out var value))
        {
            return new List<string>();
        }

        return SplitIds(value)
            .Where(id => TerriasConfigIndex.Row(type, id) != null)
            .ToList();
    }

    private static string RunSeed()
    {
        var save = GameSaveManager.GetNowSave() ?? GameEntryUI.selectedSave;
        if (save?.GameVars != null
            && save.GameVars.TryGetValue(TerriasIds.DimensionShopRunSeedKey, out var seed)
            && !string.IsNullOrWhiteSpace(seed))
        {
            return seed;
        }

        return save?.Seed ?? GameSaveManager.GetSeed().ToString();
    }

    private static List<string> WithPreferred(
        IReadOnlyList<string> sampled,
        string preferred,
        IReadOnlyCollection<string> eligible)
    {
        var result = sampled
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(OfferCount)
            .ToList();
        preferred = Canonical(preferred);
        if (string.IsNullOrWhiteSpace(preferred)
            || !eligible.Contains(preferred))
        {
            return result;
        }

        result.RemoveAll(id => string.Equals(id, preferred, StringComparison.Ordinal));
        result.Insert(0, preferred);
        if (result.Count > OfferCount)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static HashSet<string> BoughtRelics()
    {
        return new HashSet<string>(
            SplitIds(PlayerValue(TerriasIds.DimensionShopBoughtRelicsKey)).Select(Canonical),
            StringComparer.Ordinal);
    }

    private static string PlayerValue(string key)
    {
        var map = RoleTable.Instance?.SpecialVarMap;
        return map != null && map.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static int PlayerInt(string key)
    {
        return DictionaryUtil.ParseInt(PlayerValue(key));
    }

    private static void SetPlayerValue(string key, string value)
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        role.SpecialVarMap ??= new Dictionary<string, string>();
        role.SpecialVarMap[key] = value ?? "";
    }

    private static bool IsPlayerFlagSet(string key)
    {
        return string.Equals(PlayerValue(key), "1", StringComparison.Ordinal);
    }

    private static bool IsOfferSlot(int slot)
    {
        return slot >= 0 && slot < OfferCount;
    }

    private static List<string> CurrentOffers(string key)
    {
        var offers = SplitSlots(PlayerValue(key));
        while (offers.Count < OfferCount)
        {
            offers.Add("");
        }

        return offers.Take(OfferCount).ToList();
    }

    private static void SetCurrentOffers(string key, IEnumerable<string> offers)
    {
        var slots = (offers ?? Array.Empty<string>()).Take(OfferCount).ToList();
        while (slots.Count < OfferCount)
        {
            slots.Add("");
        }

        SetPlayerValue(key, string.Join("|", slots));
    }

    private static bool[] CardBoughtSlots()
    {
        var values = SplitSlots(PlayerValue(TerriasIds.DimensionShopCardBoughtSlotsKey));
        var result = new bool[OfferCount];
        for (var i = 0; i < result.Length && i < values.Count; i++)
        {
            result[i] = string.Equals(values[i], "1", StringComparison.Ordinal);
        }

        return result;
    }

    private static void SetCardBoughtSlots(IReadOnlyList<bool> values)
    {
        SetPlayerValue(
            TerriasIds.DimensionShopCardBoughtSlotsKey,
            string.Join("|", Enumerable.Range(0, OfferCount).Select(index =>
                index < values.Count && values[index] ? "1" : "0")));
    }

    private static string DisplayName(DataType type, string id)
    {
        var row = TerriasConfigIndex.Row(type, id);
        return row == null ? id : Localized(row, "Name", id);
    }

    private static string Localized(Dictionary<string, string> row, string field, string fallback = "")
    {
        try
        {
            var value = row.Localize(field);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            var value = DictionaryUtil.Get(row, field);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }

    private static bool CardPackMatches(string rowPack, string sourcePack)
    {
        if (string.IsNullOrWhiteSpace(rowPack) || string.IsNullOrWhiteSpace(sourcePack))
        {
            return false;
        }

        return TerriasContentIdCompatibility.Equivalent(rowPack, sourcePack);
    }

    private static bool IsFlagSet(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) && value == "1";
    }

    private static string Canonical(string id)
    {
        return DimensionShopGameApi.CanonicalId(id);
    }

    private static string JoinIds(IEnumerable<string> ids)
    {
        return string.Join("|", ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal));
    }

    private static List<string> SplitIds(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
    }

    private static List<string> SplitSlots(string value)
    {
        return string.IsNullOrEmpty(value)
            ? new List<string>()
            : value.Split(new[] { '|' }, StringSplitOptions.None)
                .Select(id => id.Trim())
                .ToList();
    }
}
