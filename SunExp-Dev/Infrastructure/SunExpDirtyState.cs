using System;

namespace SunExp.Dll.Infrastructure;

public sealed class SunExpDirtyState
{
    private string lastKey = "";
    private bool hasLastKey;

    public bool ShouldRefresh(string? key)
    {
        var normalized = key ?? "";
        if (hasLastKey && string.Equals(lastKey, normalized, StringComparison.Ordinal))
        {
            SunExpPerformanceCounters.Record("DirtyState.Skipped");
            return false;
        }

        lastKey = normalized;
        hasLastKey = true;
        SunExpPerformanceCounters.Record("DirtyState.Refreshed");
        return true;
    }

    public void Reset()
    {
        lastKey = "";
        hasLastKey = false;
    }
}
