using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraUi.Shared;
using Data.Save;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.Events;
using Witch;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class DimensionShopGameApi
{
    private const string TruthCurrencyResourcePath = "Icon/UI_Icons/Native/Icon/\u771f\u7406\u4e4b\u6676";
    private const string TruthCurrencyFallbackResourcePath = "Icon/\u6210\u5c31/\u771f\u7406\u4e4b\u6676";

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

    public static int GoldBalance()
    {
        try
        {
            return RoleTable.Instance == null ? 0 : RoleTable.Instance.Money;
        }
        catch
        {
            return 0;
        }
    }

    public static Sprite? TruthCurrencySprite()
    {
        try
        {
            return SunExpResourceCache.Load<Sprite>(
                TruthCurrencyResourcePath,
                loadFromMod: false,
                category: "dimension.shop.truth.currency")
                   ?? SunExpResourceCache.Load<Sprite>(
                       TruthCurrencyFallbackResourcePath,
                       loadFromMod: false,
                       category: "dimension.shop.truth.currency.fallback");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] truth currency icon lookup failed: " + ex.Message);
            return null;
        }
    }

    public static bool ShowCardSellMenu(Transform anchor, string label, Action sell)
    {
        try
        {
            var manager = UIManager.Instance;
            var window = manager?.GetFloatingWindow();
            if (window == null || anchor == null)
            {
                return false;
            }

            if (!AuraUiNativeOverlayVisibility.SharesRootCanvas(
                    anchor,
                    window.transform,
                    out var canvasDiagnostic))
            {
                SunExpLog.Warn("[DimensionShopGameApi] card sell menu rejected because its native Floating Window is on a different Canvas: "
                               + canvasDiagnostic
                               + ".");
                return false;
            }

            window.Hide();
            window.Clear();
            manager?.GetTooltip()?.Hide();
            window.AddButton(label ?? "", new UnityAction(sell));
            window.Show(anchor);
            ScheduleNativeOverlayVerification(
                "floating-card",
                anchor,
                () => window.gameObject);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] card sell menu failed: " + ex.Message);
            return false;
        }
    }

    public static bool ShowRelicMenu(
        Transform anchor,
        string sellLabel,
        Action sell,
        bool equipped,
        string unequipLabel,
        Action unequip)
    {
        try
        {
            var manager = UIManager.Instance;
            var window = manager?.GetFloatingWindow();
            if (window == null || anchor == null)
            {
                return false;
            }

            if (!AuraUiNativeOverlayVisibility.SharesRootCanvas(
                    anchor,
                    window.transform,
                    out var canvasDiagnostic))
            {
                SunExpLog.Warn("[DimensionShopGameApi] relic action menu rejected because its native Floating Window is on a different Canvas: "
                               + canvasDiagnostic
                               + ".");
                return false;
            }

            window.Hide();
            window.Clear();
            manager?.GetTooltip()?.Hide();
            window.AddButton(sellLabel ?? "", new UnityAction(sell));
            if (equipped)
            {
                window.AddButton(unequipLabel ?? "", new UnityAction(unequip));
            }

            window.Show(anchor);
            ScheduleNativeOverlayVerification(
                "floating-relic",
                anchor,
                () => window.gameObject);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] relic action menu failed: " + ex.Message);
            return false;
        }
    }

    public static void HideFloatingWindow()
    {
        try
        {
            UIManager.Instance?.GetFloatingWindow()?.Hide();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] floating window hide failed: " + ex.Message);
        }
    }

    public static void HideTooltip()
    {
        try
        {
            UIManager.Instance?.GetTooltip()?.Hide();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] tooltip hide failed: " + ex.Message);
        }
    }

    public static void VerifyTooltipVisible(string kind, Transform anchor)
    {
        ScheduleNativeOverlayVerification(
            "tooltip-" + (kind ?? "unknown"),
            anchor,
            () => UIManager.Instance?.GetTooltip()?.gameObject);
    }

    private static void ScheduleNativeOverlayVerification(
        string kind,
        Transform anchor,
        Func<GameObject?> resolveOverlay)
    {
        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = "SunExp.DimensionShop",
            Key = "native-overlay-visibility-" + kind,
            Source = "DimensionShop.NativeOverlay." + kind,
            DelayFrames = 6,
            Phase = AuraSharedFramePhase.Presentation,
            Action = () =>
            {
                var overlay = resolveOverlay();
                if (AuraUiNativeOverlayVisibility.IsVisibleAbove(anchor, overlay, out var diagnostic))
                {
                    SunExpLog.InfoOnceAlways(
                        "dimension-shop-native-overlay-visible-" + kind,
                        "[DimensionShop] native overlay verified visible: kind="
                        + kind
                        + ", "
                        + diagnostic
                        + ".");
                    return;
                }

                SunExpLog.WarnOnce(
                    "dimension-shop-native-overlay-not-visible-" + kind,
                    "[DimensionShop] native overlay was invoked but not verified visible: kind="
                    + kind
                    + ", "
                    + diagnostic
                    + ".");
            }
        });
    }

    public static void CloseNativeBreakFallback()
    {
        try
        {
            var breakRoot = GameObject.Find("Breaks");
            if (breakRoot != null)
            {
                UnityEngine.Object.Destroy(breakRoot);
            }

            var background = GameApp.Instance?.NowBackground;
            if (background != null)
            {
                background.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DimensionShopGameApi] native break fallback cleanup failed: " + ex.Message);
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
