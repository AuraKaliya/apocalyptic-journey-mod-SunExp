using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public readonly struct BurnTriggerSnapshot
{
    public BurnTriggerSnapshot(IStatusManager target, int stacksAtTrigger, string source)
    {
        Target = target;
        StacksAtTrigger = Math.Max(0, stacksAtTrigger);
        Source = source ?? "";
    }

    public IStatusManager Target { get; }

    public int StacksAtTrigger { get; }

    public string Source { get; }
}

/// <summary>Single semantic entry emitted only after Burn is actually executed.</summary>
public static class BurnTriggerApi
{
    public static event Action<BurnTriggerSnapshot>? Triggered;

    [ThreadStatic]
    private static int immediateExecutionDepth;

    public static void ExecuteImmediate(Action action)
    {
        if (action == null)
        {
            return;
        }

        immediateExecutionDepth++;
        try
        {
            action();
        }
        finally
        {
            immediateExecutionDepth = Math.Max(0, immediateExecutionDepth - 1);
        }
    }

    public static void NotifyActual(IStatusManager? target, int stacksAtTrigger, string source)
    {
        if (target == null || stacksAtTrigger <= 0)
        {
            return;
        }

        if (immediateExecutionDepth > 0
            && string.Equals(source, "NativeBurnStartRound", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            Triggered?.Invoke(new BurnTriggerSnapshot(target, stacksAtTrigger, source));
            TerriasPerformanceCounters.Record("BurnTrigger.Actual");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Burn actual-trigger subscriber failed from " + source, ex);
        }
    }
}
