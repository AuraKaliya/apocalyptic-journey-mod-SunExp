using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AuraGameData.Shared.Application;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraUi.Shared;
using Data.Save;
using Mirror;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.Events;
using Witch;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class DimensionShopGameApi
{
    private static readonly object RolePersistGate = new();
    private static bool rolePersistPending;
    private static string pendingRolePersistSource = "";
    private static long nativeOverlayGeneration;

    public static bool HasPendingRolePersist
    {
        get
        {
            lock (RolePersistGate)
            {
                return rolePersistPending;
            }
        }
    }

    public static int TruthBalance()
    {
        return TruthCurrencyApi.Balance();
    }

    public static bool TrySpendTruth(int amount)
    {
        return TruthCurrencyApi.TrySpend(amount);
    }

    public static void RefundTruth(int amount)
    {
        TruthCurrencyApi.Refund(amount);
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

            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, resolved);
            if (handle == null)
            {
                error = "registered card definition is unavailable";
                return false;
            }

            var result = new AuraCardInstanceService(new WitchCardInstancePort())
                .GrantToReserveDeck(new AuraCardGrantCommand
                {
                    Definition = handle,
                    Context = new AuraGameMutationContext
                    {
                        RequesterModId = TerriasIds.ModId,
                        Source = "DimensionShop.Card",
                        Authoritative = true
                    }
                });
            error = result.Success ? "" : result.FailureStep + ": " + result.Message;
            return result.Success;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            TerriasLog.Warn("[DimensionShopGameApi] card grant failed: " + ex.Message);
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

            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Relic, relicId);
            if (handle == null)
            {
                error = "registered relic definition is unavailable";
                return false;
            }

            var result = new AuraRelicInstanceService(new WitchRelicInstancePort())
                .Grant(new AuraRelicGrantCommand
                {
                    Definition = handle,
                    PreferEquippedSlot = false,
                    Context = new AuraGameMutationContext
                    {
                        RequesterModId = TerriasIds.ModId,
                        Source = "DimensionShop.Relic",
                        Authoritative = true
                    }
                });
            error = result.Success ? "" : result.FailureStep + ": " + result.Message;
            return result.Success;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            TerriasLog.Warn("[DimensionShopGameApi] relic grant failed: " + ex.Message);
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
            TerriasLog.Warn("[DimensionShopGameApi] map UI close failed: " + ex.Message);
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
        return TruthCurrencyApi.CurrencySprite();
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
                TerriasLog.Warn("[DimensionShopGameApi] card sell menu rejected because its native Floating Window is on a different Canvas: "
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
            TerriasLog.Warn("[DimensionShopGameApi] card sell menu failed: " + ex.Message);
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
                TerriasLog.Warn("[DimensionShopGameApi] relic action menu rejected because its native Floating Window is on a different Canvas: "
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
            TerriasLog.Warn("[DimensionShopGameApi] relic action menu failed: " + ex.Message);
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
            TerriasLog.Warn("[DimensionShopGameApi] floating window hide failed: " + ex.Message);
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
            TerriasLog.Warn("[DimensionShopGameApi] tooltip hide failed: " + ex.Message);
        }
    }

    public static long BeginNativeOverlayGeneration()
    {
        var generation = Interlocked.Increment(ref nativeOverlayGeneration);
        HideTooltip();
        return generation;
    }

    public static void VerifyTooltipVisible(string kind, KeywordDisplay tooltip, long generation)
    {
        ScheduleNativeOverlayVerification(
            "tooltip-" + (kind ?? "unknown"),
            tooltip == null ? null : tooltip.transform,
            () => UIManager.Instance?.GetTooltip()?.gameObject,
            () => TooltipVerificationCancellationReason(tooltip, generation));
    }

    private static void ScheduleNativeOverlayVerification(
        string kind,
        Transform? anchor,
        Func<GameObject?> resolveOverlay,
        Func<string>? cancellationReason = null)
    {
        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = "Terrias.DimensionShop",
            Key = "native-overlay-visibility-" + kind,
            Source = "DimensionShop.NativeOverlay." + kind,
            DelayFrames = 6,
            Phase = AuraSharedFramePhase.Presentation,
            Action = () =>
            {
                var cancellation = cancellationReason?.Invoke() ?? "";
                if (!string.IsNullOrWhiteSpace(cancellation))
                {
                    TerriasLog.DebugOnce(
                        "dimension-shop-native-overlay-cancelled-" + kind + "-" + cancellation,
                        "[DimensionShop] native overlay verification cancelled: kind="
                        + kind
                        + ", reason="
                        + cancellation
                        + ".");
                    return;
                }

                var overlay = resolveOverlay();
                if (AuraUiNativeOverlayVisibility.IsVisibleAbove(anchor, overlay, out var diagnostic))
                {
                    TerriasLog.InfoOnceAlways(
                        "dimension-shop-native-overlay-visible-" + kind,
                        "[DimensionShop] native overlay verified visible: kind="
                        + kind
                        + ", "
                        + diagnostic
                        + ".");
                    return;
                }

                TerriasLog.WarnOnce(
                    "dimension-shop-native-overlay-not-visible-" + kind,
                    "[DimensionShop] native overlay was invoked but not verified visible: kind="
                    + kind
                    + ", "
                    + diagnostic
                    + ".");
            }
        });
    }

    private static string TooltipVerificationCancellationReason(KeywordDisplay? tooltip, long generation)
    {
        if (generation != Interlocked.Read(ref nativeOverlayGeneration))
        {
            return "render-generation-changed";
        }

        if (tooltip == null)
        {
            return "anchor-destroyed";
        }

        if (!tooltip.isActiveAndEnabled || !tooltip.gameObject.activeInHierarchy)
        {
            return "anchor-inactive";
        }

        return tooltip.isHover ? "" : "hover-ended";
    }

    public static void AdvanceMap()
    {
        try
        {
            MapManager.Instance?.TryChange();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShopGameApi] map advance failed", ex);
        }
    }

    public static bool PersistRole(string source)
    {
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "DimensionShop.Unknown" : source.Trim();
        try
        {
            var role = RoleTable.Instance;
            if (role == null)
            {
                DeferRolePersist(normalizedSource, "local role is unavailable");
                return false;
            }

            if (NetworkClient.active)
            {
                var playerManager = PlayerManager.Instance;
                if (playerManager == null)
                {
                    DeferRolePersist(normalizedSource, "network player manager is unavailable");
                    return false;
                }

                playerManager.CmdSyncRoleTable(role);
                ClearPendingRolePersist();
                return true;
            }

            var save = GameSaveManager.GetNowSave();
            if (save?.roleTable == null)
            {
                DeferRolePersist(normalizedSource, "local save role table is unavailable");
                return false;
            }

            GameSaveManager.UpdateRoles(role);
            ClearPendingRolePersist();
            return true;
        }
        catch (Exception ex)
        {
            DeferRolePersist(normalizedSource, ex.Message);
            return false;
        }
    }

    public static bool FlushPendingRolePersist(string source)
    {
        string pendingSource;
        lock (RolePersistGate)
        {
            if (!rolePersistPending)
            {
                return true;
            }

            pendingSource = pendingRolePersistSource;
        }

        var retrySource = string.IsNullOrWhiteSpace(source) ? "DimensionShop.PendingRetry" : source.Trim();
        return PersistRole(pendingSource + " via " + retrySource);
    }

    private static void DeferRolePersist(string source, string reason)
    {
        var shouldLog = false;
        lock (RolePersistGate)
        {
            shouldLog = !rolePersistPending;
            rolePersistPending = true;
            if (shouldLog)
            {
                pendingRolePersistSource = source;
            }
        }

        if (shouldLog)
        {
            TerriasLog.Warn("[DimensionShopGameApi] role persist deferred from "
                           + source
                           + ": "
                           + reason
                           + ".");
        }
    }

    private static void ClearPendingRolePersist()
    {
        lock (RolePersistGate)
        {
            rolePersistPending = false;
            pendingRolePersistSource = "";
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
