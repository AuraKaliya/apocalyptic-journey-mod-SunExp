using System;

namespace Terrias.Dll.Infrastructure;

public sealed class TerriasDirtyState
{
    private string lastKey = "";
    private bool hasLastKey;

    public bool ShouldRefresh(string? key)
    {
        var normalized = key ?? "";
        if (hasLastKey && string.Equals(lastKey, normalized, StringComparison.Ordinal))
        {
            TerriasPerformanceCounters.Record("DirtyState.Skipped");
            return false;
        }

        lastKey = normalized;
        hasLastKey = true;
        TerriasPerformanceCounters.Record("DirtyState.Refreshed");
        return true;
    }

    public void Reset()
    {
        lastKey = "";
        hasLastKey = false;
    }
}
