using System;
using System.Collections.Generic;
using System.Reflection;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class FightUiCardLayoutApi
{
    private static readonly MethodInfo? UpdateCardItemPosMethod = ResolveUpdateCardItemPos();
    private const int MaxNativeQueueWaitFrames = 360;
    private static FightUI? pendingFightUi;
    private static string pendingSource = "";
    private static int waitFrames;

    public static bool RequestHandLayout(FightUI? fightUi, string source)
    {
        if (fightUi == null)
        {
            return false;
        }

        if (UpdateCardItemPosMethod == null)
        {
            TerriasLog.WarnOnce(
                "FightUiCardLayoutApi.UpdateCardItemPosUnavailable",
                "[FightUiCardLayout] no compatible FightUI.UpdateCardItemPos signature was found.");
            TerriasPerformanceCounters.Record("FightUiCardLayout.SignatureUnavailable");
            return false;
        }

        if (!ReferenceEquals(pendingFightUi, fightUi))
        {
            waitFrames = 0;
        }

        pendingFightUi = fightUi;
        pendingSource = source ?? "";
        return AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
        {
            OwnerId = TerriasIds.ModId,
            Key = "FightUiCardLayout.Apply",
            Source = "Terrias.FightUiCardLayout",
            Phase = AuraSharedFramePhase.Presentation,
            Priority = 80,
            EstimatedCost = 3,
            Action = ApplyScheduled
        });
    }

    public static bool RequestCurrentHandLayout(string source)
    {
        return RequestHandLayout(UIManager.Instance?.GetUI<FightUI>("FightUI"), source);
    }

    private static void ApplyScheduled()
    {
        var fightUi = pendingFightUi;
        if (fightUi == null)
        {
            return;
        }

        if (fightUi.createCardQueue?.Count > 0 && waitFrames < MaxNativeQueueWaitFrames)
        {
            waitFrames++;
            TerriasPerformanceCounters.Record("FightUiCardLayout.WaitedForNativeQueue");
            RequestHandLayout(fightUi, pendingSource + ":native-queue");
            return;
        }

        if (fightUi.createCardQueue?.Count > 0)
        {
            TerriasPerformanceCounters.Record("FightUiCardLayout.NativeQueueTimeout");
            TerriasLog.WarnOnce(
                "FightUiCardLayout.NativeQueueTimeout",
                "[FightUiCardLayout] native card creation queue did not settle before the layout deadline; applying a recovery layout.");
        }

        waitFrames = 0;
        AuditHandList();
        var source = pendingSource;
        pendingFightUi = null;
        pendingSource = "";
        ApplyNow(fightUi, source);
    }

    private static void AuditHandList()
    {
        var seen = new HashSet<int>();
        for (var index = FightUI.cardItemList.Count - 1; index >= 0; index--)
        {
            var card = FightUI.cardItemList[index];
            if (card == null)
            {
                FightUI.cardItemList.RemoveAt(index);
                TerriasPerformanceCounters.Record("FightUiCardLayout.RemovedDestroyedEntry");
                continue;
            }

            var instanceId = card.GetInstanceID();
            if (!seen.Add(instanceId))
            {
                FightUI.cardItemList.RemoveAt(index);
                TerriasPerformanceCounters.Record("FightUiCardLayout.RemovedDuplicateEntry");
            }
        }
    }

    private static bool ApplyNow(FightUI fightUi, string source)
    {
        var method = UpdateCardItemPosMethod;
        if (method == null)
        {
            return false;
        }

        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            var parameterCount = method.GetParameters().Length;
            method.Invoke(
                fightUi,
                parameterCount == 0 ? null : new object?[parameterCount]);
            TerriasPerformanceCounters.Record("FightUiCardLayout.Applied");
            TerriasPerformanceCounters.RecordDuration("FightUiCardLayout.Apply", start);
            return true;
        }
        catch (Exception ex)
        {
            var reason = ex is TargetInvocationException { InnerException: not null }
                ? ex.InnerException!.Message
                : ex.Message;
            TerriasLog.Warn("[FightUiCardLayout] layout failed from " + (source ?? "") + ": " + reason);
            TerriasPerformanceCounters.Record("FightUiCardLayout.Failed");
            return false;
        }
    }

    private static MethodInfo? ResolveUpdateCardItemPos()
    {
        MethodInfo? legacy = null;
        MethodInfo? singleParameter = null;
        foreach (var method in typeof(FightUI).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!string.Equals(method.Name, "UpdateCardItemPos", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 2)
            {
                return method;
            }

            if (parameters.Length == 1 && parameters[0].IsOptional)
            {
                singleParameter = method;
            }
            else if (parameters.Length == 0)
            {
                legacy = method;
            }
        }

        return singleParameter ?? legacy;
    }
}
