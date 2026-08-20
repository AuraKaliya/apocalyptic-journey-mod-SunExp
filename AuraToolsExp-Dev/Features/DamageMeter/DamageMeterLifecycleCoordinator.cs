using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterLifecycleCoordinator
{
    private static bool endingSent;
    private static int lastRoundStartFrame = -10000;
    private static object? lastRoundUnit;

    internal static void MarkEndingSent() => endingSent = true;

    internal static void ResetRoundDeduplication()
    {
        lastRoundStartFrame = -10000;
        lastRoundUnit = null;
    }

    internal static void ResetCaptureServices()
    {
        DamageCaptureCoordinator.ResetSession();
        DamageCaptureCoordinator.ResetAttribution();
        DamageMeterFightIndex.Clear();
        ResetRoundDeduplication();
    }

    internal static void OnFightInitStarting(bool supportedContext)
    {
        DamageMeterHookAdapter.RunHook("fight init", () =>
        {
            if (supportedContext)
            {
                DamageMeterAvailabilityRuntime.PreparationUiActive = false;
                DamageMeterAvailabilityRuntime.SetAvailable(true, "FightInit.Init");
                DamageMeterSettlementRuntime.PrepareSettlementCgAssets("FightInit.Init");
            }

            ResetCaptureServices();
            DamageMeterFightIndex.SetFriendlyIdentitySnapshots(DamageMeterSettlementRuntime.AdventureTeamMembers);
            DamageMeterFightIndex.BeginFight();
            DamageCaptureCoordinator.AttachBuffBroadcastListener();
            endingSent = false;
            DamageMeterNetworkRuntime.StartFight(AuraToolsDamageMeterRuntime.Enabled);
            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }

    internal static void OnFightStartFallback()
    {
        DamageMeterHookAdapter.RunHook("fight start fallback", () =>
        {
            if (!AuraToolsDamageMeterRuntime.Ledger.InFight)
            {
                if (DamageMeterAvailabilityRuntime.IsActiveDamageMeterContext())
                {
                    DamageMeterAvailabilityRuntime.SetAvailable(true, "Fight_Start.Init");
                }

                ResetCaptureServices();
                DamageMeterFightIndex.SetFriendlyIdentitySnapshots(DamageMeterSettlementRuntime.AdventureTeamMembers);
                DamageMeterFightIndex.BeginFight();
                DamageCaptureCoordinator.AttachBuffBroadcastListener();
                endingSent = false;
                DamageMeterNetworkRuntime.StartFight(AuraToolsDamageMeterRuntime.Enabled);
                AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
            }
        });
    }

    internal static void OnPlayerRoundStart(object? roundUnit)
    {
        DamageMeterHookAdapter.RunHook("round start", () =>
        {
            if (roundUnit != null && ReferenceEquals(lastRoundUnit, roundUnit))
            {
                return;
            }

            if (Time.frameCount - lastRoundStartFrame <= 5)
            {
                return;
            }

            lastRoundUnit = roundUnit;
            lastRoundStartFrame = Time.frameCount;
            DamageMeterNetworkRuntime.StartRound();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }

    internal static void OnFightEnding(string fightResult)
    {
        DamageMeterHookAdapter.RunHook("fight ending", () =>
        {
            if (!endingSent)
            {
                endingSent = true;
                DamageMeterNetworkRuntime.EndFight(fightResult);
            }

            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }

    internal static void OnFightRestarting()
    {
        DamageMeterHookAdapter.RunHook("fight restarting", () =>
        {
            ResetCaptureServices();
            endingSent = false;
            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }

    internal static void OnFightEnded()
    {
        DamageMeterHookAdapter.RunHook("fight ended", () =>
        {
            ResetCaptureServices();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }
}
