using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.GameApi;

public static class DimensionShopGameApi
{
    public static int TruthBalance()
    {
        try
        {
            var runtime = Singleton<GameRuntimeData>.Instance;
            return runtime == null ? 0 : runtime.Truth;
        }
        catch
        {
            return 0;
        }
    }

    public static bool TrySpendTruth(int amount)
    {
        amount = Math.Max(0, amount);
        try
        {
            var runtime = Singleton<GameRuntimeData>.Instance;
            if (runtime == null || runtime.Truth < amount)
            {
                return false;
            }

            runtime.Truth -= amount;
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] truth spend failed: " + ex.Message);
            return false;
        }
    }

    public static void RefundTruth(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        try
        {
            var runtime = Singleton<GameRuntimeData>.Instance;
            if (runtime != null)
            {
                runtime.Truth += amount;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShopGameApi] truth refund failed", ex);
        }
    }

    public static bool TryGrantCardToReserve(string cardId, out string error)
    {
        error = "";
        try
        {
            var role = RoleTable.Instance;
            var resolved = CardApi.ResolveCardId(cardId);
            if (role == null || string.IsNullOrWhiteSpace(resolved))
            {
                error = "card or role is unavailable";
                return false;
            }

            if (role.UnCardList == null || role.UnCardList.Count >= role.MaxAlCardCount)
            {
                error = "reserve is full";
                return false;
            }

            role.UnCardList.Add(new DataConfig(resolved, DataType.Card));
            PersistRole("DimensionShop.Card");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SunExpLog.Warn("[DimensionShopGameApi] card grant failed: " + ex.Message);
            return false;
        }
    }

    public static bool TryGrantRelicToWarehouse(string relicId, out string error)
    {
        error = "";
        try
        {
            var role = RoleTable.Instance;
            if (role?.WithoutArmedRelicList == null || string.IsNullOrWhiteSpace(relicId))
            {
                error = "relic or role is unavailable";
                return false;
            }

            role.WithoutArmedRelicList.Add(new DataConfig(relicId, DataType.Relic));
            PersistRole("DimensionShop.Relic");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SunExpLog.Warn("[DimensionShopGameApi] relic grant failed: " + ex.Message);
            return false;
        }
    }

    public static bool HasRelic(string relicId)
    {
        var expected = CanonicalId(relicId);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        try
        {
            var role = RoleTable.Instance;
            return ContainsRelic(role?.relicList, expected)
                   || ContainsRelic(role?.WithoutArmedRelicList, expected);
        }
        catch
        {
            return false;
        }
    }

    public static string LocalPlayerScope()
    {
        try
        {
            var playerId = PlayerManager.Instance?.PlayerId;
            if (!string.IsNullOrWhiteSpace(playerId))
            {
                return playerId!;
            }
        }
        catch
        {
        }

        return RoleTable.Instance?.Id ?? "solo";
    }

    public static void CloseMapUi()
    {
        try
        {
            MapManager.Instance?.CloseMapUI();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] map UI close failed: " + ex.Message);
        }
    }

    public static void AdvanceMap()
    {
        try
        {
            MapManager.Instance?.TryChange();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShopGameApi] map advance failed", ex);
        }
    }

    public static void PersistRole(string source)
    {
        try
        {
            if (RoleTable.Instance != null)
            {
                GameSaveManager.UpdateRoles(RoleTable.Instance);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] role persist skipped from " + source + ": " + ex.Message);
        }
    }

    public static string CanonicalId(string? value)
    {
        return (value ?? "").Trim().TrimStart('*').Replace("_*", "_");
    }

    private static bool ContainsRelic(IEnumerable<DataConfig>? relics, string expected)
    {
        return relics != null && relics.Any(relic =>
            string.Equals(CanonicalId(DictionaryUtil.Get(relic?.data, "Id")), expected, StringComparison.Ordinal));
    }
}
