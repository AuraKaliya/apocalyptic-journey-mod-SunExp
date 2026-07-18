using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using UnityEngine;
using Witch.Core;

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

    internal static void OnFightInitStarting(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("fight init", () =>
        {
            if (DamageMeterAvailabilityRuntime.IsSupportedDamageMeterContext(context, allowMapManagerFallback: true))
            {
                DamageMeterAvailabilityRuntime.PreparationUiActive = false;
                DamageMeterAvailabilityRuntime.SetAvailable(true, "FightInit.Init");
                DamageMeterSettlementRuntime.PrepareSettlementCgAssets("FightInit.Init");
            }

            DamageCaptureCoordinator.ResetCaptureState();
            DamageMeterFightIndex.SetFriendlyIdentitySnapshots(DamageMeterSettlementRuntime.AdventureTeamMembers);
            DamageMeterFightIndex.BeginFight();
            DamageCaptureCoordinator.AttachBuffBroadcastListener(context);
            endingSent = false;
            DamageMeterNetworkRuntime.StartFight(AuraToolsDamageMeterRuntime.Enabled);
            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }

    internal static void OnFightStartFallback(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("fight start fallback", () =>
        {
            if (!AuraToolsDamageMeterRuntime.Ledger.InFight)
            {
                if (DamageMeterAvailabilityRuntime.IsActiveDamageMeterContext())
                {
                    DamageMeterAvailabilityRuntime.SetAvailable(true, "Fight_Start.Init");
                }

                DamageCaptureCoordinator.ResetCaptureState();
                DamageMeterFightIndex.SetFriendlyIdentitySnapshots(DamageMeterSettlementRuntime.AdventureTeamMembers);
                DamageMeterFightIndex.BeginFight();
                DamageCaptureCoordinator.AttachBuffBroadcastListener(context);
                endingSent = false;
                DamageMeterNetworkRuntime.StartFight(AuraToolsDamageMeterRuntime.Enabled);
                AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
            }
        });
    }

    internal static void OnPlayerRoundStart(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("round start", () =>
        {
            if (context.Target != null && ReferenceEquals(lastRoundUnit, context.Target))
            {
                return;
            }

            if (Time.frameCount - lastRoundStartFrame <= 5)
            {
                return;
            }

            lastRoundUnit = context.Target;
            lastRoundStartFrame = Time.frameCount;
            DamageMeterNetworkRuntime.StartRound();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }

    internal static void OnFightEnding(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("fight ending", () =>
        {
            if (!endingSent)
            {
                endingSent = true;
                DamageMeterNetworkRuntime.EndFight(DamageMeterSettlementRuntime.FightResult(context));
            }

            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }

    internal static void OnFightEnded(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("fight ended", () =>
        {
            DamageCaptureCoordinator.ResetCaptureState();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        });
    }
}
