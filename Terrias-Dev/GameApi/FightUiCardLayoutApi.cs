using System;
using System.Reflection;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

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
            TerriasLog.WarnOnce(
                "FightUiCardLayoutApi.UpdateCardItemPosUnavailable",
                "[FightUiCardLayout] no compatible FightUI.UpdateCardItemPos signature was found.");
            TerriasPerformanceCounters.Record("FightUiCardLayout.SignatureUnavailable");
            return false;
        }

        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            var parameterCount = UpdateCardItemPosMethod.GetParameters().Length;
            UpdateCardItemPosMethod.Invoke(
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
