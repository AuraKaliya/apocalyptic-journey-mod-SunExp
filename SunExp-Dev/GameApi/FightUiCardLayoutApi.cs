using System;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class FightUiCardLayoutApi
{
    private static readonly MethodInfo? UpdateCardItemPosMethod = ResolveUpdateCardItemPos();

    public static bool RequestHandLayout(FightUI? fightUi, string source)
    {
        if (fightUi == null)
        {
            return false;
        }

        if (UpdateCardItemPosMethod == null)
        {
            SunExpLog.WarnOnce(
                "FightUiCardLayoutApi.UpdateCardItemPosUnavailable",
                "[FightUiCardLayout] no compatible FightUI.UpdateCardItemPos signature was found.");
            SunExpPerformanceCounters.Record("FightUiCardLayout.SignatureUnavailable");
            return false;
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var parameterCount = UpdateCardItemPosMethod.GetParameters().Length;
            UpdateCardItemPosMethod.Invoke(
                fightUi,
                parameterCount == 0 ? null : new object?[parameterCount]);
            SunExpPerformanceCounters.Record("FightUiCardLayout.Applied");
            SunExpPerformanceCounters.RecordDuration("FightUiCardLayout.Apply", start);
            return true;
        }
        catch (Exception ex)
        {
            var reason = ex is TargetInvocationException { InnerException: not null }
                ? ex.InnerException!.Message
                : ex.Message;
            SunExpLog.Warn("[FightUiCardLayout] layout failed from " + (source ?? "") + ": " + reason);
            SunExpPerformanceCounters.Record("FightUiCardLayout.Failed");
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
