using System;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.DamageMeter;

public static class AuraToolsDamageMeterRuntime
{
    private static bool initialized;
    private static float nextRefreshAt;
    private static float uiRetryBlockedUntil;
    private static float nextUiFailureLogAt;
    private static bool disabledUiHidden;
    private static bool uiDirty = true;

    public static bool Visible { get; private set; }

    public static bool Available => DamageMeterAvailabilityRuntime.Available;

    public static bool Enabled => AuraToolsConfigService.MatchExperience.MatchRecords.Enabled
                                  && AuraToolsConfigService.MatchExperience.MatchRecords.Statistics.Enabled;

    internal static DamageLedger Ledger => DamageMeterNetworkRuntime.Ledger;

    internal static DamageRunLedger RunAggregate => DamageMeterNetworkRuntime.RunAggregate;

    internal static DamageHistoryStore History => DamageMeterNetworkRuntime.History;

    internal static string NetworkStatus
    {
        get
        {
            if (!DamageMeterNetworkRuntime.IsMultiplayer)
            {
                return "单机统计";
            }

            if (!Ledger.SharedEnabled)
            {
                return "联机统计未启用";
            }

            return "主机同步 #" + Ledger.ServerSequence;
        }
    }

    public static int OutOfRunHistoryCount => DamageMeterSettlementRuntime.OutOfRunHistoryCount;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.DamageStatistics,
            OnConfigChanged);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.FileLogging,
            DamageMeterHookAdapter.EnsureHooksMatchConfig);
        DamageMeterHookAdapter.Initialize(modConfig);
        AuraToolsLog.Info("[DamageMeter] DPT runtime initialized. Network protocol v"
                          + DamageMeterProtocol.Version + ".");
    }

    public static void Tick()
    {
        if (!Enabled)
        {
            HideDisabledUiSafe();
            return;
        }

        disabledUiHidden = false;
        DamageMeterNetworkRuntime.Tick();
        DamageMeterAvailabilityRuntime.ReconcileAvailabilitySafe();
        RefreshUiSafe();
        DamageMeterPerformanceCounters.MaybeLog();
    }

    private static void HideDisabledUiSafe()
    {
        var now = Time.unscaledTime;
        if (disabledUiHidden || now < uiRetryBlockedUntil)
        {
            return;
        }

        try
        {
            SetVisible(false);
            disabledUiHidden = true;
        }
        catch (Exception ex)
        {
            uiRetryBlockedUntil = now + 1f;
            LogUiFailure("disabled UI cleanup", ex);
        }
    }

    private static void RefreshUiSafe()
    {
        var now = Time.unscaledTime;
        var refreshInterval = Math.Max(
            0.1f,
            AuraToolsConfigService.MatchExperience.DamageMeter.UiRefreshIntervalMs / 1000f);
        if (now < uiRetryBlockedUntil || now < nextRefreshAt)
        {
            return;
        }

        if (!uiDirty && !Ledger.InFight)
        {
            nextRefreshAt = now + refreshInterval;
            return;
        }

        if (!Available || !Visible)
        {
            nextRefreshAt = now + refreshInterval;
            uiDirty = false;
            return;
        }

        try
        {
            var startedAt = DamageMeterPerformanceCounters.StartSample();
            nextRefreshAt = now + refreshInterval;
            uiDirty = false;
            AuraToolsDamageMeterUi.Refresh(
                Ledger,
                RunAggregate,
                History,
                AuraToolsConfigService.MatchExperience.DamageMeter,
                NetworkStatus);
            DamageMeterPerformanceCounters.RecordUiRefresh(
                DamageMeterPerformanceCounters.ElapsedMs(startedAt),
                Ledger.Combatants.Count,
                Ledger.InFight);
        }
        catch (Exception ex)
        {
            uiDirty = true;
            uiRetryBlockedUntil = now + 1f;
            LogUiFailure("UI refresh", ex);
        }
    }

    internal static void LogUiFailure(string operation, Exception ex)
    {
        var now = Time.unscaledTime;
        if (now >= nextUiFailureLogAt)
        {
            nextUiFailureLogAt = now + 10f;
            AuraToolsLog.Warn("[DamageMeter] " + operation + " failed: " + ex.Message);
        }
    }

    public static void SetVisible(bool visible)
    {
        Visible = visible;
        uiDirty = true;
        AuraToolsDamageMeterUi.SetVisible(visible && Enabled && Available);
        if (!visible)
        {
            AuraToolsDamageMeterUi.CloseDetails();
        }
    }

    internal static void SetVisibleFromAvailability(bool visible)
    {
        Visible = visible;
        uiDirty = true;
    }

    public static bool ReportDamage(DamageEvent damage)
    {
        if (damage == null || !Ledger.InFight || !Ledger.SharedEnabled)
        {
            return false;
        }

        DamageEventFactory.Normalize(damage);
        DamageMeterNetworkRuntime.Submit(damage);
        return true;
    }

    public static void OpenOutOfRunHistory() => DamageMeterSettlementRuntime.OpenOutOfRunHistory();

    public static void ClearOutOfRunHistory() => DamageMeterSettlementRuntime.ClearOutOfRunHistory();

    internal static void NotifyLedgerChanged() => uiDirty = true;

    private static void OnConfigChanged()
    {
        uiDirty = true;
        DamageMeterHookAdapter.EnsureHooksMatchConfig();
        if (!Enabled)
        {
            try
            {
                SetVisible(false);
                disabledUiHidden = true;
            }
            catch (Exception ex)
            {
                disabledUiHidden = false;
                uiRetryBlockedUntil = Time.unscaledTime + 1f;
                LogUiFailure("configuration UI cleanup", ex);
            }
        }
        else
        {
            AuraToolsDamageMeterUi.SetAvailable(Available);
            AuraToolsDamageMeterUi.SetVisible(Visible && Available);
        }
    }
}
